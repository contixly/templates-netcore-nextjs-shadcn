#!/usr/bin/env python3

from __future__ import annotations

import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import tempfile
import time


SOURCE_LAUNCHER = Path(__file__).resolve().parents[1] / "run-local-https.sh"


def write_executable(path: Path, contents: str) -> None:
    path.write_text(contents, encoding="utf-8")
    path.chmod(0o755)


def process_is_alive(pid: int) -> bool:
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        return False
    state = process_state(pid)
    return bool(state) and not state.startswith("Z")


def process_state(pid: int) -> str:
    result = subprocess.run(
        ["ps", "-o", "stat=", "-p", str(pid)],
        check=False,
        capture_output=True,
        text=True,
    )
    return result.stdout.strip()


def assert_zombies_are_not_alive() -> None:
    zombie_pid = os.fork()
    if zombie_pid == 0:
        os._exit(0)

    try:
        deadline = time.monotonic() + 3
        while time.monotonic() < deadline:
            if process_state(zombie_pid).startswith("Z"):
                break
            time.sleep(0.01)
        else:
            raise AssertionError("test child did not enter the zombie state")

        if process_is_alive(zombie_pid):
            raise AssertionError("zombie descendants must count as stopped")
    finally:
        os.waitpid(zombie_pid, 0)


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


assert_zombies_are_not_alive()


