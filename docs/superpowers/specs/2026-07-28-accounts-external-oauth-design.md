# Итерация 4: accounts и внешний OAuth

**Дата:** 2026-07-28

**Статус:** дизайн согласован пользователем

**Долгосрочная дорожная карта:** [`../../aspnetcore-migration-plan.md`](../../aspnetcore-migration-plan.md)

## 1. Цель

Итерация 4 восстанавливает account lifecycle из immutable reference
`template/` на целевой архитектуре:

- ASP.NET Core 10 является единственным владельцем `/api/**`,
  аутентификации, авторизации, бизнес-правил и persistence;
- отдельный Next.js UI использует только REST и сгенерированный SDK;
- браузер получает только secure HttpOnly same-origin session cookie;
- Prisma, Better Auth, Server Actions, browser bearer storage и прямой доступ UI
  к базе отсутствуют.

Пользователь может войти через один из пяти внешних providers, подключить и
отключить provider, изменить display name, просмотреть и отозвать активные
сессии и удалить аккаунт. Password lifecycle в production не переносится:
password остаётся только механизмом уже существующей local automation.

Итерация использует чистую Identity-базу из итерации 3. Данные, users,
sessions, OAuth accounts и secrets из reference не мигрируются.

## 2. Изученный контекст

Перед проектированием проверены:

- корневой `AGENTS.md`;
- `docs/aspnetcore-migration-plan.md`, API/web conventions и authentication
  operations;
- текущие Domain, Application, Infrastructure и Api projects;
- текущие Identity entities, `AuthDbContext`, EF migration,
  PostgreSQL-backed `ITicketStore`, cookie/CSRF/auth capability contracts;
- текущий Next.js generated client, SSR/browser adapters, route boundaries,
  Jest и Playwright harness;
- reference routes `/auth/login`, `/auth/error` и
  `template/src/app/(protected)/(global)/user/**`;
- весь `template/src/features/accounts`, account actions, schemas, errors,
  components и navigation;
- reference auth server/client, five-provider configuration и catch-all auth
  handler;
- Prisma `User`, `Session`, `Account` и `Verification` models;
- account settings, profile/connections, session security и delete-account
  reference documentation;
- reference unit/integration and Playwright account/session scenarios;
- имена OAuth configuration keys в `template/.env`; значения не выводились и
  не копировались в tracked files.

Установленная Next.js документация должна быть перечитана непосредственно
перед изменением `apps/web`, как требует repository policy.

Актуальные платформенные решения сверены с официальной документацией:

