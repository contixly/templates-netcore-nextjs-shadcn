# API conventions

## Scope and ownership

ASP.NET Core owns every `/api/**` route. Consumer APIs use URL versioning under
`/api/v1`; operational health routes remain unversioned. The Next.js application
uses REST only and does not access the database or authentication store directly.

## Success and error bodies

Successful JSON responses use a typed envelope:

```json
{ "data": {} }
```

API failures use `application/problem+json` with required non-null RFC Problem
Details fields `type`, `title`, `status`, `detail`, and `instance`, plus required
stable `code` and `traceId`. Validation responses also require an `errors`
dictionary. Each segment of a dotted validation property path is camel-cased,
and messages from source keys that normalize to the same JSON path are merged.
The initial codes are `invalid_request`, `validation_failed`, `unauthorized`,
`forbidden`, `not_found`, `method_not_allowed`, and `internal_error`.
Authentication adds `antiforgery_failed`,
`local_auth_invalid_credentials`, `local_auth_user_required`,
`local_auth_disabled`, `local_auth_user_exists`, and `rate_limited`.
The iteration-4 account and external-auth surface additionally uses
`invalid_return_url`, `external_provider_not_configured`,
`already_authenticated`, `external_auth_failed`, `external_email_required`,
`external_email_unverified`, `external_identity_conflict`,
`external_email_conflict`, `oauth_flow_context_changed`, `invalid_cursor`,
`external_connection_required`, `external_connection_not_found`,
`account_session_not_found`, `current_session_cannot_be_revoked`, and
`concurrency_conflict`.

The API error contract takes precedence over request content negotiation:
an incompatible `Accept` header does not suppress or downgrade Problem Details.

`type` is always `urn:template:problem:{code}`. Client code branches on `code`,
not on invariant-English `title`, `detail`, or validation messages. API responses
never expose stack traces, exception messages, SQL, secrets, cookies, or
authorization headers.

Health `503` is a typed health result rather than a Problem Details failure.

## Validation and authorization

Minimal API binding and Data Annotations validate request DTOs and parameters at
the HTTP boundary. Domain and application rules remain independent of HTTP
validation. Endpoint composition creates one central `/api/v1` consumer group
with the named authenticated-user policy and gives modules that group for
consumer mappings; public operations explicitly opt out with `AllowAnonymous`.

Authenticated browser endpoints require policy `Api.BrowserSession`. Its
primary scheme is `Template.Session`; session issuance and replacement use
internal scheme `Template.Session.Issuer`. The issuer has a write-only cookie
manager, while both schemes share the persistent ticket-store format and Data
Protection purpose. This prevents an existing request cookie from being read as
the replacement during credential changes and key rotation.
`Template.Session.Selector` is `DefaultAuthenticateScheme`; it forwards
ordinary requests to the primary scheme and forwards only the canonical
liveness path and its route-equivalent trailing-slash form to a process-only
no-result handler.
Authorization policies still name the primary scheme; the selector and
process-only handler accept no credentials and are not consumer auth schemes.

Both schemes write `__Host-template.session` with `HttpOnly`, `Secure`,
`SameSite=Lax`, `Path=/`, no `Domain`, and persistent seven-day sliding
expiration. The cookie contains only a protected opaque key. PostgreSQL stores
only its SHA-256 hash and a separately Data-Protection-protected authentication
ticket through `ITicketStore`; the database record is the revocation source of
truth. API challenge/forbid returns `401`/`403` and never redirects to HTML.
Sliding renewal is suppressed for SSR-marked authenticated `GET` projections,
because a Next.js Server Component cannot propagate the API `Set-Cookie` header
to the browser. An unmarked same-origin browser read uses the normal cookie
handler and renews both the persisted ticket and browser cookie after half-life.
The session projection additionally completes that renewal before producing its
timestamps.

The implemented browser authentication surface is:

