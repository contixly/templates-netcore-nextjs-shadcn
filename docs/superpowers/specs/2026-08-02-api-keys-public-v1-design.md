# Итерация 7: API keys и public `/api/v1`

**Дата:** 2026-08-02
**Статус:** утверждённый дизайн
**Ветка:** `codex/iteration-7-api-keys`

## 1. Цель

Перенести personal и organization API keys и поддерживаемую machine-to-machine
поверхность из immutable reference `template/` как один завершённый
вертикальный срез на целевой архитектуре:

- ASP.NET Core 10 владеет генерацией, хранением, проверкой и отзывом ключей,
  authorization, rate limits, audit и всеми `/api/**` routes;
- Next.js остаётся отдельным UI и использует только generated REST SDK;
- browser management использует существующую secure HttpOnly same-origin cookie
  и CSRF, а machine access использует только `x-api-key`;
- personal key действует как текущий владелец и теряет organization access при
  удалении membership;
- organization key действует как отдельный principal ровно одной organization и
  не наследует дальнейшие изменения роли создателя;
- secret показывается только после create/rotate и никогда не сохраняется в
  открытом виде;
- public read contract получает явные scopes, pagination, stable Problem Details
  и OpenAPI consumer documentation.

Итерация завершается schema migration, REST/OpenAPI/generated SDK, двумя
management pages, unit/integration/contract/Jest/Playwright coverage и
acceptance evidence. Она не переносит documents search или следующую предметную
область.

## 2. Изученный контекст

### Обязательные документы

- `AGENTS.md`;
- `docs/aspnetcore-migration-plan.md`;
- `docs/api-conventions.md`;
- `docs/web-conventions.md`;
- `docs/authentication-persistence-operations.md`;
- design и implementation plan итераций 5 и 6.

### Reference feature и persistence

- `template/src/features/api-keys/**`;
- `template/src/server/auth.ts` и `template/src/lib/api-key-config.ts`;
- `template/prisma/schema.prisma`, модель `ApiKey`;
- `template/prisma/migrations/20260627171629_add_api_key_table/`;
- organization access-control statements с `apiKey` management permission;
- account/organization cleanup, удаляющий принадлежащие ключи.

### Reference routes и UI

- `/user/api-keys`;
- `/w/[organizationKey]/settings/api-keys`;
- `GET /api/v1/me`;
- `GET /api/v1/organizations`;
- `GET /api/v1/organizations/{organizationId}`;
- `GET /api/v1/organizations/{organizationId}/members`;
- `GET /api/v1/organizations/{organizationId}/teams`;
- `GET /api/v1/organizations/{organizationId}/teams/{teamId}/members`.

`template/src/app/api/v1/documents-system/search/route.ts` найден, но относится
к iteration 8 и в этот срез не входит.

### Reference tests и user journeys

- `template/test/features/api-keys/**`;
- `template/test/server/auth-api-key-config.test.ts`;
- `template/test/infrastructure/prisma-api-key-schema.test.ts`;
- `template/test/infrastructure/api-keys-e2e-helper.test.ts`;
- `template/e2e/specs/api-key-management/**`;
- `template/e2e/specs/api-v1-api-key-access/api-v1-access.spec.ts`.

Reference проверяет anonymous/invalid/cookie-only denial, personal membership,
organization isolation, independent scopes, one-time reveal, edit/disable/delete,
organization permission denial и все starter read endpoints.

### Актуальные официальные источники

- [.NET 10 policy schemes](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/policyschemes?view=aspnetcore-10.0);
- [ASP.NET Core 10 authentication overview](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/?view=aspnetcore-10.0);
- [ASP.NET Core 10 rate limiting](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0);
- [.NET 10 `RandomNumberGenerator`](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.randomnumbergenerator?view=net-10.0);
- [EF Core transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions);
- установленные Next.js 16.2.11 docs о Server/Client Components, data fetching,
  mutations и data security под `apps/web/node_modules/next/dist/docs/01-app/`.

## 3. Dependency gate и scope

### Зависимости выполнены

- iteration 3 предоставляет PostgreSQL, Identity, persistent browser sessions,
  secure cookie, CSRF и local automation;
