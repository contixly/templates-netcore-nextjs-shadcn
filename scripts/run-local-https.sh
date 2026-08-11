#!/usr/bin/env bash

set -Eeuo pipefail

readonly api_origin="https://localhost:7297"
readonly web_origin="https://localhost:3000"
readonly ready_timeout_seconds="${LOCAL_HTTPS_READY_TIMEOUT_SECONDS:-60}"

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
api_project="$repo_root/apps/api/src/Template.Api/Template.Api.csproj"
infrastructure_project="$repo_root/apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj"
web_directory="$repo_root/apps/web"
web_lockfile="$web_directory/package-lock.json"
web_install_stamp="$web_directory/node_modules/.template-package-lock.sha256"
local_settings="${LOCAL_HTTPS_SETTINGS_FILE:-$repo_root/apps/api/src/Template.Api/appsettings.Local.json}"

api_pid=""
web_pid=""
certificate_directory=""
certificate_file=""
certificate_key_file=""

usage() {
  cat <<EOF
Usage: scripts/run-local-https.sh

Prepare and run the complete local application over HTTPS:
  UI:  $web_origin
  API: $api_origin

The script reads PostgreSQL from ConnectionStrings__Postgres when set, or
falls back to ConnectionStrings:Postgres in the ignored file:
  apps/api/src/Template.Api/appsettings.Local.json

Optional OAuth settings also come from that file. HTTPS mode forces the
external-authentication public origin to $web_origin, trusts and exports the
.NET development certificate, restores tools/dependencies when needed,
applies EF Core migrations, starts ASP.NET Core and Next.js, and stops both
on Ctrl+C.
EOF
}

fail() {
  printf 'error: %s\n' "$1" >&2
  exit 1
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || fail "required command not found: $1"
}

ensure_port_available() {
  local port="$1"

  python3 - "$port" <<'PY' ||
import errno
import socket
import sys

port = int(sys.argv[1])
unsupported_ipv6_errors = {
    errno.EAFNOSUPPORT,
    errno.EPROTONOSUPPORT,
    getattr(errno, "EPFNOSUPPORT", -1),
}
for address in (("127.0.0.1", port), ("::1", port)):
    family = socket.AF_INET6 if ":" in address[0] else socket.AF_INET
    try:
        sock = socket.socket(family, socket.SOCK_STREAM)
        sock.settimeout(0.2)
        try:
            if sock.connect_ex(address) == 0:
                raise SystemExit(1)
        finally:
            sock.close()
    except OSError as error:
        if family == socket.AF_INET6 and error.errno in unsupported_ipv6_errors:
            continue
        raise
PY
    fail "port $port is already in use"
}

read_postgres_connection_string() {
  local require_connection_string="$1"

  python3 - "$local_settings" "$require_connection_string" <<'PY'
import json
import os
import stat
import sys


def remove_json_comments(source):
    output = []
    index = 0
    in_string = False
    escaped = False

    while index < len(source):
        character = source[index]
        if in_string:
            output.append(character)
            if escaped:
                escaped = False
            elif character == "\\":
                escaped = True
            elif character == '"':
                in_string = False
            index += 1
            continue

        if character == '"':
            in_string = True
            output.append(character)
            index += 1
            continue

        following = source[index + 1] if index + 1 < len(source) else ""
        if character == "/" and following == "/":
            comment_start = index
            index += 2
            while index < len(source) and source[index] not in "\r\n":
                index += 1
            output.extend(" " for _ in source[comment_start:index])
            continue

        if character == "/" and following == "*":
            comment_end = source.find("*/", index + 2)
            if comment_end == -1:
                raise ValueError("unterminated block comment")
            output.extend(
                character if character in "\r\n" else " "
                for character in source[index : comment_end + 2]
            )
            index = comment_end + 2
            continue

        output.append(character)
        index += 1

    return "".join(output)


def remove_trailing_json_commas(source):
    output = []
    index = 0
    in_string = False
    escaped = False

    while index < len(source):
        character = source[index]
        if in_string:
            output.append(character)
            if escaped:
                escaped = False
            elif character == "\\":
                escaped = True
            elif character == '"':
                in_string = False
            index += 1
            continue

        if character == '"':
            in_string = True
            output.append(character)
            index += 1
            continue

        if character == ",":
            following_index = index + 1
            while (
                following_index < len(source)
                and source[following_index].isspace()
            ):
                following_index += 1
            if (
                following_index < len(source)
                and source[following_index] in "}]"
            ):
                output.append(" ")
                index += 1
                continue

        output.append(character)
        index += 1

    return "".join(output)


path = sys.argv[1]
require_connection_string = sys.argv[2] == "true"

try:
    file_mode = stat.S_IMODE(os.stat(path).st_mode)
except FileNotFoundError:
    if not require_connection_string:
        raise SystemExit(0)
    print(
        "error: ConnectionStrings__Postgres is not set and "
        "appsettings.Local.json is missing",
        file=sys.stderr,
    )
    raise SystemExit(1)

if file_mode & 0o077:
    print(
        f"error: appsettings.Local.json must use mode 0600, found {file_mode:04o}",
        file=sys.stderr,
    )
    raise SystemExit(1)

try:
    with open(path, encoding="utf-8-sig") as settings_file:
        source = remove_json_comments(settings_file.read())
    settings = json.loads(remove_trailing_json_commas(source))
except (OSError, ValueError) as error:
    print(f"error: cannot read appsettings.Local.json: {error}", file=sys.stderr)
    raise SystemExit(1)

connection_string = settings.get("ConnectionStrings", {}).get("Postgres")
if not isinstance(connection_string, str) or not connection_string.strip():
    if not require_connection_string:
        raise SystemExit(0)
    print(
        "error: set ConnectionStrings__Postgres or add "
        "ConnectionStrings:Postgres to appsettings.Local.json",
        file=sys.stderr,
    )
    raise SystemExit(1)

print(connection_string)
PY
}