with tempfile.TemporaryDirectory(prefix="run-local-https-test.") as directory:
    temp = Path(directory).resolve()
    repository = temp / "repository"
    outside_directory = temp / "outside"
    fake_bin = temp / "bin"
    python_customization = temp / "python-customization"
    pid_directory = temp / "pids"
    command_log = temp / "commands.log"
    api_directory = repository / "apps" / "api" / "src" / "Template.Api"
    infrastructure_directory = (
        repository / "apps" / "api" / "src" / "Template.Infrastructure"
    )
    web_directory = repository / "apps" / "web"
    scripts_directory = repository / "scripts"

    for path in (
        outside_directory,
        fake_bin,
        python_customization,
        pid_directory,
        api_directory,
        infrastructure_directory,
        web_directory / "node_modules",
        scripts_directory,
    ):
        path.mkdir(parents=True, exist_ok=True)

    (api_directory / "Template.Api.csproj").touch()
    (infrastructure_directory / "Template.Infrastructure.csproj").touch()
    (web_directory / "package.json").write_text("{}\n", encoding="utf-8")
    (web_directory / "package-lock.json").write_text(
        '{"lockfileVersion": 3}\n', encoding="utf-8"
    )

    launcher = scripts_directory / "run-local-https.sh"
    shutil.copy2(SOURCE_LAUNCHER, launcher)

    real_python = shutil.which("python3")
    if real_python is None:
        raise AssertionError("python3 is required to run this test")

    settings = temp / "appsettings.Local.json"
    settings.write_text(
        json.dumps(
            {
                "ExternalAuthentication": {
                    "PublicOrigin": "http://localhost:3000"
                },
            }
        ),
        encoding="utf-8",
    )
    settings.chmod(0o600)

    write_executable(
        fake_bin / "curl",
        r'''#!/usr/bin/env bash
set -euo pipefail

if [[ "${LOCAL_HTTPS_TEST_SLOW_CURL:-}" == "1" ]]; then
  sleep 2
  exit 22
fi

exit 0
''',
    )
    (python_customization / "sitecustomize.py").write_text(
        r'''import errno
import os
import socket


_real_socket = socket.socket


class _AvailablePortSocket:
    def settimeout(self, _timeout):
        return None

    def connect_ex(self, _address):
        return errno.ECONNREFUSED

    def close(self):
        return None


def _socket_with_ipv6_unavailable(family=-1, type=-1, proto=-1, fileno=None):
    if os.environ.get("LOCAL_HTTPS_TEST_FAKE_PORTS") == "1":
        if family == socket.AF_INET6:
            raise OSError(errno.EAFNOSUPPORT, "IPv6 is unavailable in this test")
        if family == socket.AF_INET:
            return _AvailablePortSocket()
    return _real_socket(family, type, proto, fileno)


socket.socket = _socket_with_ipv6_unavailable
''',
        encoding="utf-8",
    )
    write_executable(
        fake_bin / "python3",
        f'''#!/usr/bin/env bash
set -euo pipefail

exec "{real_python}" "$@"
''',
    )
    write_executable(
        fake_bin / "dotnet",
        r'''#!/usr/bin/env bash
set -euo pipefail

printf 'dotnet\t%s\t%s\n' "$PWD" "$*" >>"$LOCAL_HTTPS_TEST_COMMAND_LOG"
printf 'database-scope\tdotnet\t%s\t%s\n' \
  "$*" "${ConnectionStrings__Postgres:+set}" \
  >>"$LOCAL_HTTPS_TEST_COMMAND_LOG"

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
  printf 'api-environment\t%s\t%s\t%s\n' \
    "${LocalAutomationAuth__Enabled:-}" \
    "${ExternalAuthentication__PublicOrigin:-}" \
    "${ConnectionStrings__Postgres:-}" \
    >>"$LOCAL_HTTPS_TEST_COMMAND_LOG"
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

printf 'npm\t%s\t%s\n' "$PWD" "$*" >>"$LOCAL_HTTPS_TEST_COMMAND_LOG"
printf 'database-scope\tnpm\t%s\t%s\n' \
  "$*" "${ConnectionStrings__Postgres:+set}" \
  >>"$LOCAL_HTTPS_TEST_COMMAND_LOG"

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
            "LOCAL_HTTPS_TEST_COMMAND_LOG": str(command_log),
            "LOCAL_HTTPS_TEST_FAKE_PORTS": "1",
            "LOCAL_HTTPS_TEST_PID_DIRECTORY": str(pid_directory),
            "ConnectionStrings__Postgres": "Host=environment-test",
            "PATH": f"{fake_bin}:{environment['PATH']}",
            "PYTHONPATH": str(python_customization),
        }
    )

    with log_path.open("w", encoding="utf-8") as log_file:
        process = subprocess.Popen(
            [str(launcher)],
            cwd=outside_directory,
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
            wait_for_files(child_pid_files, process, log_path)
            child_pids = [
                int(path.read_text(encoding="utf-8").strip())
                for path in child_pid_files
            ]

            os.kill(process.pid, signal.SIGINT)
            return_code = process.wait(timeout=5)
            if return_code != 130:
                raise AssertionError(
                    f"Ctrl+C returned {return_code}, expected 130\n"
                    + log_path.read_text(encoding="utf-8")
                )

            survivors = [pid for pid in child_pids if not wait_until_stopped(pid)]
            if survivors:
                raise AssertionError(
                    "Ctrl+C left descendant processes running: "
                    + ", ".join(str(pid) for pid in survivors)
                )
        finally:
            if process.poll() is None:
                process.kill()
                process.wait(timeout=5)
            for pid in child_pids:
                if process_is_alive(pid):
                    os.kill(pid, signal.SIGKILL)

    command_lines = command_log.read_text(encoding="utf-8").splitlines()
    if not any(line.endswith("\tdev-certs https --trust") for line in command_lines):
        raise AssertionError(
            "launcher must establish development-certificate trust before checking it"
        )
    expected_tool_restore = f"dotnet\t{repository}\ttool restore"
    if expected_tool_restore not in command_lines:
        raise AssertionError("dotnet tool restore must run from the repository root")
    if not any(
        line.startswith(f"dotnet\t{repository}\tef database update ")
        for line in command_lines
    ):
        raise AssertionError("dotnet ef must run from the repository root")
    if not any(
        line.startswith("api-environment\ttrue\t") for line in command_lines
    ):
        raise AssertionError("launcher must enable Development-only local authentication")
    if not any(
        line.startswith("api-environment\ttrue\thttps://localhost:3000\t")
        for line in command_lines
    ):
        raise AssertionError("launcher must override the copied HTTP origin for HTTPS mode")
    if (
        "api-environment\ttrue\thttps://localhost:3000\tHost=environment-test"
        not in command_lines
    ):
        raise AssertionError("launcher must pass the selected PostgreSQL setting to the API")
    if f"npm\t{web_directory}\tci" not in command_lines:
        raise AssertionError("launcher must repair an unvalidated node_modules tree")
    unexpected_database_scope = [
        line
        for line in command_lines
        if line.startswith("database-scope\t")
        and line.endswith("\tset")
        and not line.startswith("database-scope\tdotnet\tef database update ")
        and not line.startswith("database-scope\tdotnet\trun ")
    ]
    if unexpected_database_scope:
        raise AssertionError(
            "launcher leaked the PostgreSQL credential to unrelated commands: "
            + ", ".join(unexpected_database_scope)
        )

    for child_pid_file in child_pid_files:
        child_pid_file.unlink(missing_ok=True)

    deadline_log_path = temp / "deadline-launcher.log"
    deadline_environment = environment.copy()
    deadline_environment.update(
        {
            "LOCAL_HTTPS_READY_TIMEOUT_SECONDS": "2",
            "LOCAL_HTTPS_TEST_SLOW_CURL": "1",
        }
    )
    with deadline_log_path.open("w", encoding="utf-8") as log_file:
        deadline_process = subprocess.Popen(
            [str(launcher)],
            cwd=outside_directory,
            env=deadline_environment,
            stdout=log_file,
            stderr=subprocess.STDOUT,
            text=True,
            start_new_session=True,
        )

        deadline_child_pid_file = pid_directory / "api-child.pid"
        deadline_child_pid: int | None = None
        try:
            wait_for_files(
                [deadline_child_pid_file], deadline_process, deadline_log_path
            )
            deadline_child_pid = int(
                deadline_child_pid_file.read_text(encoding="utf-8").strip()
            )
            deadline_started_at = time.monotonic()
            try:
                deadline_return_code = deadline_process.wait(timeout=5)
            except subprocess.TimeoutExpired as error:
                raise AssertionError(
                    "readiness timeout must be a wall-clock deadline"
                ) from error

            deadline_elapsed = time.monotonic() - deadline_started_at
            if deadline_return_code == 0:
                raise AssertionError("unready API must make the launcher fail")
            if deadline_elapsed > 4:
                raise AssertionError(
                    "two-second readiness timeout took "
                    f"{deadline_elapsed:.2f} seconds"
                )

            deadline_log = deadline_log_path.read_text(encoding="utf-8")
            if "API did not become ready within 2 seconds" not in deadline_log:
                raise AssertionError(
                    "launcher must report the configured wall-clock readiness timeout"
                )
            if not wait_until_stopped(deadline_child_pid):
                raise AssertionError(
                    "readiness timeout left the API descendant process running"
                )
        finally:
            if deadline_process.poll() is None:
                os.kill(deadline_process.pid, signal.SIGINT)
                deadline_process.wait(timeout=5)
            if deadline_child_pid is not None and process_is_alive(deadline_child_pid):
                os.kill(deadline_child_pid, signal.SIGKILL)

print("run-local-https Ctrl+C cleanup: PASS")