- iteration 5 предоставляет Organization, fixed roles, membership, active
  organization, paged organization/member reads и organization-aware cleanup;
- iteration 6 предоставляет teams, team membership, безопасные paged reads и
  финальный reviewed implementation state;
- Problem Details, structured audit, OpenAPI export, generated TypeScript SDK и
  browser/server API clients уже приняты.

PR #7 с iteration 6 merged. Его final head `e7bcf7cae758e7dc08ebb6aacaf25fb4a14595de`
получил automatic Codex review без major issues до merge. Поэтому dependency
gate iteration 7 выполнен.

### Входит

- personal и organization API keys;
- create, list, update, enable/disable, revoke и rotate;
- reveal-once secret handling;
- allowlisted read scopes и UI presets;
- persisted per-key fixed-window rate limits;
- custom ASP.NET Core API-key authentication scheme;
- browser-or-key policy для пересекающихся GET routes;
- public `/me`, organization detail и существующие organization/team reads;
- schema migration, cleanup cascades, audit, REST/OpenAPI/generated SDK;
- personal и organization settings pages;
- unit, PostgreSQL, API, contract, Jest и Playwright coverage;
- consumer/operator documentation и migration register/evidence.

### Не входит

- documents system/search iteration 8;
- machine write endpoints;
- Bearer/JWT/OAuth authorization server;
- arbitrary raw permission JSON или user-defined scopes;
- Redis/Valkey/distributed rate-limit cache;
- IP allowlists, mTLS, service-account lifecycle или secret vault UI;
- hard deletion/audit-retention policy for revoked keys;
- product dashboard, YARP, Docker, Aspire или production orchestration;
- data/key migration из `template/`;
- OpenSpec change/spec.

## 4. Рассмотренные архитектурные варианты

### A — общие versioned resource routes с явным credential selector — выбран

Machine calls используют reference paths. Уже существующие browser GET routes
остаются одним REST resource contract, но принимают либо browser session, либо
API key. Policy scheme выбирает API-key scheme при наличии `x-api-key`, иначе
cookie scheme. Invalid key никогда не откатывается к valid cookie.

Вариант сохраняет URL parity, existing pagination и один набор application
queries. Machine calls получают target `{ data }`/Problem Details contract, а не
legacy Better Auth error envelope.

### B — отдельный `/api/v1/machine/**`

Даёт простое разделение auth и DTO, но меняет внешние reference URLs и создаёт
дублирующую resource surface. Для функционального клона это хуже.

### C — разные response shapes на одном URL по виду credential

Позволяет дословно вернуть legacy arrays machine-клиенту, но превращает один
OpenAPI operation в неоднозначный `oneOf`, усложняет generated clients и
поддерживает две версии одной проекции. Этот вариант отклонён.

## 5. Reference correspondence

| Reference                                           | Новый API                                                      | Новый UI                                           | Acceptance tests                                                         |
| --------------------------------------------------- | -------------------------------------------------------------- | -------------------------------------------------- | ------------------------------------------------------------------------ |
| Prisma `ApiKey`, Better Auth `user-keys`/`org-keys` | `auth.api_keys`, `Template.ApiKey` scheme                      | нет                                                | EF model/migration, hash-only storage, cascade, auth concurrency         |
| create/list/update/delete actions                   | session-authenticated account/organization key management REST | shared management components                       | Domain/Application/API/Jest/Playwright                                   |
| `/user/api-keys`                                    | `/api/v1/account/api-keys/**`                                  | `/user/api-keys`                                   | auth, reveal once, update, rotate, revoke, pagination                    |
| `/w/{key}/settings/api-keys`                        | `/api/v1/organizations/{id}/api-keys/**`                       | `/w/{key}/settings/api-keys`                       | owner/admin management, member denial, tenant isolation                  |
| `GET /api/v1/me`                                    | тот же machine-only route                                      | consumer only                                      | missing/blank/invalid/cookie-only/scope/rate-limit API и E2E             |
| organization list/detail/members                    | shared target routes плюс UUID detail                          | существующий browser UI не меняет credential model | personal membership, organization principal, foreign scope, cursor tests |
| teams/team members                                  | shared target GET routes                                       | существующий teams UI                              | independent `team:read`/`teamMember:read`, team ownership, E2E           |