| Operation                                         | Access and mutation policy                                                                                                                    |
| ------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------- |
| `GET /api/v1/auth/capabilities`                   | anonymous, no-store                                                                                                                           |
| `GET /api/v1/auth/session`                        | anonymous `200` projection for both authenticated and anonymous state, no-store                                                               |
| `GET /api/v1/auth/csrf`                           | anonymous, issues the paired antiforgery cookie/request token, no-store                                                                       |
| `POST /api/v1/auth/logout`                        | `Api.BrowserSession` plus CSRF; revokes only the current session                                                                              |
| `POST /api/v1/auth/external/{provider}/challenge` | conditional auth by intent plus CSRF; returns an API-issued HTTPS authorization URL for a configured provider                                 |
| `POST /api/local-auth/scenario`                   | local-only two-part gate, CSRF, 20 requests per IP per minute                                                                                 |
| `POST /api/local-auth/sign-in`                    | local-only two-part gate, CSRF, 10 requests per IP per five minutes                                                                           |
| `DELETE /api/local-auth/scenario`                 | local-only two-part gate, `Api.BrowserSession`, CSRF; atomic cleanup; `409` when ownership transfer or a stable concurrency retry is required |

Every auth response uses `Cache-Control: no-store`. Local operations are
available only when the environment is `Development` or `Test` **and**
`LocalAutomationAuth:Enabled=true`; all other environments return
`404 local_auth_disabled`, including Production when the flag is accidentally
true. Their OpenAPI operations carry tag `local-only` and
`x-local-only: true`.

The Application local-namespace policy remains the source of truth for which
normalized scenario emails are eligible. Infrastructure classifies a failed
local-user creation only when its non-empty Identity error set is homogeneous:
duplicate-only built-in codes use the separate `409 local_auth_user_exists`
path, while recognized built-in username, email, or password input-validation-
only codes report the typed condition that rolls back into the existing
`400 validation_failed` path. An unknown/custom code, or a mix of recognized
categories (including duplicate plus custom), remains an unexpected,
non-disclosing `500 internal_error` failure: the transaction rolls back, no
user/session or cookie is created, and the provider code and detail are not
exposed.

The browser never reads the HttpOnly cookie and never stores a bearer token.
Browser requests send the same-origin cookie automatically. The combined
Next.js SSR auth read uses two isolated generated-SDK clients in parallel:
capabilities receives only the incoming correlation ID and therefore cannot
authenticate or slide a session, while session receives `Cookie`, correlation
ID, and the narrow `X-Template-Session-Renewal: suppress` marker. That header
can only prevent renewal on a safe `GET`; account Server Component loaders use
the same marker for their cookie-bearing projections.
The authenticated UI then performs an unmarked generated-SDK session refresh
in the browser so any half-life `Set-Cookie` reaches the cookie jar. Every
unsafe browser operation first obtains a request token from
`GET /api/v1/auth/csrf` and sends it in `X-CSRF-TOKEN`; the paired
`__Host-template.antiforgery` cookie is HttpOnly, Secure, SameSite Strict,
Path `/`, and has no Domain. The deployment is same-origin, so CORS is not
enabled.

## External OAuth

ASP.NET Core uses OpenIddict Client, not OpenIddict Server, for the five closed
provider ids `google`, `github`, `gitlab`, `vk`, and `yandex`. A provider is
advertised and challengeable only when a valid public origin and its complete
client-id/client-secret pair are configured. Zero providers is valid; an
unknown or partial provider block fails option validation without logging its
values.

`POST /api/v1/auth/external/{provider}/challenge` is the only versioned REST
OAuth operation. Its strict JSON body selects `signIn` or `connect` and may
provide a safe same-origin return path. It always requires a fresh CSRF pair.
`signIn` requires an anonymous browser; `connect` requires the current
`Api.BrowserSession`. Full URLs, network paths, backslashes, controls,
malformed escapes, encoded-separator confusion in the escaped pathname,
`/api/**`, and `/auth/**` return targets fail closed. Encoded `/` and `%`
characters remain valid in query and fragment data; repeated decoding still
rejects controls or backslashes anywhere in the target. The response contains
an absolute HTTPS authorization URL produced by the server; the browser
validates its structure but does not duplicate provider host configuration.
Production Google challenges add `prompt=select_account` through OpenIddict Web
Integration; non-production Google challenges omit it. No other provider
inherits that Google-specific parameter.

The provider protocol callbacks are stable, unversioned, accept only `GET` and
`POST`, and are deliberately excluded from OpenAPI and the generated browser
SDK:

| Provider | Callback                           |
| -------- | ---------------------------------- |
| Google   | `/api/auth/callback/google`        |
| GitHub   | `/api/auth/callback/github`        |
| GitLab   | `/api/auth/callback/gitlab`        |
| VK       | `/api/auth/callback/vk`            |
| Yandex   | `/api/auth/oauth2/callback/yandex` |

