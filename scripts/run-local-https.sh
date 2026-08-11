#!/usr/bin/env bash

set -Eeuo pipefail

readonly api_origin="https://localhost:7297"
readonly web_origin="https://localhost:3000"

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
api_project="$repo_root/apps/api/src/Template.Api/Template.Api.csproj"
infrastructure_project="$repo_root/apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj"
web_directory="$repo_root/apps/web"
local_settings="$repo_root/apps/api/src/Template.Api/appsettings.Local.json"

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

The script reads PostgreSQL and OAuth settings from the ignored file:
  apps/api/src/Template.Api/appsettings.Local.json

It validates local configuration, trusts and exports the .NET development
certificate, restores tools/dependencies when needed, applies EF Core
migrations, starts ASP.NET Core and Next.js, and stops both on Ctrl+C.
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
import socket
import sys

port = int(sys.argv[1])
for address in (("127.0.0.1", port), ("::1", port)):
    family = socket.AF_INET6 if ":" in address[0] else socket.AF_INET
    sock = socket.socket(family, socket.SOCK_STREAM)
    sock.settimeout(0.2)
    try:
        if sock.connect_ex(address) == 0:
            raise SystemExit(1)
    finally:
        sock.close()
PY
    fail "port $port is already in use"
}

read_postgres_connection_string() {
  python3 - "$local_settings" "$web_origin" <<'PY'
import json
import os
import stat
import sys

path, expected_origin = sys.argv[1:]

try:
    file_mode = stat.S_IMODE(os.stat(path).st_mode)
except FileNotFoundError:
    print(
        "error: appsettings.Local.json is missing; copy the local example first",
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
    with open(path, encoding="utf-8") as settings_file:
        settings = json.load(settings_file)
except (OSError, json.JSONDecodeError) as error:
    print(f"error: cannot read appsettings.Local.json: {error}", file=sys.stderr)
    raise SystemExit(1)

connection_string = settings.get("ConnectionStrings", {}).get("Postgres")
if not isinstance(connection_string, str) or not connection_string.strip():
    print(
        "error: ConnectionStrings:Postgres is required in appsettings.Local.json",
        file=sys.stderr,
    )
    raise SystemExit(1)

public_origin = settings.get("ExternalAuthentication", {}).get("PublicOrigin")
if public_origin != expected_origin:
    print(
        f"error: ExternalAuthentication:PublicOrigin must be {expected_origin}",
        file=sys.stderr,
    )
    raise SystemExit(1)

print(connection_string)
PY
}

wait_for_url() {
  local name="$1"
  local url="$2"
  local pid="$3"
  local attempt=1

  while ((attempt <= 60)); do
    if ! kill -0 "$pid" 2>/dev/null; then
      fail "$name stopped before becoming ready"
    fi

    if curl --http1.1 --silent --show-error --fail --max-time 2 \
      --output /dev/null "$url"; then
      printf '%s is ready: %s\n' "$name" "$url"
      return 0
    fi

    sleep 1
    attempt=$((attempt + 1))
  done

  fail "$name did not become ready within 60 seconds"
}

cleanup() {
  local exit_code=$?
  trap - EXIT INT TERM

  if [[ -n "$web_pid" ]] && kill -0 "$web_pid" 2>/dev/null; then
    kill "$web_pid" 2>/dev/null || true
  fi
  if [[ -n "$api_pid" ]] && kill -0 "$api_pid" 2>/dev/null; then
    kill "$api_pid" 2>/dev/null || true
  fi

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

trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

require_command curl
require_command dotnet
require_command npm
require_command python3

[[ -f "$api_project" ]] || fail "run this script from its repository checkout"
[[ -f "$infrastructure_project" ]] || fail "infrastructure project not found"
[[ -f "$web_directory/package.json" ]] || fail "web package not found"

ensure_port_available 7297
ensure_port_available 3000

postgres_connection_string="$(read_postgres_connection_string)" ||
  fail "local configuration validation failed"

printf 'Trusting the ASP.NET Core development certificate...\n'
dotnet dev-certs https --check --trust

printf 'Restoring local .NET tools...\n'
dotnet tool restore

if [[ ! -d "$web_directory/node_modules" ]]; then
  printf 'Installing Next.js dependencies...\n'
  (
    cd "$web_directory"
    npm ci
  )
fi

printf 'Applying EF Core migrations...\n'
ConnectionStrings__Postgres="$postgres_connection_string" \
  dotnet ef database update \
    --project "$infrastructure_project" \
    --startup-project "$api_project" \
    --context TemplateDbContext
unset postgres_connection_string

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
  ASPNETCORE_ENVIRONMENT=Development \
    ASPNETCORE_URLS=https://localhost:7297 \
    dotnet run \
      --project "$api_project" \
      --no-launch-profile \
      --no-build
) &
api_pid=$!

wait_for_url "API" "$api_origin/api/health/ready" "$api_pid"

node_options="${NODE_OPTIONS:-}"
case " $node_options " in
  *" --use-openssl-ca "*) ;;
  *) node_options="${node_options:+$node_options }--use-openssl-ca" ;;
esac

printf 'Starting Next.js UI...\n'
(
  cd "$web_directory"
  APP_PUBLIC_ORIGIN="$web_origin" \
    API_INTERNAL_BASE_URL="$api_origin" \
    API_PROXY_TARGET="$api_origin" \
    SSL_CERT_FILE="$certificate_file" \
    NODE_OPTIONS="$node_options" \
    npm run dev -- \
      --experimental-https \
      --experimental-https-key "$certificate_key_file" \
      --experimental-https-cert "$certificate_file"
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