## 6. Layer architecture

### Domain

- `ApiKeyId` UUID value object;
- owner kind `user | organization`;
- closed built-in scope/action vocabulary;
- key-name, expiration, rate-limit and status policies;
- preset expansion as a pure deterministic policy;
- organization capability `CanManageApiKeys`: true for owner/admin, false for
  member;
- no EF, HTTP, cryptography implementation, Identity or clock dependency.

### Application

- `ApiKeyManagementService` validates commands and orchestrates personal or
  organization management;
- `IApiKeyStore` owns atomic create/list/update/revoke/rotate operations;
- `IApiKeyCredentialGenerator` returns a generated secret plus its safe persisted
  representation;
- `IApiKeyAuthenticator`/store port returns a typed machine principal and
  atomically consumes usage quota;
- `MachineOrganizationService` resolves user-vs-organization scope and calls
  explicit machine-safe read ports without fake user IDs;
- commands use typed user/organization/team/key IDs and validated relative
  durations; Infrastructure owns post-lock authoritative timestamps;
- Application never sees browser cookies or HTTP headers.

### Infrastructure

- EF entity/configuration and one additive migration in the shared
  `TemplateDbContext`;
- `EfApiKeyStore` implements owner-qualified management, role rechecks, key
  authentication and fixed-window usage;
- machine organization/team read methods reuse existing EF projections where a
  real user principal is available and use explicit organization-principal
  queries otherwise;
- no in-memory collection is the revocation or rate-limit source of truth.

### Api

- custom `Template.ApiKey` authentication handler;
- `Template.Consumer.Selector` policy scheme forwards to `Template.ApiKey` when
  `x-api-key` is present and otherwise to the primary browser cookie scheme;
- `Api.MachineKey` and `Api.BrowserOrMachine` policies;
- endpoint scope metadata plus an authorization handler;
- management and machine endpoint modules, strict DTOs, CSRF on browser
  mutations, no-store, Problem Details and bounded safe audit;
- browser management endpoints remain cookie-only and cannot be authorized by
  an API key.

### Web

- Server Components load the first management page through the generated SDK,
  forwarded cookie and SSR renewal-suppression marker;
- Client Components own dialogs, copy interaction, continuation and confirmed
  mutation reconciliation;
- every mutation uses the existing generated-SDK CSRF wrapper;
- no Prisma, Better Auth, Server Actions, raw fetch or direct database access.

## 7. PostgreSQL model

New table `auth.api_keys`:

- `id uuid` primary key, UUID v7 application/EF fallback;
- nullable `user_id uuid` FK to `auth.users` with cascade;
- nullable `organization_id uuid` FK to
  `organizations.organizations` with cascade;
- check `num_nonnulls(user_id, organization_id) = 1`;
- `name varchar(32)` with trimmed non-control content check;
- `key_hash bytea` required, exactly 32 bytes, unique;
- `key_start varchar(16)` required and safe for display;
- `scopes text[]` required, nonempty and limited to the built-in vocabulary;
- `enabled boolean`;
- `rate_limit_enabled boolean`;
- `rate_limit_window_seconds integer` in `60 | 3600 | 86400`;
- `rate_limit_max integer` in `1..1_000_000`;
- `window_started_at timestamptz`, `request_count integer`;
- nullable `last_request_at`, `expires_at`, `rotated_at`, `revoked_at`;
- `created_at`, `updated_at` timestamptz;
- list indexes `(user_id, created_at DESC, id DESC)` and
  `(organization_id, created_at DESC, id DESC)` restricted to non-revoked rows;
- unique `key_hash` is the direct authentication lookup index.

Owner kind and reference/config IDs are derived from which FK is non-null; no
unconstrained string `referenceId` is persisted. Account deletion removes
personal keys. Organization deletion removes organization keys. Deleting the
creator later does not remove an organization-owned key.

Revocation is a soft terminal state. Ordinary management lists exclude revoked
rows; no revoked-key history UI is introduced in this iteration.

## 8. Secret format and handling

`RandomNumberGenerator.GetBytes(32)` produces 256 random bits. The external
credential is base64url without padding and uses an owner prefix:

