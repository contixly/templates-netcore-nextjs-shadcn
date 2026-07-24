# Итерация 3: persistence, Identity и базовая аутентификация

**Дата:** 2026-07-24

**Статус:** дизайн согласован пользователем

**Долгосрочная дорожная карта:** [`../../aspnetcore-migration-plan.md`](../../aspnetcore-migration-plan.md)

## 1. Цель

Итерация 3 добавляет первый persistent vertical slice в новую архитектуру:
PostgreSQL, EF Core migrations, ASP.NET Core Identity, server-side browser
sessions, cookie authentication и reference-подобный local automation flow.

Reference разрешает email/password только вне production для automation и
быстрой локальной проверки. Новая система сохраняет это поведение:

- публичной production-регистрации и password login нет;
- локальный пользователь создаётся одной кнопкой на `/auth/login`;
- credentials возвращаются automation-клиенту один раз и позволяют создать
  дополнительные тестовые sessions;
- production-вход через внешние providers переносится в итерацию 4.

Итерация создаёт чистую Identity schema. Prisma/Better Auth users, sessions,
accounts и verification records не импортируются.

## 2. Изученный контекст

Перед проектированием проверены:

- корневой `AGENTS.md`;
- `docs/aspnetcore-migration-plan.md`;
- `docs/api-conventions.md` и `docs/web-conventions.md`;
- designs/plans и acceptance evidence итераций 1–2;
- текущие API endpoint modules, cookie extension point, OpenAPI export,
  `WebApplicationFactory` и Problem Details contract;
- текущий Next.js generated client, browser/SSR factories, Cache Components,
  source-boundary checks, Jest и Playwright harness;
- recent commits и чистое состояние `main`;
- reference `template/src/server/auth.ts`, auth client, proxy и route config;
- reference `/auth/login`, `/api/auth/[...all]`,
  `/api/local-auth/scenario`, local automation helpers и tests;
- reference account actions и session-management tests, чтобы отделить
  iteration-3 foundation от iteration-4 account lifecycle;
- Prisma `User`, `Session`, `Account`, `Verification` models и initial SQL
  migration;
- reference API-key implementation и docs: machine access использует
  `x-api-key`, а не Bearer.

Reference production login является OAuth-only. Better Auth
`emailAndPassword` включается только при local automation feature flag,
автоматически создаёт session и не требует email verification.

Актуальные платформенные решения сверены с официальной документацией:

