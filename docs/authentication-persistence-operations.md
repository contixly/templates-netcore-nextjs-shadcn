# Authentication and persistence operations

## Scope

Iterations 3–5 own the clean PostgreSQL `auth` schema, Identity Core users,
verified-email and external-login records, PostgreSQL-backed browser tickets
and OpenIddict state, the persistent Data Protection key ring, the secure
session cookie, antiforgery, local automation auth, account lifecycle, and
five-provider external OAuth. They do not migrate Prisma/Better Auth users,
sessions, OAuth accounts, tokens, or secrets. Production password registration,
login, reset, and change remain unavailable; password credentials are only the
existing Development/Test local-automation mechanism.

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

External OAuth uses:

- `ExternalAuthentication__PublicOrigin`;
- `ExternalAuthentication__Providers__Google__ClientId` and `ClientSecret`;
- the equivalent `GitHub`, `GitLab`, `Vk`, and `Yandex` provider pairs;
- `ExternalOAuthSecurity__ChallengePermitLimitPerMinute` (default `20`);
- `ExternalOAuthSecurity__CallbackPermitLimitPerFiveMinutes` (default `60`);
- `ExternalOAuthSecurity__CallbackConcurrencyLimit` (default `10`, no queue).

The public origin must be HTTPS, except that HTTP loopback is allowed for local
development. A provider is active only when both canonical non-empty credential
values are present. A partial or unknown provider block fails validation
without logging values; an absent block is simply not advertised.

For local development only, ignored
`apps/api/src/Template.Api/appsettings.Local.json` is loaded as the final
optional configuration overlay. It is never loaded in Test or Production and
is excluded from build/publish output. Copy values manually into the shape in
`appsettings.Local.example.json`; runtime and scripts must not read
`template/.env`. Keep the real file mode `0600` and never commit it.

Data Protection always uses application discriminator `Template` and persists
keys in PostgreSQL. Production additionally requires
`DataProtection__CertificatePath` and
`DataProtection__CertificatePassword` for a mounted RSA PFX. The certificate
and private key stay outside Git and PostgreSQL. Production defers PFX loading
until the DI-owned startup service resolves it, then fails closed on missing,
invalid, non-RSA, or private-key-less material. Development and Test share the
database key ring without enforcing the production certificate.

The development Next.js rewrite is a one-hop loopback proxy. ASP.NET Core
processes one `X-Forwarded-For` value from that trusted loopback boundary before
auth rate limiting, so different originating clients receive independent
partitions. Forwarded client addresses from non-loopback peers are ignored; do
not clear the framework trusted-proxy lists to broaden this boundary.

## Apply and inspect migrations

The API never applies migrations automatically.

```bash
dotnet tool restore
dotnet ef database update \
  --project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --startup-project apps/api/src/Template.Api/Template.Api.csproj \
  --context TemplateDbContext

dotnet ef migrations has-pending-model-changes \
  --project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --startup-project apps/api/src/Template.Api/Template.Api.csproj \
  --context TemplateDbContext

dotnet ef migrations script --idempotent \
  --project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --startup-project apps/api/src/Template.Api/Template.Api.csproj \
  --context TemplateDbContext
```

`Template.Infrastructure` is the migration target. `Template.Api` is the
explicit design-time startup project and the only HTTP host; both projects keep
their EF Design dependency private.

Integration tests and `Template.E2EHost` use disposable PostgreSQL 18.4
databases created by Testcontainers. Despite its historical project name,
`Template.E2EHost` is an orchestration executable rather than an HTTP host: it
creates and migrates the database, then launches the real `Template.Api`
project as a child process. Its explicit
`Testing__AssumeHttpsBoundary=true` setting is honored only in the `Test`
environment so the loopback HTTP listener can model the production HTTPS
boundary required by secure antiforgery cookies.

The iteration-4 and iteration-5 migrations are additive:

- `20260728232503_AccountsExternalOAuth` creates `auth.user_emails`, adds
  verified-email/timestamp metadata to Identity logins, creates the Data
  Protection key table and OpenIddict EF tables, and backfills one primary email
  for each existing iteration-3 user;
- `20260728235449_AccountSessionAuthenticationMethod` adds the bounded
  `authentication_method` session projection and backfills existing rows to
  `local`;
- `20260730091827_OrganizationsMembershipOnboarding` creates the
  `organizations` schema (`organizations`, `members`,
  `allowed_email_domains`), adds the closed role/name/slug constraints and
  indexes, and adds nullable indexed `auth.sessions.active_organization_id`
  with an FK to `organizations.organizations` using `SET NULL`.

`auth.user_emails.normalized_email` is globally unique, and a partial unique
index permits at most one primary row per user. Identity
`ApplicationUser.Email`/`NormalizedEmail` remain a compatibility mirror of the
primary row and are changed in the same transaction. Each external login points
to a verified-email row, retains unique `(provider, subject)` ownership, and is
also unique by `(user, provider)`. No account/login/provider table has an
access-token or refresh-token column.