```text
user_<base64url-secret>
org_<base64url-secret>
```

Infrastructure stores only SHA-256 over the canonical complete credential and a
short safe `key_start`. With 256-bit random input the hash is an indexed lookup,
not a password-hardening substitute. No server pepper lifecycle is introduced.

Create and rotate return the raw credential once. List, update, revoke, audit,
logs and persistence never expose it. UI retains it only in component state,
clears it when the reveal dialog closes/unmounts, and never writes it to URL,
cookies, local/session storage, telemetry or error text.

OpenAPI documents reveal-once semantics but contains no realistic secret
example. Request logging never reads authorization headers.

## 9. Scopes and presets

Built-in scopes are stored canonically as `resource:action`:

| Scope               | Purpose                        |
| ------------------- | ------------------------------ |
| `basic:read`        | `/api/v1/me`                   |
| `organization:read` | organization collection/detail |
| `member:read`       | organization members           |
| `team:read`         | teams                          |
| `teamMember:read`   | team members                   |

Required sets match the reference:

- basic read: `basic:read`;
- organization read: `organization:read`;
- members: `organization:read + member:read`;
- teams: `organization:read + team:read`;
- team members: `organization:read + team:read + teamMember:read`.

UI exposes only these presets:

- `basic-read`;
- `organization-read`;
- `organization-members-read`;
- `organization-teams-read`;
- `organization-team-members-read`;
- `organization-read-all`.

At least one preset is required. The API expands presets server-side; clients do
not submit raw scopes. Extending the built-in vocabulary later requires an
intentional Domain, database-check, OpenAPI, UI-message and test change.

## 10. Authentication and authorization

### Credential selection

On mixed GET routes:

1. If the request contains the `x-api-key` header, select only
   `Template.ApiKey`.
2. Otherwise select the primary browser session cookie scheme.
3. A valid cookie never rescues a missing/blank/malformed/invalid supplied key.
4. The API-key scheme never issues a cookie or browser session.

Machine-only routes select only `Template.ApiKey`, even when the header is
absent, and therefore never authenticate, renew or delete a browser cookie.
Browser-only routes continue to select only `Template.Session` even when an
unrelated `x-api-key` header is present. Endpoint metadata/policy, not path
string matching, owns this route classification.

The header must have exactly one value, remain within the bounded credential
length and match one canonical owner-prefixed format. Missing or whitespace-only
input maps to `api_key_missing`; multiple/malformed values map to
`api_key_invalid`.

Machine-only routes require `Api.MachineKey`. Management routes retain
`Api.BrowserSession`. Mixed resource reads require `Api.BrowserOrMachine`.

### Machine principal

Authenticated claims contain only:

- key ID and safe start;
- owner kind;
- user ID or organization ID;
- canonical built-in scopes.

They contain no raw key, hash, key name, creator identity or browser-session
claims.

### Organization scope

- personal key: organization access requires a current membership for the key's
  user owner at request time;
- organization key: route organization ID must equal the key's owner FK;
- organization key behavior is independent of the creator after create;
- authorization occurs before resource lookup that could disclose a foreign
  team or organization;
- team-member reads additionally qualify the team by organization.

Organization key management requires current owner/admin membership for every
operation. Hidden navigation and capabilities are presentation only; the store
rechecks role while holding the relevant transaction locks.

## 11. Management REST contract

All management responses are no-store. Success uses `{ "data": ... }`; errors
use standard RFC Problem Details. Unsafe operations require the existing
antiforgery cookie and `X-CSRF-TOKEN`. JSON bodies are strict and reject unknown
members.

### Personal keys

| Method | Route                                        | Success                        |
| ------ | -------------------------------------------- | ------------------------------ |
| GET    | `/api/v1/account/api-keys?cursor=&limit=`    | `200 ApiKeyPageResponse`       |
| POST   | `/api/v1/account/api-keys`                   | `201 ApiKeySecretResponse`     |
| PATCH  | `/api/v1/account/api-keys/{apiKeyId}`        | `200 ApiKeyResponse`           |
| DELETE | `/api/v1/account/api-keys/{apiKeyId}`        | `200 ApiKeyRevocationResponse` |
| POST   | `/api/v1/account/api-keys/{apiKeyId}/rotate` | `200 ApiKeySecretResponse`     |