file_sha256() {
  python3 - "$1" <<'PY'
import hashlib
import sys

digest = hashlib.sha256()
with open(sys.argv[1], "rb") as source:
    for chunk in iter(lambda: source.read(1024 * 1024), b""):
        digest.update(chunk)
print(digest.hexdigest())
PY
}

wait_for_url() {
  local name="$1"
  local url="$2"
  local pid="$3"
  local deadline=$((SECONDS + ready_timeout_seconds))
  local request_timeout

  while ((SECONDS < deadline)); do
    if ! kill -0 "$pid" 2>/dev/null; then
      fail "$name stopped before becoming ready"
    fi

    request_timeout=$((deadline - SECONDS))
    if ((request_timeout > 2)); then
      request_timeout=2
    fi

    if curl --http1.1 --silent --show-error --fail \
      --max-time "$request_timeout" \
      --cacert "$certificate_file" \
      --noproxy '*' \
      --output /dev/null "$url"; then
      printf '%s is ready: %s\n' "$name" "$url"
      return 0
    fi

    if ((SECONDS < deadline)); then
      sleep 1
    fi
  done

  fail "$name did not become ready within $ready_timeout_seconds seconds"
}

exec_in_new_session() {
  exec python3 -c '
import os
import sys

os.setsid()
os.execvpe(sys.argv[1], sys.argv[1:], os.environ)
' "$@"
}

process_group_has_live_members() {
  local process_group_id="$1"

  ps -eo pgid=,stat= | awk -v process_group_id="$process_group_id" '
    $1 == process_group_id && $2 !~ /^Z/ { found = 1 }
    END { exit found ? 0 : 1 }
  '
}

stop_process_group() {
  local leader_pid="$1"
  local attempt=1

  [[ -n "$leader_pid" ]] || return 0
  process_group_has_live_members "$leader_pid" || return 0

  kill -TERM -- "-$leader_pid" 2>/dev/null || true
  while ((attempt <= 50)); do
    if ! process_group_has_live_members "$leader_pid"; then
      return 0
    fi
    sleep 0.1
    attempt=$((attempt + 1))
  done

  kill -KILL -- "-$leader_pid" 2>/dev/null || true
}

cleanup() {
  local exit_code=$?
  trap - EXIT INT TERM

  stop_process_group "$web_pid"
  stop_process_group "$api_pid"

  [[ -z "$web_pid" ]] || wait "$web_pid" 2>/dev/null || true
  [[ -z "$api_pid" ]] || wait "$api_pid" 2>/dev/null || true

  if [[ -n "$certificate_directory" ]]; then
    [[ -z "$certificate_file" ]] || rm -f -- "$certificate_file"
    [[ -z "$certificate_key_file" ]] || rm -f -- "$certificate_key_file"
    rmdir "$certificate_directory" 2>/dev/null || true
  fi

  exit "$exit_code"
}

if [[ "${1:-}" == "--help" || "${1:-}" == "-h" ]]; then
  usage
  exit 0
fi

