#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
launcher="$repo_root/scripts/run-local-https.sh"

fail() {
  printf 'FAIL: %s\n' "$1" >&2
  exit 1
}

[[ -x "$launcher" ]] || fail "launcher must exist and be executable"
bash -n "$launcher"

help_output="$($launcher --help)"
grep -Fq -- "appsettings.Local.json" <<<"$help_output" ||
  fail "help must name the local configuration file"
grep -Fq -- "https://localhost:3000" <<<"$help_output" ||
  fail "help must name the HTTPS UI origin"
grep -Fq -- "https://localhost:7297" <<<"$help_output" ||
  fail "help must name the HTTPS API origin"

grep -Fq -- "dotnet ef database update" "$launcher" ||
  fail "launcher must apply EF Core migrations"
grep -Fq -- "ASPNETCORE_URLS=https://localhost:7297" "$launcher" ||
  fail "launcher must bind ASP.NET Core to the HTTPS API origin"
grep -Fq -- "--experimental-https" "$launcher" ||
  fail "launcher must start Next.js with HTTPS"
grep -Fq -- "SSL_CERT_FILE" "$launcher" ||
  fail "launcher must configure Node to trust the exported development certificate"
grep -Fq -- "trap cleanup" "$launcher" ||
  fail "launcher must clean up child processes and temporary certificate files"

printf 'run-local-https contract: PASS\n'