### Organization keys

The same operations exist under:

```text
/api/v1/organizations/{organizationId}/api-keys
/api/v1/organizations/{organizationId}/api-keys/{apiKeyId}
/api/v1/organizations/{organizationId}/api-keys/{apiKeyId}/rotate
```

### Requests

Create requires:

- `name`;
- nonempty `presetIds`;
- `expiresIn`: `never | 7d | 30d | 90d | 365d`;
- `rateLimitEnabled`;
- `rateLimitMax`;
- `rateLimitWindow`: `1m | 1h | 1d`.

Defaults used by UI are 30 days, enabled rate limit, 1000 requests per hour,
`basic-read` for personal keys and `organization-read-all` for organization
keys. Defaults are UI convenience; the strict API body remains explicit.

PATCH accepts any subset of name, presets, enabled, expiry and rate-limit
fields, but rejects an empty/no-op update. Supplying `expiresIn` sets expiry
relative to the successful update time; omitting it preserves the current
expiry. UI provides an explicit renewal control when the same duration should
restart from now.

DELETE and rotate require an empty body. Revoke is idempotency-safe only for the
specific confirmed request: a later repeat sees `api_key_not_found`. Rotate
keeps the logical key ID/configuration, changes hash/start atomically, resets the
active rate window, preserves historical `lastRequestAt`, and invalidates the old
credential at commit.

### Safe DTO

List/update responses may contain id, owner/config identity, name, key start,
enabled/status, scopes, rate-limit configuration/counters, last request,
expiry/rotation and created/updated timestamps. They never contain raw key or
hash. Only create/rotate response adds `key`.

## 12. Machine REST contract

| Method | Route                                                                          | Credential         | Required scopes                            |
| ------ | ------------------------------------------------------------------------------ | ------------------ | ------------------------------------------ |
| GET    | `/api/v1/me`                                                                   | API key only       | `basic:read`                               |
| GET    | `/api/v1/organizations?cursor=&limit=`                                         | browser or API key | `organization:read` for key                |
| GET    | `/api/v1/organizations/{organizationId}`                                       | API key only       | `organization:read`                        |
| GET    | `/api/v1/organizations/{organizationId}/members?cursor=&limit=`                | browser or API key | `organization:read`, `member:read` for key |
| GET    | `/api/v1/organizations/{organizationId}/teams?cursor=&limit=`                  | browser or API key | `organization:read`, `team:read` for key   |
| GET    | `/api/v1/organizations/{organizationId}/teams/{teamId}/members?cursor=&limit=` | browser or API key | organization/team/team-member read for key |

`GET /me` projects principal kind/owner, safe key identity/config and scopes. It
never returns a secret.

Collection responses intentionally retain the target opaque cursor envelope,
not the unbounded arrays from the reference. Personal-key organization list is
membership-qualified. Organization-key list contains only its owner
organization and has no continuation.

The existing organization summary projection gains an additive
`accessPrincipal` discriminator. Browser/personal calls retain the current
membership role and capabilities. Organization-key calls use
`accessPrincipal = organization`, `currentRole = organization` and all browser
mutation capabilities false; this sentinel is available only to the new machine
credential path and does not create a stored membership role.

The existing team list contains an embedded first member page. To keep
`team:read` independent from `teamMember:read`, it gains additive
`membersIncluded`. Browser calls remain `true`. Machine calls without
`teamMember:read` receive `membersIncluded = false`, an empty embedded member
page and the safe aggregate `memberCount`; the dedicated members endpoint
requires the stronger scope.

## 13. Pagination and filtering

Management list cursors are opaque, typed, versioned, checksum-protected
canonical base64url for `(createdAt DESC, apiKeyId DESC)`. Default limit is 50;
accepted range is 1..100. Invalid collection kind/version/encoding/checksum,
impossible timestamps and extra bytes fail before persistence.

Machine collection endpoints reuse their existing target cursor order and
limits. Clients return `nextCursor` verbatim and never decode or synthesize it.

