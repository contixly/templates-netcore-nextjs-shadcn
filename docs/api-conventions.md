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
Sliding renewal is suppressed only for an SSR-marked
`GET /api/v1/auth/session`, because a Next.js Server Component cannot propagate
the API `Set-Cookie` header to the browser. An unmarked same-origin browser
session read uses the normal cookie handler and renews both the persisted ticket
and browser cookie after half-life.

The implemented browser authentication surface is:

| Operation                                         | Access and mutation policy                                                                                    |
| ------------------------------------------------- | ------------------------------------------------------------------------------------------------------------- |
| `GET /api/v1/auth/capabilities`                   | anonymous, no-store                                                                                           |
| `GET /api/v1/auth/session`                        | anonymous `200` projection for both authenticated and anonymous state, no-store                               |
| `GET /api/v1/auth/csrf`                           | anonymous, issues the paired antiforgery cookie/request token, no-store                                       |
| `POST /api/v1/auth/logout`                        | `Api.BrowserSession` plus CSRF; revokes only the current session                                              |
| `POST /api/v1/auth/external/{provider}/challenge` | conditional auth by intent plus CSRF; returns an API-issued HTTPS authorization URL for a configured provider |
| `POST /api/local-auth/scenario`                   | local-only two-part gate, CSRF, 20 requests per IP per minute                                                 |
| `POST /api/local-auth/sign-in`                    | local-only two-part gate, CSRF, 10 requests per IP per five minutes                                           |
| `DELETE /api/local-auth/scenario`                 | local-only two-part gate, `Api.BrowserSession`, CSRF; atomically removes the local user and all sessions      |

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
can only prevent a renewal and is recognized only on the session-read path.
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
encoded-separator confusion, `/api/**`, and `/auth/**` return targets fail
closed. The response contains an absolute HTTPS authorization URL produced by
the server; the browser validates its structure but does not duplicate provider
host configuration.

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
Access/refresh tokens are cleared on the normalization path and are not stored
in Identity, account tables, OpenIddict rows, browser storage, logs, or
responses. There is no offline-access scope, provider API token vault, remote
token refresh, or provider-side consent revocation in this iteration.

## Account lifecycle

All account operations use `Api.BrowserSession`, return `Cache-Control:
no-store`, and expose only typed `{ "data": ... }` projections:

| Operation                                       | Mutation rule                                                                 |
| ----------------------------------------------- | ----------------------------------------------------------------------------- |
| `GET /api/v1/account`                           | current profile, primary/secondary verified emails, id and creation time      |
| `PATCH /api/v1/account/profile`                 | CSRF; trimmed display name of 2–50 non-control characters                     |
| `GET /api/v1/account/connections`               | configured providers plus connections whose runtime configuration was removed |
| `DELETE /api/v1/account/connections/{provider}` | CSRF; atomic, ownership-checked local disconnect                              |
| `GET /api/v1/account/sessions?cursor=&limit=`   | unexpired sessions; default 20, accepted limit 1–100                          |
| `DELETE /api/v1/account/sessions/{sessionId}`   | CSRF; ownership-qualified; current session is rejected                        |
| `DELETE /api/v1/account/sessions/others`        | CSRF; one set-based delete preserving the current persistent id               |
| `DELETE /api/v1/account`                        | CSRF; strict primary-email confirmation and hard delete                       |

The normalized verified-email value is globally unique. A new anonymous
provider subject links to the owner of a matching primary or secondary
verified email; otherwise it creates a user and primary email. Explicit connect
may reuse an email already owned by the current user or add a free secondary
verified email, but an email owned by another user is a conflict. A newly
connected provider may update display name and HTTPS avatar. Ordinary repeat
sign-in updates only the provider connection's `lastUsedAt`.

Disconnect is local only: it deletes the selected login and deletes its
non-primary secondary email only when no remaining provider connection vouches
for that row. Primary email is never deleted. The current authentication
provider and the final production provider connection cannot be disconnected,
even if local automation credentials exist; the server re-evaluates these rules
inside the write path. Remote provider consent remains active because no remote
token is retained.

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
email in one transaction. Account deletion commits the Identity user delete
first; database cascades remove verified emails, logins, claims/tokens, and all
persistent sessions, after which the API expires the current cookie. Session
revokes are ownership-qualified SQL deletes, and missing/foreign ids share the
same `404`.

## Health

- `GET /api/health` is the compatibility alias for readiness.
- `GET /api/health/live` excludes dependency checks and selects the process-only
  authentication handler, so even a valid session cookie cannot read PostgreSQL.
- `GET /api/health/ready` runs checks tagged `ready`.
- Health responses expose only `status` and UTC `timestamp`.
- Healthy responses use `200`; unhealthy readiness uses `503`.
- Every health response uses `Cache-Control: no-store`.
- Readiness opens `ConnectionStrings:Postgres` and requires a queryable
  `auth.users` relation; missing configuration, connectivity, or schema returns
  unhealthy without exposing database detail.
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

Completion logs contain method, path without query, status, elapsed milliseconds,
and trace scope. Bodies, query values, cookies, and credential headers are not
logged. Health completion is `Debug`; normal API success is `Information`; 4xx
is `Warning`; 5xx is `Error`.

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