- [ASP.NET Core social authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/social/?view=aspnetcore-10.0);
- [OpenIddict ASP.NET Core integration](https://documentation.openiddict.com/integrations/aspnet-core);
- [OpenIddict web providers](https://documentation.openiddict.com/integrations/web-providers);
- [OpenIddict remote-server client integration](https://documentation.openiddict.com/guides/getting-started/integrating-with-a-remote-server-instance.html);
- [OpenIddict Entity Framework Core integration](https://documentation.openiddict.com/integrations/entity-framework-core);
- [OpenIddict Data Protection integration](https://documentation.openiddict.com/integrations/aspnet-core-data-protection);
- [ASP.NET Core Data Protection key storage](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-storage-providers?view=aspnetcore-10.0);
- [ASP.NET Core Data Protection configuration](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0);
- [Google OpenID Connect](https://developers.google.com/identity/openid-connect/openid-connect);
- [GitHub OAuth authorization](https://docs.github.com/en/apps/oauth-apps/using-oauth-apps/authorizing-oauth-apps);
- [GitHub user emails API](https://docs.github.com/en/rest/users/emails);
- [GitLab OpenID Connect provider](https://docs.gitlab.com/integration/openid_connect_provider/);
- [VK ID OAuth web flow](https://id.vk.com/about/business/go/docs/ru/vkid/latest/vk-id/connection/start-integration/auth-without-sdk-web/);
- [Yandex ID user information](https://yandex.com/dev/id/doc/en/user-information);
- [Better Auth built-in OAuth callback](https://better-auth.com/docs/concepts/oauth);
- [Better Auth Generic OAuth callback](https://better-auth.com/docs/plugins/generic-oauth).

## 3. Согласованные решения

1. Используется OpenIddict Client с Web Integration, но не OpenIddict Server.
   ASP.NET Core Identity остаётся source of truth для пользователей.
2. Поддерживаются Google, GitHub, GitLab, VK и Yandex. Capabilities и UI
   показывают только полностью сконфигурированных providers.
3. Production password registration/login/reset/change UI не создаётся.
   Существующий password flow остаётся только local automation.
4. Browser E2E с реальными credentials проверяет наличие кнопки и открытие
   provider authorization screen, но не выполняет реальный вход.
   Успешные callbacks проверяются детерминированным fake-provider integration
   harness.
5. Вход или connect отклоняется, если provider не вернул email либо email не
   удовлетворяет provider-specific verified policy.
6. Explicit connect с другим verified email разрешён, если email не принадлежит
   другому user. Он добавляется как дополнительный verified email, а primary
   email не меняется.
7. Если verified email уже принадлежит текущему user, provider login можно
   добавить без создания ещё одной email-записи.
8. Anonymous sign-in с новым `(provider, subject)` автоматически привязывается
   к владельцу совпавшего primary или secondary verified email.
9. Отключение provider удаляет созданный им secondary email, только если этот
   email больше не подтверждается другой external connection. Primary email не
   удаляется.
10. Новое provider connection обновляет display name и HTTPS avatar данными
    provider. Обычный повторный sign-in обновляет только `lastUsedAt`.
11. Provider access/refresh tokens не сохраняются. Для будущих provider API
    сохраняется архитектурный seam нормализованного provider gateway; не
    создаются неиспользуемые token-vault interfaces или таблицы до первого
    provider API.
12. Data Protection keys persist в PostgreSQL. Production защищает key ring
    X.509 certificate из mounted secret и падает при отсутствии обязательной
    конфигурации. KMS/Vault сейчас не добавляются.
13. Локальные OAuth credentials могут быть вручную перенесены из
    `template/.env` в ignored
    `apps/api/src/Template.Api/appsettings.Local.json`. Runtime не читает
    reference.
14. Existing localhost callback registrations сохраняются совместимыми:
    versioned REST запускает flow, а внешние protocol callbacks имеют стабильные
    unversioned reference paths.

## 4. Scope и dependency gate

### Входит

- five-provider OAuth sign-in;
- explicit provider connect/disconnect;
- primary и secondary verified-email ownership;
- OpenIddict state-token persistence и replay protection;
- persistent/encrypted production Data Protection key ring;
- profile read/update;
- configured connections read/disconnect;
- active-session list, cursor pagination, revoke one и revoke all others;
- hard account deletion;
- account/auth REST, authorization, validation, Problem Details, CSRF,
  rate limits и audit telemetry;
- `/auth/login`, `/auth/error`, `/user/profile`, `/user/connections`,
  `/user/security`, `/user/danger` и `/user` redirect;
- EF migration, OpenAPI/generated SDK, .NET, UI, contract и E2E tests;
- durable API/web/auth/migration documentation.

### Не входит

- production password lifecycle, email/password registration, password reset,
  password change, 2FA и email delivery;
- manual verification flow;
- provider access/refresh token persistence, refresh или provider API calls;
- remote consent/token revocation при local disconnect;
- organizations, memberships, workspace onboarding и product dashboard;
- invitations/teams из итерации 6;
- API keys и machine authentication из итерации 7;
- Bearer issuer/handler;
- Redis, distributed job platform, KMS/Vault, Aspire, YARP и final production
  topology;
- import Prisma/Better Auth records или secrets;
- OpenSpec change/spec.

Итерация 3 завершена и предоставляет PostgreSQL, Identity, cookie sessions,
CSRF, auth capabilities, generated-client и E2E seams. API keys, organizations
и production orchestration не требуются для этого среза.

## 5. Reference correspondence

| Reference                                                                    | Новый API/данные                                                                                        | Новый UI                                          | Test/evidence                                                                              |
| ---------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------- | ------------------------------------------------- | ------------------------------------------------------------------------------------------ |
| `accounts-actions.ts`, `update-profile-action.ts`, profile schema/components | `GET /api/v1/account`, `PATCH /api/v1/account/profile`; Identity user + verified emails                 | `/user/profile`                                   | validation/Application/API/component/Playwright                                            |
| Better Auth accounts, `user-connections.tsx`, provider config                | capabilities, challenge/callback, `GET/DELETE /api/v1/account/connections/**`; Identity external logins | `/auth/login`, `/auth/error`, `/user/connections` | mocked callbacks, conflict/link/unlink integration, opt-in real authorization-screen smoke |
| `revoke-session-action.ts`, `revoke-all-sessions-action.ts`, session cards   | `GET /api/v1/account/sessions`, delete one/others; existing persistent tickets                          | `/user/security`                                  | ownership, pagination and two-browser-context tests                                        |
| `delete-account-action.ts`, danger components                                | `DELETE /api/v1/account`; transactional hard delete                                                     | `/user/danger`                                    | confirmation, cascade, cookie expiry and E2E                                               |
| `/user/page.tsx`                                                             | no business endpoint                                                                                    | redirect to `/user/profile`                       | route/component/Playwright                                                                 |
| Prisma `User`, `Account`, `Session`                                          | Identity user, verified emails, external logins, existing sessions                                      | projections only                                  | EF migration/index/cascade tests                                                           |
| Prisma `Verification` / Better Auth OAuth state                              | OpenIddict state token records                                                                          | none                                              | one-time state/replay/expiry integration tests                                             |
| Invitations and API keys account-nav entries                                 | outside scope                                                                                           | intentionally absent                              | source-boundary and route tests                                                            |

## 6. Architecture and dependencies

Dependency direction remains:

```text
Domain ← Application ← Infrastructure
                      ↖ Api composition
```

`Template.Domain` has no ASP.NET Core, EF, Identity, OpenIddict or HTTP
dependencies. `Template.Application` depends only on Domain.
`Template.Infrastructure` implements application/domain ports. `Template.Api`
owns the HTTP and security boundary and composes Infrastructure.

### Domain

Domain owns framework-neutral values and rules:

- normalized verified email;
- external provider id and stable provider subject;
- ownership/linking decisions;
- invariants for one primary email and globally unique normalized email;
- disconnect eligibility;
- session-revocation and account-deletion business outcomes.

### Application

Application owns use cases:

- get/update account profile;
- reconcile external sign-in/connect callback;
- list/disconnect connections;
- list/revoke sessions;
- delete account.

Application ports cover user/email/login/session persistence, transaction,
clock, provider-profile normalization and browser-session issuance/revocation.
They do not expose EF entities, OpenIddict tokens, ASP.NET principals or HTTP
results.

The provider boundary returns a normalized ephemeral external identity. This
is the extension point for a future provider API implementation; iteration 4
does not add an unused token vault abstraction.

### Infrastructure

Infrastructure owns:

- Identity user, custom login metadata and verified-email entities;
- EF mappings/migration and transaction implementation;
- OpenIddict Client, Web Integration and EF state-token storage;
- provider registrations and provider-specific identity normalization;
- existing PostgreSQL `ITicketStore`;
- PostgreSQL Data Protection key persistence;
- expired/redeemed OpenIddict state cleanup.

OpenIddict Client is not the user store and does not issue application bearer
tokens. After callback reconciliation, the existing browser-cookie issuer
creates or renews the same persistent session model used by local automation.
Sign-in creates a session whose authentication method is the provider. Connect
reissues the current cookie as needed for security but preserves the method
that authenticated that session; merely linking a provider does not rewrite
session history.

### Api

Api owns:

- route mapping and strict request DTO validation;
- `BrowserSession` authorization;
- CSRF, callback state validation and safe-return-url policy;
- provider/config capability projection;
- response envelopes, Problem Details, no-store headers and OpenAPI;
- rate limits, correlation, audit events and safe redirects.

### Web

Web owns routes, forms, loading/error states and presentation adapters over the
generated REST SDK. It never handles provider code/state/token, reads the
session cookie, imports Prisma/Better Auth, uses Server Actions, adds a database
client or performs raw product-data `fetch`.

## 7. REST and OAuth protocol contract

All account/auth responses use `Cache-Control: no-store`. Normal REST successes
use the established `{ "data": ... }` envelope. Failures use the established
RFC Problem Details shape with stable `code` and `traceId`.

### Auth endpoints

| Method and route                                  | Access                                                       | Success                                                               |
| ------------------------------------------------- | ------------------------------------------------------------ | --------------------------------------------------------------------- |
| `GET /api/v1/auth/capabilities`                   | anonymous                                                    | existing capabilities plus configured `{ id, displayName }` providers |
| `POST /api/v1/auth/external/{provider}/challenge` | CSRF; anonymous for `signIn`, `BrowserSession` for `connect` | `200 { data: { authorizationUrl } }`                                  |
| `GET\|POST /api/auth/callback/{provider}`         | anonymous callback for Google/GitHub/GitLab/VK               | `302` to validated UI return path or `/auth/error?code=...`           |
| `GET\|POST /api/auth/oauth2/callback/yandex`      | anonymous Yandex compatibility callback                      | same callback behavior                                                |

Challenge body:

```json
{
  "intent": "signIn",
  "returnUrl": "/dashboard"
}
```

`intent` is exactly `signIn` or `connect`. `signIn` requires an anonymous
browser; `connect` requires the current `BrowserSession`. Defaults are
`/dashboard` for sign-in and `/user/connections` for connect.

The endpoint returns an authorization URL produced through the OpenIddict
client pipeline. The UI performs top-level `window.location.assign`; provider
navigation is never attempted as an XHR redirect.

### Stable callback paths

The callback routes are protocol endpoints rather than versioned REST
resources. They intentionally preserve the reference registrations:

- Google: `/api/auth/callback/google`;
- GitHub: `/api/auth/callback/github`;
- GitLab: `/api/auth/callback/gitlab`;
- VK: `/api/auth/callback/vk`;
- Yandex: `/api/auth/oauth2/callback/yandex`.

For local development, providers continue to see
`http://localhost:3000/<callback-path>`. Next.js proxies `/api/**` to ASP.NET
Core, but never processes the callback. Production uses the same paths under
the production same-origin host.

The public origin is explicit configuration, not inferred from an untrusted
`Host`/forwarding header. The callback URI used in authorization and code
exchange is byte-for-byte identical.

### Account endpoints

| Method and route                                | Access                  | Success                                     |
| ----------------------------------------------- | ----------------------- | ------------------------------------------- |
| `GET /api/v1/account`                           | `BrowserSession`        | account/profile projection                  |
| `PATCH /api/v1/account/profile`                 | `BrowserSession` + CSRF | updated account projection                  |
| `GET /api/v1/account/connections`               | `BrowserSession`        | configured or existing provider states      |
| `DELETE /api/v1/account/connections/{provider}` | `BrowserSession` + CSRF | disconnected provider id                    |
| `GET /api/v1/account/sessions?cursor=&limit=`   | `BrowserSession`        | cursor page of active sessions              |
| `DELETE /api/v1/account/sessions/{sessionId}`   | `BrowserSession` + CSRF | revoked session id                          |
| `DELETE /api/v1/account/sessions/others`        | `BrowserSession` + CSRF | revoked count                               |
| `DELETE /api/v1/account`                        | `BrowserSession` + CSRF | deletion acknowledgement and expired cookie |

Profile update accepts only `displayName`. It is trimmed, must contain 2–50
characters after normalization and cannot contain control characters. Avatar,
emails, id and creation timestamp are read-only in this iteration.

Account projection contains:

- `id`, `displayName`, nullable HTTPS `imageUrl`, `createdAt`;
- `primaryEmail`;
- bounded `verifiedEmails`, each with `email`, `isPrimary` and provider ids
  currently vouching for it.

Connections projection contains the union of configured providers and existing
connections. This keeps an already-linked provider visible and disconnectable
after its credentials are removed from deployment configuration. Each item has
display name, configured/connected state, provider email, `connectedAt`,
nullable `lastUsedAt`, `isCurrentAuthenticationMethod`, `canConnect`,
`canDisconnect` and a stable disabled reason. A provider that is neither
configured nor connected is omitted. The projection never exposes provider
subject or tokens.

### Pagination and filtering

Connections are bounded to the five known providers and have no pagination or
filtering.

Sessions use an opaque base64url cursor over `(lastSeenAt, id)`, ordered by
`lastSeenAt DESC, id DESC`. Default limit is 20; minimum 1 and maximum 100.
Only unexpired sessions owned by the current user are returned. Response has
`items` and nullable `nextCursor`. Unknown/malformed cursors return
`400 invalid_cursor`.

Session items expose id, created/last-seen/expiry timestamps, current flag,
authentication method, nullable redacted IP and bounded user-agent string. They
never expose cookie keys, ticket hash, protected ticket or security stamps.

### Safe return URLs

`returnUrl` is optional and must normalize to a same-origin application path.
Absolute URLs, protocol-relative paths, backslashes, control characters,
encoded path confusion and paths below `/api/**` or `/auth/**` are rejected.
The existing central auth-redirect sanitizer is extended rather than
duplicated.

OpenIddict state binds provider, intent and normalized return path. A connect
state additionally binds the initiating user id and persistent session id.
Callback connect succeeds only while that same user/session is current.

## 8. External identity policy

The stable identity key is `(provider, subject)`. Email is never used as the
provider subject; it is used only for account creation, verified implicit
linking, explicit-connect conflict checks and secondary-email ownership.

| Provider | Stable subject            | Accepted email policy                                                           | Requested data                        |
| -------- | ------------------------- | ------------------------------------------------------------------------------- | ------------------------------------- |
| Google   | OIDC `sub`                | non-empty `email` with `email_verified = true`                                  | `openid profile email`                |
| GitHub   | numeric user id as string | primary email from `/user/emails` with `verified = true`                        | profile plus `user:email`             |
| GitLab   | OIDC `sub`                | non-empty `email` with `email_verified = true`                                  | `openid profile email`                |
| VK       | VK ID subject/user id     | non-empty email returned by authenticated user-info under requested email scope | minimal profile + email               |
| Yandex   | Yandex user id            | non-empty `default_email` returned under `login:email`                          | `login:email login:info login:avatar` |

VK and Yandex do not expose a separate email-specific boolean equivalent to
Google/GitLab in the reference-compatible flows. For this iteration, their
authenticated user-info email under an explicit email scope is treated as
provider-confirmed. Missing email is rejected. This deliberate mapping is
covered by provider-specific tests and durable authentication documentation.

Provider authorization code, access token, refresh token and raw profile exist
only in callback memory long enough to exchange the code and normalize the
identity. They are not logged, returned to UI, placed in cookies or persisted.

Avatar is accepted only as an absolute HTTPS URL. Invalid/non-HTTPS avatars are
discarded without failing a valid identity.

## 9. Reconciliation and business rules

Callback reconciliation runs in one database transaction:

1. Require a supported provider, stable subject and accepted provider-confirmed
   email.
2. Look up the `(provider, subject)` external login.
3. If it exists, its user remains the identity owner. A provider email now
   owned by another user causes `external_email_conflict`; it never moves the
   login. A free new verified email may replace the login's provider-email
   association and becomes a secondary email; the prior non-primary email is
   garbage-collected only if no other login vouches for it.
4. If the provider login is new, look up the normalized email across both
   primary and secondary verified emails.
5. For anonymous sign-in:
   - matching email owner: implicitly attach the provider login to that user;
   - no owner: create a user, its primary verified email and provider login;
   - uniqueness conflict: classify and retry once from a fresh read.
6. For authenticated connect:
   - email owned by another user: reject;
   - email already owned by current user: reuse that email;
   - free different email: add it as a secondary verified email;
   - attach the provider login to the current user.
7. A new login updates user display name when provider supplied a valid name
   and updates image when provider supplied a valid HTTPS avatar. Primary email
   remains unchanged.
8. Existing-login sign-in updates only the login `lastUsedAt`.
9. Commit the transaction, then issue or rotate the application browser
   session. A persistence failure cannot leave a successful cookie.

Database uniqueness is the final concurrency authority. The Application layer
maps known unique violations and performs one bounded reconciliation retry;
there is no unbounded retry loop.

An existing external login is never silently reassigned to another user. A
provider subject collision or email ownership conflict results in a stable
conflict outcome and no partial changes.

## 10. Disconnect rules

Disconnect runs transactionally and locks/rechecks relevant connections:

1. The provider connection must belong to the current user.
2. It cannot be the method that authenticated the current browser session.
3. Removing it must leave at least one usable production external login.
   Local automation password does not count as a production login method.
4. Remove the external login.
5. If its associated email is secondary and no remaining external login
   vouches for it, remove that secondary email.
6. Never remove or replace the primary email.

This preserves reference safety (`allowUnlinkingAll: false`) while adding the
agreed secondary-email lifecycle. Disconnect is local only: provider-side
consent/access is not revoked because provider tokens are not retained.

## 11. Persistence and EF migration

The migration is additive over the clean iteration-3 schema.

### `auth.user_emails`

- `id uuid` primary key;
- `user_id uuid` FK to `auth.users`, cascade delete;
- `email` bounded display value;
- `normalized_email` bounded comparison value;
- `is_primary bool`;
- `created_at timestamptz`.

Constraints/indexes:

- global unique `normalized_email`;
- one primary email per user via partial unique index;
- non-empty bounded values;
- supporting user/email lookup indexes.

The migration backfills exactly one primary email row for every existing
iteration-3 user from Identity email fields. This is a migration of the new
system's own clean-schema users, not a migration from `template/`.
`ApplicationUser.Email`/`NormalizedEmail` remain a compatibility mirror of the
primary row and are changed only in the same transaction.

### Identity external login metadata

A custom Identity login entity retains the Identity provider/key/user
semantics and adds:

- `verified_email_id` FK to `auth.user_emails`;
- `connected_at timestamptz`;
- nullable `last_used_at timestamptz`.

The provider email itself is not duplicated in the login row. Existing
Identity uniqueness on `(login_provider, provider_key)` remains, and an
additional unique index on `(user_id, login_provider)` limits a user to one
connection per known provider. Sign-in sets `last_used_at`; connect alone
leaves it null until that provider actually authenticates a session.

### Sessions

The existing persistent-session record remains the source for account session
management. The issued ticket receives a bounded authentication-method claim:
`local` or a configured provider id. Existing iteration-3 sessions without the
claim project as `local`.

The session row stores the same bounded authentication-method projection so
account-session listings never decrypt or deserialize protected tickets. The
additive column defaults existing rows to `local` and has a database check
constraint for `local` plus the five closed provider ids.

No provider token columns are added.

### OpenIddict state

OpenIddict EF tables store client state-token records required for CSRF/session
fixation and one-time replay protection. Only interactive state bookkeeping is
retained. The application does not persist remote access/refresh tokens.

A small Infrastructure hosted service periodically deletes only expired or
terminal redeemed state records in bounded batches. It does not introduce a
general job framework.

### Data Protection keys

Data Protection keys persist in a dedicated PostgreSQL EF table in the auth
schema and use a stable application discriminator.

- Test generates an ephemeral X.509 certificate and verifies encrypted-at-rest
  payload plus cross-host cookie/state compatibility.
- Development may use the persisted key ring without production certificate
  enforcement.
- Production requires certificate path and password supplied by mounted
  secret/configuration; startup fails closed if absent or invalid.

The PFX, password and private key are never stored in Git or database. KMS/Vault
is a future deployment decision, not a hidden requirement for iteration 4.

### Rollback

Rolling code back leaves additive tables/columns unused. Production schema
rollback uses restore or forward-fix; destructive generated `Down` is limited
to disposable Development/Test databases. No rollback process may touch
`template/`.

## 12. Local provider configuration

Tracked `appsettings.json` and an
`appsettings.Local.example.json` contain only documented shape/placeholders.
The real
`apps/api/src/Template.Api/appsettings.Local.json` is explicitly ignored.

To preserve the existing Development-only local automation gate,
`appsettings.Local.json` is loaded as an optional final configuration overlay
only when `ASPNETCORE_ENVIRONMENT=Development`; it does not introduce a new
security environment named `Local`. Test/Production never load it.

Configuration contains:

- explicit public origin (`http://localhost:3000` locally);
- per-provider client id and required client secret;
- production Data Protection certificate settings outside local credentials.

Values may be copied manually from `template/.env` for local smoke work. No
script, application code or committed file reads `template/.env`.

A provider is available only when its complete required option set is present.
An incomplete provider block fails options validation without logging values;
an entirely absent provider is simply omitted from capabilities. The service
can start with zero providers for build and non-OAuth tests.

## 13. Errors, authorization and observability

Stable Problem Details outcomes include:

| Status | Code                                | Condition                                                        |
| ------ | ----------------------------------- | ---------------------------------------------------------------- |
| 400    | `validation_failed`                 | strict body/profile/delete-confirmation validation               |
| 400    | `invalid_cursor`                    | malformed session cursor                                         |
| 400    | `invalid_return_url`                | unsafe OAuth return path                                         |
| 400    | `external_email_required`           | provider email missing                                           |
| 403    | `external_email_unverified`         | provider email fails verification policy                         |
| 401    | existing unauthorized code          | account/connect endpoint without session                         |
| 404    | `external_provider_not_configured`  | provider absent/incomplete                                       |
| 404    | `account_session_not_found`         | missing or foreign session, deliberately indistinguishable       |
| 404    | `external_connection_not_found`     | missing or foreign connection                                    |
| 409    | `already_authenticated`             | sign-in intent started by authenticated browser                  |
| 409    | `external_identity_conflict`        | provider key cannot be safely assigned                           |
| 409    | `external_email_conflict`           | verified email belongs to another user                           |
| 409    | `external_connection_required`      | disconnect would remove last production method or current method |
| 409    | `current_session_cannot_be_revoked` | current id passed to single revoke                               |
| 409    | `oauth_flow_context_changed`        | connect callback no longer matches initiating user/session       |

Callback failures redirect to `/auth/error?code=<stable-code>`. Raw provider
errors, descriptions, code, state, tokens, subject, email and stack trace never
appear in query parameters. The error page maps only known codes to localized
copy and has a generic fallback.

Delete confirmation compares the trimmed normalized input with the current
primary normalized email. Failure is validation, not an existence oracle.

Audit events cover:

- external challenge started;
- external sign-in succeeded/failed by stable reason;
- provider connected/disconnected;
- profile updated;
- session revoked/revoke-others;
- account deleted.

Events include correlation id, provider id where applicable, current user id
only after authentication and safe outcome code. They exclude email, provider
subject, authorization code, state, token, cookie, ticket and raw profile.
Metrics use bounded provider/outcome labels.

OAuth challenge and callback use dedicated fixed-window/concurrency rate
limits. Rate-limit behavior does not weaken state validation or reveal whether
an email exists.

## 14. Account and session transactions

Profile update is a single-user transactional update and refreshes
`UpdatedAt`.

Single-session revoke performs an ownership-qualified delete. Missing and
foreign ids share the same 404. The current session is rejected before delete.

Revoke-others deletes all unexpired and expired stored tickets for the user
except the current persistent session id in one database operation and returns
the affected count. The current cookie remains valid.

Account deletion:

1. Authorize current session and validate confirmation against primary email.
2. In one transaction delete the Identity user; cascades remove verified
   emails, external logins, Identity claims/tokens and every persistent session.
3. Commit.
4. Expire the browser session cookie.
5. Return a safe acknowledgement; UI navigates to `/`.

Organization/API-key cleanup counts are not fabricated because those domains
do not exist yet.

## 15. UI behavior

### Authentication

`/auth/login` retains the local automation panel when capability-gated and adds
one button per configured provider. A click obtains CSRF, calls the generated
challenge operation and performs top-level navigation to `authorizationUrl`.
Double submission is disabled while starting a flow.

`/auth/error` renders localized stable-code content, a retry link and a safe
home/login action. It never echoes arbitrary query text.

### Account layout

Protected `/user` redirects to `/user/profile`. The iteration-4 navigation
contains only:

- Profile;
- Connections;
- Security;
- Danger.

Invitations and API Keys remain absent until their planned iterations.

### Profile

`/user/profile` shows avatar, editable display name, read-only primary and
secondary verified emails, user id and member-since timestamp. Submit uses the
generated PATCH operation, displays field errors and updates the projection
without optimistic identity mutation.

### Connections

`/user/connections` renders providers returned by the connections API:
configured providers plus any still-connected provider whose configuration was
removed. Connected cards show provider email, connected/last-used timestamps
and current-method state. Connect is available only when configured and starts
`intent=connect`. Disconnect confirmation is disabled with the server-provided
stable reason when unsafe; the server revalidates regardless of UI state.

### Security

`/user/security` renders session cards with current marker, authentication
method, timestamps, IP/user-agent presentation and “load more” cursor paging.
The current card has no single-revoke action. “Revoke all others” preserves the
current browser.

Browser/OS labels are best-effort presentation derived from the bounded
user-agent string; authorization never depends on parsing.

### Danger

`/user/danger` requires the primary email to be typed in a confirmation dialog.
Successful deletion navigates to `/`. The UI does not assume success until the
REST operation completes.

Every route has loading, unauthenticated and expected Problem Details states.
Text follows the existing fixed deployment locale model (`en`/`ru`).

## 16. Test-first implementation order

Every behavior begins with a failing focused test, followed by the smallest
implementation and a focused green run.

1. **Domain/Application RED**
   - normalized email and primary invariants;
   - implicit link for primary/secondary email;
   - explicit same/different/free/conflicting email connect;
   - missing/unverified email rejection;
   - stable subject ownership;
   - last/current connection disconnect rules;
   - provider-secondary-email cleanup;
   - profile/session/delete outcomes.
2. **Infrastructure RED**
   - EF migration/backfill;
   - uniqueness, partial index, FK/cascade and one-provider-per-user;
   - transactional reconciliation under concurrent callbacks;
   - OpenIddict one-time state and expiry;
   - Data Protection database persistence, certificate protection and
     cross-host use;
   - no stored remote provider token.
3. **API RED with `WebApplicationFactory`**
   - capabilities and complete/incomplete provider configuration;
   - anonymous/authenticated challenge intent, CSRF and safe return paths;
   - callback success for all five normalized provider profiles;
   - state replay, wrong provider, changed connect session and provider errors;
   - account authorization, validation and no-store;
   - connection conflicts/disconnect safety;
   - session pagination, ownership, current-session and revoke-others;
   - delete confirmation, cascade and cookie expiry;
   - Problem Details, rate limits and safe audit payloads.
4. **Contract RED**
   - OpenAPI 3.1 endpoint/schema/security metadata;
   - deterministic committed contract;
   - deterministic generated TypeScript SDK;
   - callbacks covered by protocol/API tests and durable documentation, but
     deliberately excluded from the versioned REST OpenAPI/generated UI SDK.
5. **Web RED**
   - generated-client adapters only;
   - provider buttons, challenge navigation and stable error rendering;
   - profile validation/projection;
   - connection disabled reasons;
   - session cursor loading/revoke interactions;
   - danger confirmation;
   - protected redirects and loading/error boundaries;
   - source-boundary guards against Prisma, Better Auth, Server Actions, route
     handlers and raw data access.
6. **Playwright**
   - `/user` redirect and profile update;
   - connections states without invoking real callbacks;
   - two independent browser contexts, revoke one and revoke all others while
     preserving current;
   - invalid and successful account deletion;
   - opt-in, sequential five-provider smoke that asserts button availability,
     top-level navigation and provider authorization/login host or page.

Fake provider infrastructure is deterministic and owns no production shortcut.
Live smoke reads only local ignored configuration and skips with an explicit
reason when a provider is not configured. It never submits real credentials or
requires a successful callback.

## 17. Acceptance commands

Before completion, run and record at least:

```bash
dotnet restore Template.sln
dotnet build Template.sln --no-restore
dotnet test Template.sln --no-restore
dotnet build apps/api/src/Template.Api/Template.Api.csproj \
  --no-restore -p:OpenApiGenerateDocuments=true
git diff --exit-code -- contracts/openapi/v1.json
```

From `apps/web`, run the repository-defined equivalents of:

```bash
npm ci
npm audit --omit=dev
npm run api:check
npm run boundaries:check
npm run format:check
npm run lint
npm run typecheck
npm test -- --runInBand
npm run build
npm run e2e
```

Also verify:

- EF model has no pending changes and migration applies to a clean PostgreSQL
  database;
- generated OpenAPI/SDK are deterministic;
- focused provider authorization-screen smoke when local credentials are
  available;
- `git diff --check`;
- `git diff --exit-code -- template/`;
- branch-range diff for `template/` is empty.

The final `docs/aspnetcore-migration-plan.md` update records exact scope,
iteration state, acceptance evidence, command counts/results and known
differences.

## 18. Intentional differences and residual boundaries

- The reference disables implicit linking; this design enables verified-email
  implicit linking by explicit user decision.
- The new model records secondary verified emails; reference exposes only one
  user email.
- Production password lifecycle is intentionally absent despite the broad
  original migration-register wording.
- Problem Details replaces Better Auth/Server Action error shapes.
- Callback paths remain reference-compatible, but callback processing is owned
  entirely by ASP.NET Core.
- Provider tokens and remote-consent revocation are absent until a concrete
  provider API requires them.
- VK/Yandex authenticated email is treated as provider-confirmed according to
  the agreed provider-specific mapping.
- Invitations and API Keys are omitted from account navigation until
  iterations 6 and 7.
- Organization-dependent delete results do not exist in this clean schema.
- Final proxy/certificate deployment is later scope; iteration 4 establishes
  the required production Data Protection certificate contract.

There are no unresolved product decisions blocking the implementation plan.
Live successful OAuth callback verification can require provider-console
configuration outside the repository, but it does not block deterministic
callback integration tests or the agreed authorization-screen smoke.