No name/status/search filtering is added to API-key management in iteration 7.
Revoked keys are excluded by definition. This explicitly fixes the filtering
contract instead of adding an unbounded query surface without a reference user
journey.

## 14. Transactions, concurrency and rate limiting

### Management

- create locks/rechecks the user or organization actor boundary before insert;
- organization create/update/revoke/rotate re-read current owner/admin role
  inside the transaction;
- update/revoke/rotate lock the owner-qualified key row;
- foreign and missing IDs are coalesced;
- revoke, rotate and account/organization deletion serialize through database
  row/FK behavior;
- classified PostgreSQL serialization/deadlock failures use a bounded fresh
  transaction retry; only exhaustion becomes `concurrency_conflict`;
- validation, permission, not-found and cancellation outcomes are not retried.

Every transaction attempt samples its clock only after the listed
authorization/key locks. Create/rotate assign their timestamps there and update
converts `expiresIn` from a validated relative duration to `expiresAt` there.
Committed key, use and quota-window timestamps are monotonic lower bounds when
the system clock moves backward; rotation can never precede a persisted use.

### Authentication and quota

For a structurally valid header, one transaction locks the `key_hash` row,
samples its authoritative time after the lock on every fresh attempt, clamps it
against the committed key/window/use timeline, and:

1. verifies not revoked, enabled and unexpired;
2. resets a stale fixed window when necessary;
3. rejects an exhausted live window with `api_key_rate_limited` and computed
   `Retry-After`;
4. increments request count and updates `last_request_at` for every valid-key
   presentation, including a later scope denial;
5. commits before the resource handler runs.

This makes revocation, rotation and configured quota consistent across processes
without Redis. It intentionally serializes authentication per key; high-volume
distributed limiting is a future operational design and must be load-tested
before production tiering.

Changing rate-limit configuration or rotating resets the current window and
counter. Disabling or expiring a key maps to the same non-disclosing invalid-key
result as an unknown key.

## 15. Validation and stable failures

### Management validation

- name trims to 1..32 Unicode scalars and rejects controls;
- API key route IDs are canonical UUIDs;
- preset IDs and option enums are closed and case-sensitive;
- rate max is integer 1..1,000,000;
- at least one editable field is required by PATCH;
- organization ID comes from the route, never from a client-controlled body;
- unexpected JSON members and non-JSON bodies use existing strict boundary
  behavior.

### Machine authentication failures

| Status | Code                         | Meaning                                          |
| -----: | ---------------------------- | ------------------------------------------------ |
|    401 | `api_key_missing`            | required header absent/blank                     |
|    401 | `api_key_invalid`            | malformed, unknown, disabled, revoked or expired |
|    403 | `api_key_permission_denied`  | valid key lacks endpoint scopes                  |
|    403 | `organization_access_denied` | principal cannot target the route organization   |
|    429 | `api_key_rate_limited`       | valid key exhausted its active window            |

Rate-limit responses include a bounded integer `Retry-After`. Authorized-scope
resource misses reuse target non-disclosing organization/team not-found codes.

### Management failures

- `api_key_not_found` for missing, foreign or revoked management targets;
- `api_key_permission_denied` for an organization role without management
  capability;
- `api_key_update_unchanged` for a semantic no-op;
- `concurrency_conflict` only after bounded concurrency retry exhaustion;
- existing `validation_failed`, `antiforgery_failed`, `unauthorized`,
  `method_not_allowed` and `internal_error` rules remain.

Every error uses RFC Problem Details with stable `code`, safe `traceId`, required
fields and no backend exception detail. The target error contract intentionally
replaces reference `{ error: { code, message } }` JSON.

## 16. Cache and audit contract

Every management or API-key-authenticated response is `Cache-Control: no-store`,
including failures. Auth response middleware covers the new account,
organization and `/me` paths.

Structured management events contain only:

- closed operation `create | list | update | revoke | rotate`;
- stable outcome;
- actor user ID;
- owner kind and owner ID;
- opaque key ID when trusted;
- correlation/trace scope.

Machine events contain only closed operation/path identity, stable outcome,
trusted key ID/owner identity when authentication reached that state, and
correlation scope. They never contain raw header, key/hash/start, name, scopes,
email, IP as a metric label, cookie, request body or exception message.