The OpenIddict EF rows are client state bookkeeping, not an authorization
server or provider-token vault. An hourly bounded cleanup deletes at most 500
expired state rows or redeemed state rows older than 24 hours. Recent redeemed,
revoked, future-valid, and non-state records are preserved.

## Local sign-in

1. Configure and migrate PostgreSQL.
2. Set `LocalAutomationAuth__Enabled=true`.
3. Start API and Next.js with the existing same-origin development rewrite.
4. Open `/auth/login` and choose **Create local automation user**.

Automation clients call `GET /api/v1/auth/csrf` before every unsafe request.
Scenario creation returns the generated password once. The visible UI discards
it after navigation; test helpers may use it for a second session.

## External provider sign-in and callbacks

Configure only the provider pairs required locally, keep the Next.js/API
same-origin path, then open `/auth/login`. The capability response and UI show
only complete providers. Every challenge obtains a fresh CSRF token and calls
`POST /api/v1/auth/external/{provider}/challenge`; the browser performs
top-level navigation to the returned HTTPS authorization URL.

Keep these callback registrations exact for the configured public origin:

| Provider | Callback path                      |
| -------- | ---------------------------------- |
| Google   | `/api/auth/callback/google`        |
| GitHub   | `/api/auth/callback/github`        |
| GitLab   | `/api/auth/callback/gitlab`        |
| VK       | `/api/auth/callback/vk`            |
| Yandex   | `/api/auth/oauth2/callback/yandex` |

Google and GitLab require an explicit verified-email claim. GitHub requires its
primary verified email from the bounded email API. VK user-info email and
Yandex `default_email` are provider-confirmed under the approved mapping.
Missing or unverified evidence fails closed.

Provider access/refresh tokens are callback-local normalization inputs only.
The production callback copies them into its owned mutable bag, removes them
from authentication properties, and clears the bag in `finally`. Normalization
also attempts to clear mutable input, but the `IReadOnlyDictionary` abstraction
cannot clear an immutable/read-only bag; such callers retain cleanup ownership,
and direct evidence for that caller contract is deferred. Tokens are never
saved to OpenIddict, Identity, the account schema, logs, responses, or browser
storage. Consequently local disconnect cannot revoke remote provider consent,
and no provider API refresh workflow exists.

Authorization-screen smoke is explicitly opt-in. It may prove that a configured
button reaches an official provider host without submitting credentials or
following a callback. Provider-console redirect registration and external
network state can still reject that navigation; do not treat such a smoke as a
successful callback or login. Successful callback behavior is covered by the
deterministic fake-provider/OpenIddict integration suite.

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
and no Bearer or API-key scheme exists at runtime.
`Template.Session.Selector` is `DefaultAuthenticateScheme` and normally
forwards to the primary cookie scheme; only the canonical liveness path and its
route-equivalent trailing-slash form use a process-only no-result handler.

The combined Next.js SSR auth read uses a correlation-only client for anonymous
capabilities and a separate cookie-bearing client for the session projection.
SSR session reads send `X-Template-Session-Renewal: suppress`, preventing an
invisible persisted-ticket renewal whose `Set-Cookie` cannot reach the browser;
the capabilities hop cannot authenticate or renew at all. The authenticated
dashboard follows with an unmarked same-origin generated-SDK session read, so
normal half-life sliding renewal updates both PostgreSQL and the browser's
secure HttpOnly cookie before the endpoint projects `updatedAt` and `expiresAt`.
The response therefore describes the renewed server-side session rather than
the pre-renewal row. Cookie-bearing account Server Component reads send the same
suppression marker; unmarked browser `GET` requests keep normal sliding
expiration.

When a valid opaque cookie references a missing, expired, corrupt, or mismatched
server-side ticket, authentication remains anonymous and the same response
expires the browser cookie. Transient database failures do not trigger this
deletion.

Every issued ticket contains exactly one authentication-method claim from
`local`, `google`, `github`, `gitlab`, `vk`, or `yandex`, and the session row
stores the same bounded relational projection. Missing, duplicate, unknown, or
case-variant input normalizes to `local` before initial protection. Retrieval
normalizes the protected claim and requires an ordinal match with the row. A
mismatch is an integrity failure: the row is conditionally removed only while
the protected payload is unchanged, the cookie is invalidated, and neither
projection silently overwrites the other. Account session listings read only
safe relational columns and never decrypt the protected ticket.

The antiforgery cookie is `__Host-template.antiforgery`: HttpOnly, Secure,
SameSite Strict, Path `/`, no Domain. Send its paired request token in
`X-CSRF-TOKEN` for scenario creation, credential sign-in, external challenge,
logout, profile/disconnect/session mutations, account deletion, and cleanup.

## Account transactions and operational limits

