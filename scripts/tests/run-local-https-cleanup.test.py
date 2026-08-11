#!/usr/bin/env python3

from __future__ import annotations

import json
import os
from pathlib import Path
import signal
import subprocess
import tempfile
import time


REPO_ROOT = Path(__file__).resolve().parents[2]
LAUNCHER = REPO_ROOT / "scripts" / "run-local-https.sh"


def write_executable(path: Path, contents: str) -> None:
    path.write_text(contents, encoding="utf-8")
    path.chmod(0o755)


def process_is_alive(pid: int) -> bool:
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        return False
    return True


def wait_for_files(
    paths: list[Path], process: subprocess.Popen[str], log_path: Path
) -> None:
    deadline = time.monotonic() + 10
    while time.monotonic() < deadline:
        if process.poll() is not None:
            raise AssertionError(
                "launcher exited before starting test children: "
                f"{process.returncode}\n{log_path.read_text(encoding='utf-8')}"
            )
        if all(path.exists() for path in paths):
            return
        time.sleep(0.05)
    raise AssertionError("launcher did not start both test children")


def wait_until_stopped(pid: int) -> bool:
    deadline = time.monotonic() + 3
    while time.monotonic() < deadline:
        if not process_is_alive(pid):
            return True
        time.sleep(0.05)
    return False


with tempfile.TemporaryDirectory(prefix="run-local-https-test.") as directory:
    temp = Path(directory)
    fake_bin = temp / "bin"
    pid_directory = temp / "pids"
    fake_bin.mkdir()
    pid_directory.mkdir()

    settings = temp / "appsettings.Local.json"
    settings.write_text(
        json.dumps(
            {
                "ConnectionStrings": {"Postgres": "Host=test"},
                "ExternalAuthentication": {
                    "PublicOrigin": "https://localhost:3000"
                },
            }
        ),
        encoding="utf-8",
    )
    settings.chmod(0o600)

    write_executable(
        fake_bin / "curl",
        "#!/usr/bin/env bash\nexit 0\n",
    )
    write_executable(
        fake_bin / "dotnet",
        r'''#!/usr/bin/env bash
set -euo pipefail

if [[ "${1:-}" == "dev-certs" && " $* " == *" --export-path "* ]]; then
  while (($#)); do
    if [[ "$1" == "--export-path" ]]; then
      certificate_file="$2"
      : >"$certificate_file"
      : >"${certificate_file%.pem}.key"
      exit 0
    fi
    shift
  done
fi

if [[ "${1:-}" == "run" ]]; then
  sleep 300 &
  child_pid=$!
  printf '%s\n' "$child_pid" >"$LOCAL_HTTPS_TEST_PID_DIRECTORY/api-child.pid"
  wait "$child_pid"
fi

exit 0
''',
    )
    write_executable(
        fake_bin / "npm",
        r'''#!/usr/bin/env bash
set -euo pipefail

if [[ "${1:-}" == "run" && "${2:-}" == "dev" ]]; then
  sleep 300 &
  child_pid=$!
  printf '%s\n' "$child_pid" >"$LOCAL_HTTPS_TEST_PID_DIRECTORY/web-child.pid"
  wait "$child_pid"
fi

exit 0
''',
    )

    log_path = temp / "launcher.log"
    environment = os.environ.copy()
    environment.update(
        {
            "LOCAL_HTTPS_SETTINGS_FILE": str(settings),
            "LOCAL_HTTPS_TEST_PID_DIRECTORY": str(pid_directory),
            "PATH": f"{fake_bin}:{environment['PATH']}",
        }
    )

    with log_path.open("w", encoding="utf-8") as log_file:
        launcher = subprocess.Popen(
            [str(LAUNCHER)],
            cwd=REPO_ROOT,
            env=environment,
            stdout=log_file,
            stderr=subprocess.STDOUT,
            text=True,
            start_new_session=True,
        )

        child_pid_files = [
            pid_directory / "api-child.pid",
            pid_directory / "web-child.pid",
        ]
        child_pids: list[int] = []
        try:
            wait_for_files(child_pid_files, launcher, log_path)
            child_pids = [
                int(path.read_text(encoding="utf-8").strip())
                for path in child_pid_files
            ]

            os.kill(launcher.pid, signal.SIGINT)
            launcher.wait(timeout=10)

            survivors = [pid for pid in child_pids if not wait_until_stopped(pid)]
            if survivors:
                raise AssertionError(
                    "Ctrl+C left descendant processes running: "
                    + ", ".join(str(pid) for pid in survivors)
                )
        finally:
            if launcher.poll() is None:
                launcher.kill()
                launcher.wait(timeout=5)
            for pid in child_pids:
                if process_is_alive(pid):
                    os.kill(pid, signal.SIGKILL)

print("run-local-https Ctrl+C cleanup: PASS")