Callback state is Data-Protection protected and backed by one-time OpenIddict
state-token rows in PostgreSQL. A connect state binds both the initiating user
and persistent session id; the callback revalidates both before changing the
account. Callback rate limiting runs before authentication/provider exchange.
Failures redirect only to `/auth/error?code=<allow-listed-code>` and never
place raw provider errors, state, authorization codes, tokens, subjects, email,
or stack traces in the browser URL.

Provider identity/email normalization is:

| Provider | Stable subject        | Accepted email evidence                                                                |
| -------- | --------------------- | -------------------------------------------------------------------------------------- |
| Google   | `sub`                 | one email plus exactly one `email_verified=true` claim                                 |
| GitHub   | positive numeric `id` | exactly one primary, verified address from the bounded `/user/emails` backchannel call |
| GitLab   | `sub`                 | one email plus exactly one `email_verified=true` claim                                 |
| VK       | `user_id`             | provider user-info email, treated as provider-confirmed by the approved mapping        |
| Yandex   | string `id`           | `default_email`, treated as provider-confirmed by the approved mapping                 |

Subjects and profile values are bounded; avatars must be credential-free HTTPS
URLs. GitHub and Yandex backchannel tokens exist only in callback memory.
The production callback owns a mutable ephemeral token bag and clears it in
`finally`; normalization additionally makes a best-effort clear when its
`IReadOnlyDictionary` input is mutable. Immutable/read-only callers retain
cleanup ownership. Regardless of that in-memory cleanup boundary, access/refresh
tokens are not persisted to Identity, account tables, OpenIddict rows, browser
storage, logs, or responses. There is no offline-access scope, provider API
token vault, remote token refresh, or provider-side consent revocation in this
iteration.

## Account lifecycle

All account operations use `Api.BrowserSession`, return `Cache-Control:
no-store`, and expose only typed `{ "data": ... }` projections:

| Operation                                       | Mutation rule                                                                                                 |
| ----------------------------------------------- | ------------------------------------------------------------------------------------------------------------- |
| `GET /api/v1/account`                           | current profile, primary/secondary verified emails, id and creation time                                      |
| `PATCH /api/v1/account/profile`                 | CSRF; trimmed display name of 2–50 non-control characters                                                     |
| `GET /api/v1/account/connections`               | configured providers plus connections whose runtime configuration was removed                                 |
| `DELETE /api/v1/account/connections/{provider}` | CSRF; atomic, ownership-checked local disconnect                                                              |
| `GET /api/v1/account/sessions?cursor=&limit=`   | unexpired sessions; default 20, accepted limit 1–100                                                          |
| `DELETE /api/v1/account/sessions/{sessionId}`   | CSRF; ownership-qualified; current session is rejected                                                        |
| `DELETE /api/v1/account/sessions/others`        | CSRF; one set-based delete preserving the current persistent id                                               |
| `DELETE /api/v1/account`                        | CSRF; strict primary-email confirmation and hard delete; ownership/concurrency cleanup conflicts return `409` |

The normalized verified-email value is globally unique. A new anonymous
provider subject links to the owner of a matching primary or secondary
verified email only while another provider login owned by that user still
references that exact email row. A historical primary email with no current
provider vouch returns an email conflict instead of implicitly linking. An
otherwise free email creates a user and primary email. Explicit connect may
reuse an email already owned by the current user or add a free secondary
verified email, but an email owned by another user is a conflict. A newly
connected provider may update display name and HTTPS avatar. Ordinary repeat
sign-in for an unchanged `(provider, subject, email)` updates only the provider
connection's `lastUsedAt`; it does not reapply profile data.

For an existing `(provider, subject)`, a changed incoming email is reconciled
inside the same transaction. If another user owns it, the operation fails
without moving the login. If the current user already owns it, the login is
reassociated with that verified-email row. If it is free, a secondary verified
email is created and the login is reassociated. The first existing-login lookup
locks that `(provider, subject)` row with `FOR UPDATE` for the surrounding
authentication transaction, then reads its current verified email. Ownership
validation and reassociation therefore use one serialized snapshot. The update
path retains a defensive lock/reload before selecting the previous email, so
cleanup applies to the email actually replaced. That previous non-primary email
is then deleted only when no login still references it; a primary email is
never removed. Connect preserves `lastUsedAt`, while sign-in records the new
use time.