[[ $# -eq 0 ]] || {
  usage >&2
  exit 2
}

[[ "$ready_timeout_seconds" =~ ^[1-9][0-9]*$ ]] ||
  fail "LOCAL_HTTPS_READY_TIMEOUT_SECONDS must be a positive integer"

trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

require_command curl
require_command dotnet
require_command npm
require_command python3
require_command ps
require_command awk

[[ -f "$api_project" ]] || fail "run this script from its repository checkout"
[[ -f "$infrastructure_project" ]] || fail "infrastructure project not found"
[[ -f "$web_directory/package.json" ]] || fail "web package not found"
[[ -f "$web_lockfile" ]] || fail "web package lock not found"

cd "$repo_root"

ensure_port_available 7297
ensure_port_available 3000

if [[ -n "${ConnectionStrings__Postgres:-}" ]]; then
  postgres_connection_string="$ConnectionStrings__Postgres"
  unset ConnectionStrings__Postgres
  read_postgres_connection_string false >/dev/null ||
    fail "local configuration validation failed"
else
  postgres_connection_string="$(read_postgres_connection_string true)" ||
    fail "local configuration validation failed"
fi

printf 'Trusting the ASP.NET Core development certificate...\n'
dotnet dev-certs https --trust

printf 'Restoring local .NET tools...\n'
dotnet tool restore

package_lock_hash="$(file_sha256 "$web_lockfile")"
installed_package_lock_hash=""
if [[ -f "$web_install_stamp" ]]; then
  IFS= read -r installed_package_lock_hash <"$web_install_stamp" || true
fi

if [[ "$installed_package_lock_hash" != "$package_lock_hash" ]] ||
  ! (cd "$web_directory" && npm ls --depth=0 --silent >/dev/null 2>&1); then
  printf 'Installing Next.js dependencies...\n'
  (
    cd "$web_directory"
    npm ci
  )
  printf '%s\n' "$package_lock_hash" >"$web_install_stamp"
fi
unset installed_package_lock_hash package_lock_hash

printf 'Applying EF Core migrations...\n'
ConnectionStrings__Postgres="$postgres_connection_string" \
  dotnet ef database update \
    --project "$infrastructure_project" \
    --startup-project "$api_project" \
    --context TemplateDbContext

certificate_directory="$(mktemp -d "${TMPDIR:-/tmp}/template-local-https.XXXXXX")"
certificate_file="$certificate_directory/localhost.pem"
certificate_key_file="$certificate_directory/localhost.key"

dotnet dev-certs https \
  --export-path "$certificate_file" \
  --format Pem \
  --no-password
chmod 600 "$certificate_file" "$certificate_key_file"

printf 'Starting ASP.NET Core API...\n'
(
  cd "$repo_root"
  export DOTNET_ENVIRONMENT=Development
  export ASPNETCORE_ENVIRONMENT=Development
  export ASPNETCORE_URLS=https://localhost:7297
  export ConnectionStrings__Postgres="$postgres_connection_string"
  export ExternalAuthentication__PublicOrigin="$web_origin"
  export LocalAutomationAuth__Enabled=true
  exec_in_new_session \
    dotnet run \
    --project "$api_project" \
    --no-launch-profile \
    --no-build
) &
api_pid=$!
unset postgres_connection_string

wait_for_url "API" "$api_origin/api/health/ready" "$api_pid"

printf 'Starting Next.js UI...\n'
(
  cd "$web_directory"
  export APP_PUBLIC_ORIGIN="$web_origin"
  export API_INTERNAL_BASE_URL="$api_origin"
  export API_PROXY_TARGET="$api_origin"
  export NODE_EXTRA_CA_CERTS="$certificate_file"
  unset SSL_CERT_FILE
  exec_in_new_session \
    npm run dev -- \
    --experimental-https \
    --experimental-https-key "$certificate_key_file" \
    --experimental-https-cert "$certificate_file" \
    --hostname 127.0.0.1 \
    --port 3000
) &
web_pid=$!

wait_for_url "UI" "$web_origin" "$web_pid"

cat <<EOF

Local HTTPS environment is running:
  UI:      $web_origin
  Login:   $web_origin/auth/login
  API:     $api_origin
  OpenAPI: $api_origin/api/openapi/v1.json

Press Ctrl+C to stop both applications.
EOF

while true; do
  if ! kill -0 "$api_pid" 2>/dev/null; then
    wait "$api_pid" 2>/dev/null || true
    fail "API process stopped unexpectedly"
  fi
  if ! kill -0 "$web_pid" 2>/dev/null; then
    wait "$web_pid" 2>/dev/null || true
    fail "UI process stopped unexpectedly"
  fi
  sleep 1
done