- [Identity for SPA/Web API backends](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization?view=aspnetcore-10.0);
- [ITicketStore](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.authentication.cookies.iticketstore?view=aspnetcore-10.0);
- [ASP.NET Core antiforgery](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0);
- [ASP.NET Core rate limiting](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0);
- [EF Core migration deployment](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying);
- [Npgsql EF Core 10 release notes](https://www.npgsql.org/efcore/release-notes/10.0.html);
- [PostgreSQL versioning policy](https://www.postgresql.org/support/versioning/);
- [PostgreSQL Testcontainers for .NET](https://dotnet.testcontainers.org/modules/postgres/).

Установленный Chromium дополнительно проверен практическим probe: Secure
`__Host-` cookie принимается на используемых E2E loopback hosts
`127.0.0.1` и `localhost`. Production по-прежнему требует HTTPS.

## 3. Согласованные решения

1. Используется custom REST facade над ASP.NET Core Identity Core, а не
   `MapIdentityApi`.
2. Browser session хранится в PostgreSQL через `ITicketStore`; cookie содержит
   только защищённый opaque key.
3. Local auth одновременно требует environment `Development`/`Test` и
   feature flag.
4. `/auth/login` содержит только reference-подобную one-click automation
   panel; ручной credential form отсутствует.
5. После входа пользователь попадает на временный защищённый `/dashboard`.
6. Development использует `ConnectionStrings:Postgres`; integration/E2E
   используют PostgreSQL Testcontainers. Docker Compose и Aspire не добавляются.
7. Архитектура допускает будущие cookie, API-key и Bearer handlers, но сейчас
   реализуется и документируется только cookie scheme.
8. Reference-compatible `x-api-key` остаётся iteration 7. Реальный Bearer
   issuer/handler также не входит в iteration 3.
9. Database baseline — current supported PostgreSQL `18.4`, exact-pinned in
   Testcontainers; prerelease PostgreSQL 19 не используется.

## 4. Scope

### Входит

- Npgsql и EF Core 10 persistence;
- initial Identity/session migration;
- ASP.NET Core Identity Core без Identity UI и roles;
- secure HttpOnly same-origin cookie;
- PostgreSQL-backed persistent authentication tickets;
- current-session projection и logout;
- local-only scenario create, credential sign-in и cleanup;
- antiforgery для всех browser-cookie mutations;
- auth-specific rate limiting and lockout;
- database readiness;
- auth capabilities contract;
- reference-like login UI и temporary protected dashboard;
- OpenAPI and generated TypeScript SDK changes;
- application, PostgreSQL integration, API, component and Playwright tests;
- durable API/web/auth documentation and migration-register evidence.

### Не входит

- production password registration/login;
- email delivery, email confirmation endpoints, password reset or 2FA UI;
- external OAuth providers and account linking;
- profile, account deletion UI, session list and revoke-other-session UI;
- organizations, workspace onboarding or product dashboard;
- API keys, `/api/v1` machine endpoints or API-key management;
- runtime Bearer authentication or token issuance;
- Redis, cache-backed sessions or background cleanup jobs;
- Docker Compose, Aspire, YARP or production process topology;
- production Data Protection key persistence/encryption;
- legacy user/session/data migration;
- active OpenSpec change/spec.

Dependency gate is satisfied: iterations 1–2 are complete, the cookie/OpenAPI
and generated-client seams already exist, PostgreSQL connection/secrets
conventions are selected below, and Docker is available for Testcontainers.
No organization, OAuth, API-key or production-hosting dependency is pulled
forward.

## 5. Reference correspondence

| Reference                                                            | Новый API/данные                                             | Новый UI                                | Test/evidence                                                       |
| -------------------------------------------------------------------- | ------------------------------------------------------------ | --------------------------------------- | ------------------------------------------------------------------- |
| `prisma/schema.prisma`: `User`, `Session`, `Account`, `Verification` | Identity users/logins/tokens plus persistent session tickets | N/A                                     | migration, indexes, uniqueness and cascade tests against PostgreSQL |
| `src/server/auth.ts`, `/api/auth/[...all]` session lookup            | `GET /api/v1/auth/session`                                   | protected `/dashboard` session proof    | anonymous/authenticated API and Playwright cases                    |
| Better Auth `signOut`                                                | `POST /api/v1/auth/logout`                                   | logout control                          | current-session removal, cookie expiry and browser redirect         |
| `POST /api/local-auth/scenario`                                      | same route with new envelope/Problem Details                 | one-click automation panel              | API, component and E2E create cases                                 |
| `/api/auth/sign-in/email`                                            | `POST /api/local-auth/sign-in`                               | no visible form; automation helper only | second-browser-context sign-in                                      |
| `DELETE /api/local-auth/scenario`                                    | same route; user/session cleanup                             | no product control; E2E helper only     | local-user authorization and cross-context invalidation             |
| `/auth/login`, `LoginForm`, local automation panel                   | capabilities/session REST composition                        | reference-like `/auth/login`            | capability, rendering, navigation and failure tests                 |
| `proxy.ts` protected-page redirect                                   | session projection remains API-owned                         | `/dashboard` server-side auth gate      | safe redirect and anonymous navigation E2E                          |
| account session list/revoke actions and tests                        | persistent-session foundation only                           | no `/user/security`                     | explicitly deferred to iteration 4                                  |
| API key auth and `/api/v1/**` reference tests                        | no runtime implementation                                    | none                                    | future policies cannot be accidentally satisfied by browser cookie  |

## 6. Chosen architecture

### Rejected alternatives

`MapIdentityApi` is not used because it would expose registration, refresh,
confirmation, password reset, 2FA/management and proprietary bearer-token
surfaces beyond scope, while also bypassing the established response/error
contract.

A fully custom identity system is also rejected: password hashing, lockout,
normalization, security stamps and future external-login integration should be
provided by ASP.NET Core Identity rather than reimplemented.

### Layer responsibilities

#### Domain

Domain remains framework-independent. It contains only neutral identity/session
identifiers and lifecycle invariants that are useful outside HTTP/Identity.
ASP.NET Core `AuthenticationTicket`, EF entities and password hashes never enter
Domain.

#### Application

Application owns use cases and ports:

- get current session;
- create local automation user and first session;
- sign in a local automation user;
- logout current session;
- clean up the current local automation user;
- generate/validate local automation credentials and namespace;
- project safe current-user/session DTOs.

Application depends on abstractions for Identity operations, session
persistence, transactions, time and cryptographic credential generation. It
does not depend on EF, Npgsql, ASP.NET Identity or HTTP.

#### Infrastructure

Infrastructure owns:

- `ApplicationUser`;
- `AuthDbContext`;
- Identity stores, `UserManager` and `SignInManager`;
- Npgsql mappings and migrations;
- password hashing, normalization and lockout;
- protected authentication-ticket serialization;
- PostgreSQL `ITicketStore`;
- repository/transaction implementations.

#### Api

Api owns:

- endpoint mapping and HTTP DTO validation;
- environment/config capability gate;
- authentication schemes and authorization policies;
- cookie/antiforgery/rate-limit configuration;
- Problem Details mapping;
- cache headers and OpenAPI metadata.

#### Web

Web owns routes, presentation and operation adapters over generated SDK
functions. It does not define auth response DTOs, read the session cookie, call
raw `fetch`, use Server Actions, expose Route Handlers, or access a database.

## 7. Authentication scheme boundaries

The composition root distinguishes policy intent:

- `BrowserSession` accepts only the session cookie scheme;
- `MachineApiKey` is a reserved future boundary for iteration 7;
- `Bearer` is a reserved future extension point.

The `BrowserSession` primary handler and policy are registered in this
iteration. A non-default, write-only cookie issuer is also registered solely to
rotate a browser session's opaque lookup key safely; it is not a separately
authorizable scheme, a machine credential, or an OpenAPI security definition.
Future constants/interfaces otherwise do not register unusable policies, create
fake schemes, advertise unsupported OpenAPI security definitions or allow
browser cookies to satisfy machine-only endpoints.

External OAuth login in iteration 4 will finish by issuing the same browser
session cookie. It does not imply a browser bearer token.

## 8. REST contract

All auth responses use `Cache-Control: no-store`.

| Method and route                  | Access                        | Success                                                           |
| --------------------------------- | ----------------------------- | ----------------------------------------------------------------- |
| `GET /api/v1/auth/capabilities`   | anonymous                     | `200 { data: { localAutomationEnabled, providers } }`             |
| `GET /api/v1/auth/session`        | anonymous                     | `200` anonymous or authenticated projection                       |
| `GET /api/v1/auth/csrf`           | anonymous                     | `200` request token plus antiforgery cookie                       |
| `POST /api/v1/auth/logout`        | `BrowserSession` + CSRF       | `200` anonymous session projection after removing current session |
| `POST /api/local-auth/scenario`   | local-only + CSRF             | `201` user, one-time credentials and cleanup URL                  |
| `POST /api/local-auth/sign-in`    | local-only + CSRF             | `200` safe user/session projection                                |
| `DELETE /api/local-auth/scenario` | local `BrowserSession` + CSRF | `200 { data: { deletedOrganizations: 0 } }`                       |

`providers` is an array of typed `{ id, displayName }` values and is empty in
iteration 3. Iteration 4 can add configured providers additively.

Local endpoints remain mapped in every environment so Production consistently
returns `404 local_auth_disabled`; they cannot be activated outside
Development/Test even if configuration is wrong. The feature flag defaults to
`false`; manual development and E2E enable it explicitly.

Local endpoints are included in the committed OpenAPI input with a visible
`local-only` tag/extension because the UI and E2E helpers must consume generated
operations and types. Runtime OpenAPI remains unavailable in Production.
Production behavior (`404` unless the two-part gate is satisfied) is part of
their contract.

Scenario creation accepts a strict JSON object with optional `name`, `email`
and `password`; every omitted value is generated server-side. Credential
sign-in accepts only required `email` and `password`.
Navigation `redirect` remains a UI concern and is not accepted by either API
request. Pagination and filtering do not apply because this slice exposes no
collections. There is no cache invalidation beyond expiring the browser cookie;
all reads are uncached.

### Session projection

Anonymous:

```json
{
  "data": {
    "authenticated": false,
    "user": null,
    "session": null
  }
}
```

Authenticated:

```json
{
  "data": {
    "authenticated": true,
    "user": {
      "id": "019...",
      "name": "Local Automation ...",
      "email": "local-agent+...@local-agent.test",
      "emailVerified": false,
      "image": null
    },
    "session": {
      "id": "019...",
      "createdAt": "2026-07-24T00:00:00Z",
      "updatedAt": "2026-07-24T00:00:00Z",
      "expiresAt": "2026-07-31T00:00:00Z"
    }
  }
}
```

The API never returns a raw ticket, password hash, cookie value or bearer-like
session token.

### Local scenario response

The create operation returns the generated plaintext password once:

```json
{
  "data": {
    "user": {
      "id": "019...",
      "email": "local-agent+...@local-agent.test",
      "name": "Local Automation ...",
      "emailVerified": false,
      "image": null
    },
    "email": "local-agent+...@local-agent.test",
    "password": "local-...",
    "cleanupUrl": "/api/local-auth/scenario"
  }
}
```

The browser panel discards these credentials after navigation. Automation
helpers may use them to create a second independent session.

## 9. Validation, password and verification policy

HTTP request DTOs reject unknown JSON properties.

- display name is trimmed and 2–50 characters;
- email is trimmed, normalized, at most 254 characters and must match the
  `local-agent+...@local-agent.test` namespace;
- explicit password is 12–128 characters;
- generated password uses at least 256 bits of cryptographic randomness;
- Identity requires unique normalized email and uses email as username;
- password policy requires length 12 but disables composition rules; strength
  for the supported flow comes from generated random credentials;
- five failed password attempts lock the local user for five minutes;
- sign-in failures never reveal whether a user/email exists.

Generated-email collisions are retried with fresh credentials up to three
times. An explicit duplicate, or an exhausted generated retry budget, returns
the documented conflict rather than leaking a database exception.

Local users are intentionally allowed to sign in with
`emailVerified=false`, matching reference local automation. No email sender,
verification action or verification-token lifecycle is implemented.
Iteration 4 must define provider verification mapping; invitation verification
rules remain in their later domain iteration.

### Stable failures

| Status | Code                             | Meaning                                             |
| ------ | -------------------------------- | --------------------------------------------------- |
| 400    | `validation_failed`              | field validation                                    |
| 400    | `invalid_request`                | malformed/unknown JSON shape                        |
| 400    | `antiforgery_failed`             | missing or invalid request token                    |
| 401    | `local_auth_invalid_credentials` | generic credential failure                          |
| 401    | `unauthorized`                   | missing/expired browser session                     |
| 403    | `local_auth_user_required`       | cleanup attempted by non-local user                 |
| 404    | `local_auth_disabled`            | local feature unavailable                           |
| 409    | `local_auth_user_exists`         | duplicate email or exhausted generated retry budget |
| 429    | `rate_limited`                   | authentication limiter rejected request             |

All errors use the existing RFC Problem Details shape and include correlation
`traceId`. Sensitive values never appear in `detail`, validation messages or
logs.

## 10. Cookie, CSRF and rate limiting

### Session cookie

- scheme remains `Template.Session`;
- name: `__Host-template.session`;
- `HttpOnly=true`;
- `Secure=Always`;
- `SameSite=Lax`;
- `Path=/`;
- no `Domain`;
- persistent, seven-day sliding expiration.

The cookie contains a Data-Protection-protected ticket-store key with at least
256 bits of cryptographic randomness, not claims or credentials. The
corresponding PostgreSQL record is the revocation source of truth. OpenAPI keeps
the existing `cookieAuth` scheme name.

### Lookup-key rotation on replacement

Credential sign-in and local scenario creation must issue a fresh opaque
ticket-store lookup key even when the request already carries a valid browser
cookie. ASP.NET Core `10.0.10` retains the primary cookie handler's internal
store key after same-request sign-out and would otherwise call `RenewAsync` for
the following sign-in. Reusing that key would keep a stolen pre-replacement
cookie valid for the newly authenticated session.

The application therefore uses two standard cookie handlers with the same secure
cookie name, `PostgresTicketStore`, and explicit shared Data Protection ticket
format:

- `Template.Session` is the only default authenticate/challenge/forbid/sign-out
  scheme and reads the request cookie normally.
- `Template.Session.Issuer` is non-default and write-only: its cookie manager
  returns no request cookie, so the handler calls `StoreAsync` and obtains a
  fresh random key.

`BrowserSessionGateway` starts a replacement once per request, signs out the
primary handler to revoke the old row and suppress any pending sliding refresh,
suppresses only that intentional delete-cookie response, and signs in through
the issuer. Normal logout is unaffected. A second replacement in one request
fails closed. `RenewAsync` never recreates a missing row. Retrieval accepts only
the two expected stored-ticket schemes and exactly one matching persisted
user/session claim; mismatched, expired, malformed, or incompatible tickets are
conditionally deleted. Regression tests cover same-user and cross-user
replacement, old-cookie invalidation, normal logout, half-life refresh
suppression, and coordinated revoke/renew races.

### Antiforgery

`GET /api/v1/auth/csrf` calls the ASP.NET Core antiforgery service, returns the
request token in the response body and stores a separate Secure HttpOnly
`__Host-template.antiforgery` cookie with `SameSite=Strict`, `Path=/` and no
`Domain`.

Every cookie-relevant unsafe operation requires `X-CSRF-TOKEN`, including:

- anonymous local scenario creation;
- local credential sign-in;
- authenticated logout;
- local cleanup.

Validation covers `POST`, `PUT`, `PATCH` and `DELETE`; DELETE is not left to the
Minimal API form-only default. Antiforgery runs after authentication/
authorization middleware. CORS is not enabled.

### Limits

Default configurable local limits, with queue length zero:

- create scenario: 20 requests/minute per effective remote IP;
- credential sign-in: 10 requests/5 minutes per effective remote IP.

The configuration keys are
`LocalAutomationAuth:Enabled`,
`LocalAutomationAuth:CreateRateLimitPerMinute` and
`LocalAutomationAuth:SignInRateLimitPerFiveMinutes`; their environment forms
use the standard double underscore.

The partition key never uses attacker-controlled email. Test configuration can
lower/reset limits to exercise deterministic `429` behavior. Rate limiting is a
defense-in-depth control, not DDoS protection.

## 11. PostgreSQL model

The model lives in PostgreSQL schema `auth`.

### `users`

- UUIDv7 primary key;
- Identity username/email normalization columns;
- unique normalized-email index;
- display name and nullable image URL;
- password hash, security stamp and concurrency stamp;
- email-confirmed, lockout and 2FA-compatible Identity fields;
- `is_local_automation`;
- UTC `created_at` and `updated_at`.

### Identity support tables

`user_claims`, `user_logins` and `user_tokens` are included. Identity roles are
not: organization/workspace roles are product-domain concepts and must not be
implemented through ASP.NET Identity roles.

### `sessions`

- UUIDv7 session ID;
- `user_id` FK with cascade delete;
- unique SHA-256 hash of the random ticket-store lookup key;
- separately Data-Protection-protected serialized authentication ticket;
- UTC PostgreSQL `timestamp with time zone` created/updated/expires values;
- nullable PostgreSQL `inet` IP;
- user-agent metadata bounded to 512 characters;
- indexes on `user_id` and `expires_at`.

The stored principal contains a non-public `session_id` claim matching the row
ID. Current-session projection resolves that ID; the opaque lookup key is never
promoted to a claim or response. Safe user fields are reloaded from `users`
rather than trusted from potentially stale ticket claims.

Expired records are deleted lazily when retrieved. No scheduler, Redis or audit
table is introduced. Structured security events log operation outcome and safe
user/session IDs, never credentials, cookies or ticket data. Durable
security-audit storage remains an iteration-4 decision.

## 12. Transactions and session lifecycle

- Scenario creation stores the user and first session in one EF transaction.
- Credential sign-in verifies the password and atomically stores one new
  session with a fresh opaque lookup key, revoking any current browser session,
  without mutating the user except Identity lockout state.
- Logout removes the server-side current session before expiring the cookie.
- Cleanup verifies both the local-user marker and email namespace, then deletes
  user and all sessions in one transaction. `deletedOrganizations` is `0`
  because organization data remains outside scope.
- Unique-email races map to `409`, not `500`.
- Session-store failure rolls back scenario creation; no partial user remains.

The .NET 10 `ITicketStore` overloads with `HttpContext` resolve request-scoped
persistence so sign-in ticket writes can enlist in the active transaction.

## 13. Connection, migrations and health

- canonical key: `ConnectionStrings:Postgres`;
- environment form: `ConnectionStrings__Postgres`;
- local secrets use environment variables or .NET user-secrets;
- committed appsettings contain no connection credentials;
- Testcontainers uses exact image `postgres:18.4` and injects a dynamic
  connection string.

The migration assembly and EF CLI design-time startup project are both
`Template.Infrastructure`: it owns the design-time factory and the private EF
Design dependency. `Template.Api` remains the only HTTP host. The initial
migration and model snapshot are committed and reviewed.

The API never auto-applies migrations. Local development uses explicit
`dotnet ef database update`; integration/E2E fixtures call `MigrateAsync` on
their isolated database. Production later uses reviewed SQL or a migration
bundle.

Readiness performs a bounded PostgreSQL connectivity check tagged `ready`.
It also verifies the expected `auth.users` relation is queryable, so a connected
but unmigrated database is not reported ready. Liveness remains independent of
PostgreSQL. Health payloads do not expose connection/schema details.
Acceptance checks migration application to a clean database and pending model
drift.

The initial migration is additive relative to iterations 0–2, which never use a
database. Rolling application code back may safely leave the `auth` schema in
place. The generated `Down` path is destructive and is used only for disposable
development/test databases; production rollback uses a previous application
artifact plus forward-compatible schema or restore/forward-fix procedure, never
an automatic down migration.

Normal compilation, OpenAPI export and the production web build require valid
configuration shape but no live database/API connection. Tests that assert
persistence always use a real PostgreSQL container.

Data Protection keys use development/test defaults in this iteration.
Production key persistence, encryption-at-rest and rotation are an explicit
gate for the production topology; storing unprotected keys beside protected
session tickets is not introduced as a shortcut.

## 14. Next.js routes and data flow

### Routes

- `/` keeps the technical status smoke and adds a minimal localized
  **Get Started** link to `/auth/login?redirect=/dashboard`.
- `/auth/login` uses a simple reference-like layout without the application
  header.
- `/dashboard` is a clearly labelled temporary session-proof page, not the
  product dashboard.

Route groups split the common providers-only root from site-header and simple
auth layouts without changing public URLs.

### Login

The login page performs request-time capabilities/session work below
`connection()` and `Suspense`, then calls generated SDK operations. This
preserves the iteration-2 Cache Components rule: `next build` needs neither a
live API nor request cookies. An already authenticated user is redirected to
the sanitized target.

When local automation is enabled, the Client Component:

1. obtains a generated CSRF response;
2. invokes the generated scenario operation with its request token;
3. refreshes Next request state;
4. navigates to the safe redirect, defaulting to `/dashboard`.

There are no credential inputs or social buttons in iteration 3.

### Protected dashboard

The request-time Server Component reads the incoming cookie outside cached
scope and passes only `Cookie` and optional correlation ID through the existing
server-client allowlist. It calls the generated session operation with
`no-store`.

- authenticated projection renders safe user/session fields and logout;
- anonymous projection redirects to
  `/auth/login?redirect=/dashboard`;
- network/configuration failure renders a localized safe failure and is not
  misclassified as anonymous.

Logout obtains CSRF, calls the generated logout operation, refreshes and
navigates to the login URL.

### Redirect policy

Only local absolute paths beginning with a single `/` are accepted. Full URLs,
protocol-relative paths, `/api/**` and auth loops are rejected. The fallback is
`/dashboard`.

### UI safety

- session cookie is never read by JavaScript;
- backend `detail` and raw exceptions are never rendered;
- UI branches on stable Problem Details `code`;
- plaintext scenario credentials are discarded by the visible panel;
- auth messages are isolated in `auth.en.json` and `auth.ru.json`;
- generated API DTOs remain the only transport types.

## 15. Test-first strategy

Test/bootstrap infrastructure may be established first, but each behavior
starts with a focused failing test, followed by implementation and focused
green verification.

### Order

1. Application rules: local namespace, credential generation and session
   lifecycle inputs.
2. Clean PostgreSQL migration, indexes, uniqueness and cascade behavior.
3. Persistent ticket-store store/retrieve/renew/remove behavior.
4. Capabilities, CSRF, scenario, sign-in, session, logout and cleanup endpoints.
5. OpenAPI security/operation contract and generated SDK drift.
6. Login, redirect, dashboard and logout component/route behavior.
7. Full-stack Playwright.

### API and PostgreSQL cases

- feature flag cannot enable local auth in Production;
- Test/Development without the flag returns disabled capability and local 404;
- missing/invalid CSRF prevents every unsafe operation;
- scenario creates exactly one user and session;
- injected storage failure leaves neither user nor session;
- explicit duplicate email returns 409;
- invalid credentials are generic and lockout works;
- cookie attributes match the contract;
- anonymous/authenticated session projections are non-cacheable;
- second sign-in creates a distinct persistent session;
- logout deletes only the current session;
- cleanup rejects anonymous and non-local users;
- cleanup deletes the user and all sessions;
- expired/deleted session keys no longer authenticate;
- readiness fails with unavailable PostgreSQL while liveness stays healthy;
- migration applies to empty PostgreSQL and model has no pending changes;
- rate-limit response is typed and includes `Retry-After`.

No EF InMemory or SQLite substitute is used for persistence acceptance.

### Contract cases

- committed OpenAPI contains the supported cookie scheme;
- local operations are explicitly marked local-only;
- local/CSRF operation signatures generate usable SDK functions;
- unsupported API-key/Bearer schemes are absent;
- authenticated browser endpoints carry cookie security requirements;
- anonymous operations do not;
- generation is byte-deterministic.

### Web cases

- capabilities control panel visibility;
- one-click flow calls CSRF before scenario;
- errors are localized by stable code and do not expose backend detail;
- redirect sanitizer rejects open redirects and auth loops;
- authenticated login redirects without showing automation controls;
- dashboard renders safe authenticated fields;
- anonymous dashboard redirects;
- API outage renders failure rather than redirect;
- logout calls CSRF first and navigates correctly;
- SSR forwards only cookie/correlation ID;
- boundary guards reject raw fetch, Server Actions, Route Handlers,
  Prisma/Better Auth and handwritten DTOs.

### Playwright

The harness starts PostgreSQL through Testcontainers, applies migrations, then
starts API and Next.js.

The primary scenario:

1. open `/` and follow **Get Started**;
2. observe the local automation panel;
3. create user/session and land on `/dashboard`;
4. reload and retain authentication;
5. verify current-session API projection;
6. use returned credentials in a second browser context;
7. prove the contexts have different session IDs;
8. logout one context without invalidating the other;
9. cleanup the automation user;
10. prove every remaining context becomes anonymous and protected navigation
    returns to login.

## 16. Acceptance commands

From the repository root:

```bash
dotnet tool restore
dotnet restore Template.sln
dotnet build Template.sln --no-restore
dotnet test Template.sln --no-restore

dotnet build apps/api/src/Template.Api/Template.Api.csproj \
  --no-restore \
  -p:OpenApiGenerateDocuments=true
git diff --exit-code -- contracts/openapi/v1.json

dotnet ef migrations has-pending-model-changes \
  --project apps/api/src/Template.Infrastructure \
  --startup-project apps/api/src/Template.Api

dotnet ef migrations script --idempotent \
  --project apps/api/src/Template.Infrastructure \
  --startup-project apps/api/src/Template.Api
```

From `apps/web`:

```bash
npm ci
npm audit --json
npm run audit:prod
npm run api:check
npm run boundaries:check
npm run format:check
npm run lint
npm run typecheck
npm test -- --runInBand
npm run build
npm run e2e:install
npm run e2e
test -f .next/standalone/server.js
```

Repository guards:

```bash
git diff --check
git diff --exit-code -- template/
git diff --exit-code origin/main...HEAD -- template/
```

Exact package/tool versions and any installed-version-specific Next.js
instructions are verified in the implementation plan before code changes.

## 17. Durable documentation and completion

Implementation updates:

- `docs/api-conventions.md` with session, CSRF and auth-policy contracts;
- `docs/web-conventions.md` with request-bound session loading and CSRF client
  flow;
- a focused auth/persistence operations document covering connection strings,
  migrations, local feature gating and cleanup;
- `docs/aspnetcore-migration-plan.md` with iteration-3 scope, status,
  correspondence table, exact command results and known differences.

Acceptance evidence records test counts, Testcontainers/PostgreSQL version,
OpenAPI/client drift results, cookie/CSRF scenarios and an empty
`git diff -- template/`.

## 18. Intentional differences and next gate

Intentional iteration-3 differences from reference:

- RFC Problem Details and `{ data }` replace Better Auth/local `success/error`
  envelopes;
- local credential sign-in uses `/api/local-auth/sign-in`;
- scenario navigation redirect is validated/owned by the UI instead of being
  accepted in the scenario request body;
- production has no sign-in method until external OAuth is implemented;
- Identity schema starts clean and does not mirror Prisma table names;
- `/dashboard` is an auth proof rather than the reference product dashboard;
- local cleanup returns zero organizations because that domain is absent;
- no session-cookie cache/JWT layer is placed in front of PostgreSQL;
- no API-key or Bearer runtime exists yet.

Iteration 4 remains blocked on explicit provider priority, provider
credentials/callback conventions, production Data Protection key storage,
provider email-verification semantics and account/session-management scope.
Iteration 7 remains responsible for reference-compatible personal/
organization `x-api-key` authentication. Any real Bearer-token use case
requires its own issuer, audience, lifetime, revocation and consumer contract
decision rather than silently enabling Identity proprietary tokens.