Disconnect is local only: it deletes the selected login and deletes its
non-primary secondary email only when no remaining provider connection vouches
for that row. Primary email is never deleted. The current authentication
provider cannot be disconnected, and every removal must leave at least one
connected provider that is configured in the startup-stable runtime catalogue.
Stored logins whose runtime provider configuration was removed remain visible,
but they do not count as usable survivors. The server passes the configured set
through the Application use-case and persistence port and re-evaluates the same
set inside the locked write path. Local automation credentials do not count.
Remote provider consent remains active because no remote token is retained.

Session results are ordered by `(lastSeenAt DESC, id DESC)`. `nextCursor` is a
versioned, canonical base64url encoding of that tuple with a checksum for
format/corruption detection. It is opaque rather than a cryptographic
authorization token: clients must return it verbatim, and malformed or modified
values return `400 invalid_cursor`. Listings read only relational safe metadata,
never the protected ticket or its lookup hash. User agents are bounded and IPs
are redacted to IPv4 `/24` or IPv6 `/64`.

Profile writes update the Identity user and timestamp atomically. External
reconciliation executes in one transaction per attempt and retries a classified
uniqueness race once with fresh reads. Disconnect locks and revalidates the
user's connection snapshot, deletes the login, and conditionally removes the
email in one transaction. Only after reconciliation commits does the callback
issue a new provider-authenticated browser session for sign-in or rotate the
existing session principal for connect; browser-session issuance/rotation is
deliberately outside the account transaction. Account deletion commits the
Identity user delete first; database cascades remove verified emails, logins,
claims/tokens, and all persistent sessions, after which the API expires the
current cookie. Session revokes are ownership-qualified SQL deletes, and
missing/foreign ids share the same `404`.

## Health

- `GET /api/health` is the compatibility alias for readiness.
- `GET /api/health/live` excludes dependency checks and selects the process-only
  authentication handler, so even a valid session cookie cannot read PostgreSQL.
- `GET /api/health/ready` runs checks tagged `ready`.
- Health responses expose only `status` and UTC `timestamp`.
- Healthy responses use `200`; unhealthy readiness uses `503`.
- Every health response uses `Cache-Control: no-store`.
- Readiness opens `ConnectionStrings:Postgres` and requires queryable
  `auth.users` and `organizations.organizations` relations. Operators must
  apply migrations through
  `20260730091827_OrganizationsMembershipOnboarding`; missing configuration,
  connectivity, or either relation returns unhealthy without exposing database
  detail.
- The database check is tagged `ready` and never participates in liveness.

`Template.Api` never applies migrations automatically. Operators restore the
repository tool manifest and run EF with
`Template.Infrastructure.csproj` as `--project` and `Template.Api.csproj` as
`--startup-project`; both keep EF Design private, and `Template.Api` remains the
only HTTP host. Full commands and rollback policy are in
`docs/authentication-persistence-operations.md`.

## Correlation and logging

`X-Correlation-ID` is accepted only when it contains exactly one non-empty value
that is 1–64 characters and restricted to ASCII letters, digits, `.`, `_`, or
`-`. Invalid input is ignored. The canonical value appears in the response
header, Problem Details `traceId`, and the `TraceId` logging scope. The response
header is restored immediately before headers are sent, so handled exceptions
that reset the response preserve the same correlation value.

Completion logs contain method, the matched route template, status, elapsed
milliseconds, and trace scope. Unmatched API paths use the fixed
`/api/{unmatched}` fallback, and generic exception logs reuse that safe path.
Raw paths and route values, including name-derived organization slugs, are never
logged. Bodies, query values, cookies, and credential headers are not logged.
Health completion is `Debug`; normal API success is `Information`; 4xx is
`Warning`; 5xx is `Error`.

OAuth and account security audit events have a separate bounded contract:

- OAuth records a closed operation, closed provider id, stable outcome,
  correlation id, and authenticated user id only when applicable after a
  trusted session/provider result exists;
- account records a closed operation, stable outcome, authenticated user id,
  and an optional closed provider id or opaque session id; correlation comes from the
  existing `TraceId` logging scope;
- both exclude email, provider subject, display name/avatar/raw profile,
  provider error/description, authorization code, state, access/refresh token,
  cookie, protected ticket, lookup hash, and credential values.