No new metrics backend is introduced. Existing safe route-template request
logging remains the generic HTTP audit companion.

## 17. UI design and data flow

### Routes and navigation

- add `/user/api-keys` to account routes/navigation for every authenticated user;
- add `/w/[organizationKey]/settings/api-keys` to organization settings only
  when `CanManageApiKeys` is true;
- organization route keeps existing UUID/slug canonicalization and settings
  layout;
- member receives no navigation item and an API authorization failure if the
  route is called directly.

### Shared management surface

Both pages reuse one feature slice:

1. explanatory personal-vs-organization block;
2. paged responsive key table/list;
3. create dialog;
4. edit/enable-disable dialog;
5. rotate confirmation plus reveal-once result;
6. destructive revoke confirmation;
7. load-more continuation and localized error/retry states.

Rows show name, safe key start, active/disabled/expired status, scopes, rate
limit, expiry, last use, created date and actions. Revoked rows disappear after
confirmed success.

### Client state correctness

- server page provides only the authoritative first page;
- load-more deduplicates by key ID and rejects stale generation results;
- confirmed create/update/rotate/revoke results take precedence over older GETs;
- a failed post-mutation refresh does not replay the mutation or lose the
  one-time secret;
- retry performs only the safe GET reconciliation;
- reveal state is cleared only by explicit close/unmount, not by a background
  refresh racing the successful create/rotate response.

Accepted first-page and continuation reads form one authoritative traversal.
Rows at the same or a newer precision-preserving RFC 3339 `updatedAt` retire a
matching non-null overlay; older or malformed timestamps cannot. A revoke
overlay remains through nonterminal traversal and retires only after a terminal
accepted traversal proves the key absent from every page.

Components use existing interaction-readiness and localized Problem Details
patterns. Required shadcn primitives may be added in the target UI only; no
code is copied into or generated inside `template/`.

## 18. OpenAPI and generated client

- register `apiKeyAuth` as `type: apiKey`, `in: header`, `name: x-api-key`;
- machine-only operations advertise only `apiKeyAuth`;
- mixed GET operations advertise `cookieAuth OR apiKeyAuth`;
- management operations advertise only `cookieAuth` and their CSRF header;
- scope requirements, reveal-once descriptions, pagination limits, closed
  enums, Problem Details and `Retry-After` are exact contract assertions;
- custom operation/schema transformers remain bounded to API-key additions;
- export deterministic `contracts/openapi/v1.json` and regenerate the TypeScript
  client;
- UI uses generated request/response types and SDK functions only;
- consumer documentation includes key header usage, scope matrix, pagination,
  rate-limit/error handling, rotation and secret-storage warnings.

The same target v1 contract serves browser and machine clients. A second hand-
written public schema/client is not created.

## 19. Test-first strategy

### Domain/Application

- RED before every production behavior;
- owner/scope/preset/name/rate/expiry policies;
- cursor canonicality and collection-kind separation;
- personal vs organization management authorization;
- no-op update, rotate/revoke terminal behavior and failure mapping;
- machine organization resolution and required-scope sets.

### PostgreSQL/Infrastructure

- exact schema, named constraints/indexes and no pending EF model changes;
- only hashes/starts are persisted; raw secret scan is empty;
- user/organization cascade behavior and creator-deletion survival for org keys;
- owner-qualified list/update/revoke/rotate;
- demotion/deletion races re-authorize transactionally;
- concurrent quota boundary admits exactly the configured permits;
- rotate/revoke/use races have one serialized outcome;
- expiry/window boundary uses injected authoritative time;
- account deletion/local cleanup leaves no personal key rows.

### API and security

- create/list/update/enable-disable/revoke/rotate contracts;
- anonymous, CSRF, role, foreign key and strict-body boundaries;
- missing, blank, multiple, malformed, invalid, disabled, revoked and expired
  headers;
- valid cookie cannot satisfy machine-only routes;
- invalid supplied key cannot fall back to valid cookie on mixed routes;
- personal membership and organization-key tenant isolation;
- independent scope denial for organization/member/team/team-member reads;
- team nested-member redaction without `teamMember:read`;
- fixed-window `429` and `Retry-After`;
- no secret/header/hash in logs, Problem Details, `/me` or list DTOs;
- no-store and safe audit on success/failure/rate-limit outcomes.