External identity reconciliation performs stable `(provider, subject)` lookup
before email ownership lookup and runs one unit-of-work transaction per attempt.
A classified PostgreSQL uniqueness race receives exactly one retry with fresh
reads. New anonymous identities implicitly link to the owner of a matching
primary or secondary verified email only when another login owned by that user
still references the same email row. A retained historical primary email with
no current provider vouch fails with an email conflict. Explicit connect can
reuse the current user's existing email or add a free secondary email, but
cannot claim another user's email.

When a known `(provider, subject)` returns a changed email, reconciliation first
checks normalized ownership. Another user's email produces a conflict without
moving the login. An email already owned by the same user is reused; a free
email is created as secondary. At the first existing-login read, persistence
locks the actual `(provider, subject)` row with `FOR UPDATE` and reads its
current verified email inside the same authentication unit-of-work transaction.
Ownership validation and changed-email handling therefore serialize from their
first snapshot. The update path retains a defensive lock/reload before moving
the login, and each operation deletes the non-primary email it actually
replaced only when no remaining login references it. Primary and still-shared
emails are never removed. An unchanged-email repeat sign-in updates only
`LastUsedAt`; connect, including email reassociation, preserves the previous
`LastUsedAt`.

The reconciliation unit of work commits before the HTTP callback issues a new
provider-authenticated browser session for sign-in or rotates the existing
session principal for connect. Session issuance/rotation is deliberately not
part of the account transaction.

Disconnect takes and locks a fresh connection/email snapshot, revalidates
ownership and the startup-stable configured-provider set, deletes the selected
login, and deletes its non-primary email only when no remaining login references
that row. Any failure rolls both changes back. The current provider cannot be
disconnected, and every removal must leave at least one connected provider
whose runtime configuration is complete. Stored logins for providers whose
configuration was removed remain visible but do not count as usable survivors.
The configured set crosses the Application use-case and persistence port;
Application does not depend on Infrastructure. Development/Test local-automation
credentials do not count as a production method.

Account deletion validates the confirmation against the normalized primary
email and is organization-aware in the same transaction. It locks affected
organization rows in ascending UUID order before the user and ordered
memberships, rechecks discovery after locking, and retries bounded membership
set drift. A sole-member organization is deleted; a membership is removed when
another owner remains; a sole owner of a multi-member organization receives
`organization_ownership_transfer_required` with no partial deletion. Affected
active-organization session preferences are cleared before cascades. Local
automation cleanup follows the same path and returns the actual
`deletedOrganizations` count. Only after commit does the API expire the browser
cookie. API keys remain iteration 7 and have no cleanup behavior here.

Session list cursors are opaque versioned base64url values for
`(lastSeenAt, id)` ordering with a checksum for format/corruption detection.
Clients must not decode or synthesize them. Lists include only unexpired rows;
single revoke is ownership-qualified and cannot target the current id;
revoke-others uses one set-based delete and preserves the current browser.

## Security audit contract

Structured OAuth events contain only the closed operation/provider id, stable
outcome, correlation id, and authenticated user id when applicable after a
trusted session/provider result. Structured account events contain the closed
operation, stable outcome, authenticated user id, an optional closed provider
id or opaque session id, and the existing correlation/trace logging scope.

Neither event family may contain email, provider subject, display name, avatar
or other raw profile data, raw provider error/description, authorization code,
state, access/refresh token, cookie, protected ticket, lookup hash, password,
client secret, or certificate material. Metrics derived from these events may
use only bounded operation/outcome and closed provider labels; correlation,
user, and session ids are audit fields, not metric labels. No separate metrics
backend is introduced by this iteration.

## Health and failure diagnosis

`/api/health/live` does not touch PostgreSQL, including when the request carries
a valid session cookie. `/api/health` and `/api/health/ready` require
connectivity and a queryable `auth.users` relation. Health responses never
expose connection strings or schema errors.

Auth responses are never cached. Diagnose failures by stable Problem Details
`code` and `traceId`; do not log or display passwords, cookies, ticket data, or
backend `detail`.

## Rollback and production gate

The iteration-3/4 migrations are additive over the clean target schema. Rolling
application code back may leave the new tables, columns, and indexes unused.
Generated `Down` paths are destructive and restricted to disposable
Development/Test databases; production rollback uses restore or forward-fix
procedures and never touches `template/`.

The iteration-4 production security contracts are implemented: persistent
PostgreSQL Data Protection keys, mandatory mounted RSA certificate protection,
complete provider-pair validation, secure cookie/CSRF handling, and no provider
token storage. Deployment is still gated on operator-supplied PFX/provider
secrets, exact provider-console callback registrations, HTTPS/same-origin
topology, database backup/restore procedures, and external-provider smoke in
that environment. KMS/Vault and certificate-rotation orchestration remain
future deployment decisions. API-key `x-api-key` support remains iteration 7,
and no Bearer scheme is registered.