Metrics derived from these events may label only the closed provider and
bounded operation/outcome sets. Correlation, user, and session ids are
high-cardinality audit fields and must not become metric labels. This defines
the label contract; iteration 4 does not add a separate metrics backend.

Problem Details/status middleware is limited to `/api/**`, preserving future
Next.js/YARP response ownership.

## OpenAPI

The canonical document is OpenAPI 3.1 document `v1`. Runtime
`/api/openapi/v1.json` exists only in `Development` and `Test`. Production does
not expose a dynamic document or documentation UI.

Cookie authentication is described as cookie `apiKey` scheme `cookieAuth` with
name `__Host-template.session`. Protected operations carry its security
requirement; anonymous operations do not.

Only `cookieAuth` is advertised. The internal `Template.Session.Issuer` scheme
is not a consumer contract; API-key/`x-api-key` remains iteration 7, and no
Bearer scheme is registered or published. Local operations remain present for
generated automation clients but are marked with `local-only` and
`x-local-only: true`.

The external challenge publishes conditional security (`anonymous` for
`signIn`, `cookieAuth` for `connect`) and its required antiforgery header. The
eight account operations publish their exact cookie requirement, mutation
antiforgery headers, strict bodies, closed provider/authentication-method
values, and typed Problem Details alternatives. Provider protocol callbacks are
not REST consumer operations and remain excluded from the document and
generated SDK, as do provider subjects, credentials, tokens, and endpoint
hosts.

The session `limit` range/default is machine-readable, but the current document
does not fully publish the canonical cursor/`nextCursor` length/pattern or
`maxItems` for every account collection. Runtime validation and bounded
projections remain authoritative; this is a known contract-metadata/evidence
gap rather than permission to synthesize cursors or unbounded responses.

Success-envelope schemas require non-null `data`. Standard and validation
Problem Details schemas publish the same required invariant fields that runtime
customization always writes; validation additionally requires `errors`.
Scenario creation has an optional request body, and an empty body means all
scenario values are generated. Credential sign-in has a required request body
with non-null `email` and `password`. Any non-empty manually read auth body must
use a JSON media type; non-JSON input is rejected as `400 invalid_request`.
Malformed UTF-8 is rejected before JSON deserialization with the same stable
problem. Scenario name/email constraints apply after trimming, so the schema
uses `x-trimmed-min-length`, `x-trimmed-max-length`, `x-trimmed-format`, and
`x-trimmed-pattern` instead of raw length/format/pattern keywords that would
reject accepted padded input. `x-trimmed-pattern` is anchored and uses explicit
ASCII case classes to communicate the case-insensitive
`local-agent+...@local-agent.test` namespace after trimming and lowercase
normalization.
The unsafe logout and cleanup operations publish plain `ProblemDetails` for
`400` antiforgery failures; scenario creation and credential sign-in publish
`ProblemDetails | HttpValidationProblemDetails`, matching plain antiforgery,
malformed/non-JSON, or structured field-validation failures.

Export and verify from the repository root:

```bash
dotnet restore Template.sln
dotnet build apps/api/src/Template.Api/Template.Api.csproj \
  --no-restore \
  -p:OpenApiGenerateDocuments=true
dotnet test Template.sln --no-restore
git diff --exit-code -- contracts/openapi/v1.json
```

Commit `contracts/openapi/v1.json` with every intentional contract change.
Breaking field removal, semantic change, or incompatible status/auth change
requires a documented `/api/v2` and deprecation decision.

## Organizations and membership (iteration 5)

All organization operations are authenticated `Api.BrowserSession` projections
with `Cache-Control: no-store`; every unsafe operation also requires the normal
`X-CSRF-TOKEN` antiforgery pair. Success uses the standard `{ "data": ... }`
envelope. The strict request bodies reject unknown JSON members.

| Method             | Route                                                           | Result                                                                     |
| ------------------ | --------------------------------------------------------------- | -------------------------------------------------------------------------- |
| `GET`              | `/api/v1/organizations?cursor=&limit=`                          | accessible organization page                                               |
| `POST`             | `/api/v1/organizations`                                         | create organization, owner membership, and current session context (`201`) |
| `GET`              | `/api/v1/organizations/by-key/{organizationKey}`                | accessible detail by slug or UUID key                                      |
| `PATCH` / `DELETE` | `/api/v1/organizations/{organizationId}`                        | update; or exact-case name-confirmed deletion                              |
| `PUT`              | `/api/v1/auth/session/active-organization`                      | set the current persistent session's active organization                   |
| `GET` / `POST`     | `/api/v1/organizations/{organizationId}/members?cursor=&limit=` | list; or direct-add a user (`201`)                                         |
| `PATCH`            | `/api/v1/organizations/{organizationId}/members/{memberId}`     | change the member's role                                                   |