### OpenAPI/contract

- exact scheme definitions and security alternatives;
- exact management/machine paths and verbs;
- request strictness, enums, limits and error unions;
- create/rotate secret present, every other DTO secret absent;
- deterministic double export and generated-client drift guard.

### Web Jest

- routes/navigation/capability visibility;
- create/update validation and preset expansion display;
- personal and organization initial loaders;
- pagination dedupe and stale completion handling;
- reveal-once secret lifetime;
- update/rotate/revoke success and failed-refresh recovery;
- no raw fetch, Server Action or handwritten contract boundary violation.

### Playwright

Port the reference scenarios:

- personal page requires authentication;
- create, reveal, use, update/disable-enable, rotate and revoke a personal key;
- owner/admin organization management and member denial;
- personal and organization keys remain separated;
- missing/blank/invalid/cookie-only `/me` denial;
- insufficient scope denial;
- personal `organization-read-all` starter reads;
- personal-key membership-loss denial is covered by the PostgreSQL/API
  integration test
  `MachineOrganizationEndpointTests.PersonalKeyReadsOnlyCurrentMembershipsWithUserAccessProjection`;
  black-box Playwright does not claim this journey because the public contract
  has no member-removal operation;
- organization key limited to its owner organization and survives creator role
  change/removal where organization lifecycle permits;
- organization/member/team/team-member pagination and safe response shapes;
- old rotated/revoked secrets stop working immediately.

## 20. Verification gates

Required .NET gates:

```bash
dotnet restore Template.sln
dotnet build Template.sln --no-restore
dotnet test Template.sln --no-restore
dotnet format Template.sln --no-restore --verify-no-changes
```

Also run:

- focused red/green test commands during implementation;
- EF pending-model check and inspected idempotent migration script;
- NuGet vulnerability scan;
- deterministic double OpenAPI export and `npm run api:check`;
- web boundaries, Prettier, ESLint, Next typegen/TypeScript, full Jest;
- clean Next.js standalone production build;
- production npm audit and documented development-only audit state;
- focused and full Playwright Chromium suites;
- `git diff --check`;
- working-tree and branch-range guards proving no `template/` change;
- guard proving no active OpenSpec artifact.

## 21. Intentional differences from reference

- ASP.NET Core custom auth replaces Better Auth API Key plugin;
- UUID/FK target ownership replaces unconstrained string `referenceId`;
- raw keys are never stored; only SHA-256 of a 256-bit random credential is
  persisted;
- revoke is soft and rotate is first-class, while reference deletes and has no
  explicit rotate operation;
- target RFC Problem Details replaces reference `{ error }` envelope;
- existing bounded opaque cursor pages replace reference unbounded arrays;
- strict header precedence prevents cookie fallback when a key was supplied;
- persisted PostgreSQL quota is authoritative across app processes;
- fixed owner/admin/member model has one `CanManageApiKeys` capability instead
  of reference custom granular organization access-control statements;
- target team response explicitly prevents nested-member leakage across scope
  boundaries.

These differences strengthen security, boundedness and target consistency while
preserving visible management journeys, principal semantics, scopes and public
resource paths.

## 22. Completion criteria

Iteration 7 is complete only when:

1. every listed REST operation has exact auth, validation, no-store, OpenAPI and
   Problem Details coverage;
2. raw credentials are reveal-once and absent from persistence/logs/lists;
3. personal membership and organization tenant isolation hold under concurrent
   role/key operations;
4. configured fixed-window limits are correct under concurrent requests;
5. personal and organization settings journeys work only through generated REST;
6. reference machine-read scenarios pass with documented target envelopes and
   pagination;
7. full .NET/web/contract/E2E gates pass;
8. `template/` is unchanged and OpenSpec remains inactive;
9. `docs/aspnetcore-migration-plan.md`, API/security operations docs and
   acceptance evidence describe the implemented result and remaining
   differences;
10. the ready PR's final implementation head has no unresolved actionable
    automatic-review comments.
