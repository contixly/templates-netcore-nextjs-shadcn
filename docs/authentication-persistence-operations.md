# Authentication and persistence operations

## Scope

Iteration 3 owns the clean PostgreSQL `auth` schema, Identity Core credential
records, PostgreSQL-backed browser tickets, the secure session cookie,
antiforgery, local automation auth, and database readiness. It does not migrate
Prisma/Better Auth data and does not provide production password or social
login.

## Configuration

Runtime database configuration uses `ConnectionStrings:Postgres`; the
environment form is `ConnectionStrings__Postgres`. Keep passwords in
environment variables or .NET user-secrets, never committed appsettings.

Local automation requires both `Development`/`Test` and
`LocalAutomationAuth__Enabled=true`. Production returns
`404 local_auth_disabled` even if that flag is accidentally true.

The default local limits are:

- `LocalAutomationAuth__CreateRateLimitPerMinute=20`
- `LocalAutomationAuth__SignInRateLimitPerFiveMinutes=10`

## Apply and inspect migrations

The API never applies migrations automatically.

```bash
dotnet tool restore
dotnet ef database update \
  --project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --startup-project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --context AuthDbContext

dotnet ef migrations has-pending-model-changes \
  --project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --startup-project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --context AuthDbContext

dotnet ef migrations script --idempotent \
  --project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --startup-project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --context AuthDbContext
```

`Template.Infrastructure` is both the target and design-time startup project:
its `AuthDbContextFactory` owns the private EF Design package.
`Template.Api` remains the only HTTP host.

Integration tests and `Template.E2EHost` apply migrations only to disposable
PostgreSQL 18.4 databases created by Testcontainers.

## Local sign-in

1. Configure and migrate PostgreSQL.
2. Set `LocalAutomationAuth__Enabled=true`.
3. Start API and Next.js with the existing same-origin development rewrite.
4. Open `/auth/login` and choose **Create local automation user**.

Automation clients call `GET /api/v1/auth/csrf` before every unsafe request.
Scenario creation returns the generated password once. The visible UI discards
it after navigation; test helpers may use it for a second session.

## Session and CSRF safety

The browser session cookie is `__Host-template.session`: HttpOnly, Secure,
SameSite Lax, Path `/`, no Domain, persistent seven-day sliding expiration.
The cookie contains only a Data-Protection-protected opaque ticket-store key.
PostgreSQL stores only its SHA-256 hash and a separately protected ticket.

Browser authentication and rotation use the primary `Template.Session` scheme.
Session issuance and replacement use the internal
`Template.Session.Issuer` scheme with a write-only cookie manager, so an
existing request cookie cannot be replayed as the newly issued cookie. Both
schemes share the ticket-store format and Data Protection purpose. Only the
primary cookie contract is advertised as `cookieAuth`; the issuer is internal,
and no Bearer or API-key scheme exists at runtime. The default authenticate
selector normally forwards to the primary cookie scheme; only the canonical
liveness path and its route-equivalent trailing-slash form use a process-only
no-result handler.

Next.js SSR session reads send `X-Template-Session-Renewal: suppress`, preventing
an invisible persisted-ticket renewal whose `Set-Cookie` cannot reach the
browser. The authenticated dashboard follows with an unmarked same-origin
generated-SDK session read, so normal half-life sliding renewal updates both
PostgreSQL and the browser's secure HttpOnly cookie.

The antiforgery cookie is `__Host-template.antiforgery`: HttpOnly, Secure,
SameSite Strict, Path `/`, no Domain. Send its paired request token in
`X-CSRF-TOKEN` for scenario creation, credential sign-in, logout, and cleanup.

## Health and failure diagnosis

`/api/health/live` does not touch PostgreSQL, including when the request carries
a valid session cookie. `/api/health` and `/api/health/ready` require
connectivity and a queryable `auth.users` relation. Health responses never
expose connection strings or schema errors.

Auth responses are never cached. Diagnose failures by stable Problem Details
`code` and `traceId`; do not log or display passwords, cookies, ticket data, or
backend `detail`.

## Rollback and production gate

The initial migration is additive relative to iterations 0–2. Rolling
application code back may leave schema `auth` in place. The generated `Down`
path is destructive and is restricted to disposable development/test
databases; production uses restore or forward-fix procedures.

Production deployment remains blocked until external OAuth behavior and
persistent/encrypted Data Protection key storage are designed. API-key
`x-api-key` support remains iteration 7, and no Bearer scheme is registered.