There is deliberately no member-delete operation in this slice. `GET
/api/v1/auth/session` adds nullable `activeOrganizationId` only to the
authenticated session projection.

`owner`, `admin`, and `member` are closed organization roles—not Identity roles
or session claims. Owners can update/delete and add/change roles, including
owner assignment; admins can update and add/change only `member` or `admin`
roles; members have no mutation capability. A caller cannot change their own
role; admins cannot mutate an owner; redundant changes conflict; role changes
preserve at least one owner. Server-computed capabilities are presentation aids,
not authorization substitutes.

Organizations use a UUID id and a canonical lower-case slug in disjoint
namespaces. User-supplied UUID-shaped slugs are rejected after trim/lowercase
normalization, and a UUID-shaped name-derived base is deterministically prefixed
with `workspace-`. A UUID key resolves only by organization id; a non-UUID key
resolves only by slug. Returned `canonicalKey` is always the slug. Organization
pages sort by `(normalizedName ASC, id ASC)` and member pages by
`(joinedAt ASC, id ASC)`; both use opaque versioned base64url checksum cursors,
default `50`, range `1..100`. Clients return `nextCursor` verbatim and never
construct it. A checksum-valid organization cursor is accepted only when its
decoded normalized-name position satisfies the same 1–50 UTF-16-code-unit,
Unicode-letter/decimal-digit plus ordinary space/hyphen/underscore policy as a
runtime organization name and has no outer whitespace. Empty, control,
unsupported-symbol, and overlength positions return `400 invalid_cursor`
before any PostgreSQL query.

Missing and foreign organizations intentionally share `404
organization_not_found`; foreign/missing members likewise do not disclose
resources. Permission failures use `organization_permission_denied` or
`role_assignment_forbidden`. Other stable outcomes include
`organization_name_conflict`, `organization_slug_conflict`,
`last_organization_required`, `organization_confirmation_mismatch`,
`target_user_not_found`, `member_not_found`, `member_already_exists`,
`member_role_unchanged`, `member_domain_acknowledgement_required`,
`organization_ownership_transfer_required`, `invalid_cursor`, and
`concurrency_conflict`. The domain-acknowledgement metadata is returned only to
authorized add-member callers and the initial warning request performs no write.

A single `TemplateDbContext` PostgreSQL transaction is the organization boundary:
create serializes through the actor user, creates the organization/owner and
sets that session's active organization atomically; update locks and rechecks
permission while replacing domains; set-active is membership-qualified for the
current unexpired session; delete locks the organization, actor membership and
accessible set, requires another accessible organization, and relies on the FK
`SET NULL`; add/change-role lock and re-evaluate all relevant membership/owner
state. Unique indexes are authoritative for slug/member races and classified
results are retried only where the persistence operation specifies it.

Set-active performs one membership-qualified update and does not take an
exclusive organization `FOR UPDATE` lock, so concurrent selections and
nonmember attempts do not serialize through an organization row. The exact
PostgreSQL `23503` race for
`fk_sessions_organizations_active_organization_id` maps to the same
non-disclosing not-found result when deletion wins; unrelated FK violations
remain unhandled programming/data errors, while serialization/deadlock outcomes
retain `concurrency_conflict`.

Organization-detail and member-list reads do not acquire `FOR UPDATE` locks.
Each authorizes and projects its organization row/role and allowed domains from
one PostgreSQL repeatable-read snapshot, so concurrent reads progress together
while organization deletion, access removal, or a settings update yields a
wholly pre-change or post-change projection rather than a torn response.
Mutation lock and recheck behavior is unchanged.

The `organizations` schema contains `organizations`, `members`, and
`allowed_email_domains`; `auth.sessions.active_organization_id` is nullable,
indexed, and an FK to `organizations.organizations` with `SET NULL`. The active
preference is persistent session state, outside the protected ticket, and is
preserved across ticket renewal.
