# Итерация 5: organizations, membership и onboarding

**Дата:** 2026-07-30

**Статус:** утверждённый дизайн

**Ветка:** `codex/iteration-5-organizations-membership`

## 1. Цель

Перенести core workspace behavior из immutable reference `template/` в целевую
архитектуру ASP.NET Core 10 API + отдельный Next.js UI. ASP.NET Core становится
единственным владельцем organization/membership данных, правил, authorization и
REST-контракта. Next.js реализует только UI и обращается к API через generated
SDK.

Итерация должна дать полностью проверяемый vertical slice: новый пользователь
проходит zero-organization onboarding, создаёт и выбирает workspace, открывает
slug/UUID routes, управляет организацией и встроенными membership roles в
пределах разрешений.

## 2. Изученный контекст

Перед дизайном изучены:

- `AGENTS.md`;
- `docs/aspnetcore-migration-plan.md`;
- `docs/api-conventions.md`;
- `docs/web-conventions.md`;
- `docs/authentication-persistence-operations.md`;
- design/implementation документы итераций 3 и 4;
- текущие `apps/api`, `apps/web`, `contracts/openapi` и test harnesses;
- Prisma model, organization/session integration, actions, repositories,
  permissions, validation, UI, messages, documentation, Jest и Playwright в
  `template/`.

Основные reference-файлы:

- `template/prisma/schema.prisma` — `Session.activeOrganizationId`,
  `Organization`, `Member`;
- `template/src/features/organizations/**`;
- organization-related части `template/src/features/workspaces/**`;
- `template/src/server/auth/organization-access.ts`;
- `/dashboard`, `/workspaces`, `/welcome` и
  `/w/[organizationKey]/**` routes;
- reference tests под `template/test/features/{organizations,workspaces}`;
- E2E scenarios `organization-context-routing`, `workspace-onboarding-guard`,
  `workspace-organization-management`, `workspace-page-fallback` и
  `workspace-user-management`.

`template/` остаётся read-only. Новая база и Identity store стартуют чистыми;
перенос старых данных, идентификаторов или сессий не проектируется.

## 3. Scope и dependency gate

### Входит

- Organization persistence и lifecycle;
- Membership persistence;
- закрытые роли `owner`, `admin`, `member`;
- вычисляемая permission matrix;
- active organization preference текущей persistent browser session;
- create/list/resolve/update/delete organization;
- slug или UUID как route key, slug как canonical UI key;
- allowed email-domain policy;
- zero-organization onboarding;
- member directory;
- direct add существующего пользователя по точному user ID;
- изменение встроенной membership role;
- REST/OpenAPI/generated SDK;
- `/welcome`, `/workspaces`, `/dashboard`, `/w/[organizationKey]/**`;
- en/ru UI, Jest и Playwright coverage;
- account/local-automation cleanup integration.

### Не входит

- удаление участника: reference starter surface его намеренно не предоставляет;
- Invitation, accept/reject/expiry/email delivery;
- Team, TeamMember и active team;
- organization или personal API keys и `x-api-key`;
- произвольные/custom roles;
- глобальный user directory или user search;
- полноценный product dashboard и финальная application shell;
- Redis/cache, background jobs, audit database, Aspire, YARP и deployment;
- OpenSpec change/spec.

Teams и invitations остаются итерации 6; API keys — итерации 7; dashboard/app
shell parity — итерации 9. Iteration-6 wording о role changes относится к
invitation/team collaboration lifecycle; direct membership role update входит в
итерацию 5 как часть `membership, roles/permissions, users list`.

## 4. Рассмотренные архитектурные варианты

### Вариант A — единый `TemplateDbContext` — выбран

Существующий `AuthDbContext` сначала переименовывается в `TemplateDbContext`
code-only изменением. До добавления organization model подтверждается отсутствие
EF model drift. Auth/OpenIddict/Data Protection остаются в schema `auth`, а
organization rows создаются в schema `organizations`.

Один context обеспечивает одну PostgreSQL transaction boundary для organization,
owner membership, session preference, local cleanup и hard account deletion.
Это также создаёт правильную основу для итераций 6–7.

### Вариант B — расширить `AuthDbContext` без переименования

Даёт меньший diff, но закрепляет вводящее в заблуждение имя после появления
product-domain persistence. Не выбран.

### Вариант C — отдельный `OrganizationDbContext`

Формально отделяет persistence, но усложняет cross-schema FK, active-session
preference и atomic cleanup. Потребовал бы ручной shared-transaction orchestration
между contexts. Не выбран.

## 5. Reference correspondence

| Reference                                     | Новый API                              | Новый UI                  | Проверка                                 |
| --------------------------------------------- | -------------------------------------- | ------------------------- | ---------------------------------------- |
| organization repository + load/create actions | `GET/POST /api/v1/organizations`       | `/workspaces`             | list, empty, create, slug collision      |
| active organization helpers/action            | session active-organization REST       | `/dashboard`, switcher    | active/fallback/zero-org routing         |
| `OrganizationRouteGuard`                      | resolve accessible organization by key | `/w/[organizationKey]/**` | slug/UUID, canonical redirect, isolation |
| update/delete workspace actions               | organization PATCH/DELETE              | settings workspace        | authorization, domains, confirmation     |
| member repository + direct add                | members GET/POST                       | settings users            | directory and domain acknowledgement     |
| role policy + update action                   | member PATCH                           | role selector             | assignment matrix and owner invariant    |
| onboarding guard                              | organization/session projections       | `/welcome`                | first-workspace journey                  |

Reference `/api/v1/organizations/**` route handlers use `x-api-key` and belong to
iteration 7. Their read DTO meaning informs this contract, but their authentication
mechanism is not ported now.

## 6. Layer architecture

### Domain

`Template.Domain/Organizations` owns:

- `OrganizationId` and `OrganizationMemberId` value types;
- `OrganizationSlug` normalization/validation;
- `OrganizationRole` closed values;
- `OrganizationPermissionPolicy`;
- role assignment, self-change and last-owner rules;
- allowed email-domain normalization and eligibility rules.

Domain has no EF, Identity or HTTP dependency. Organization roles are product
roles, not ASP.NET Core Identity roles and not persistent session claims.

### Application

`Template.Application/Organizations` contains:

- API-independent models and operation outcomes;
- `OrganizationService` for list/detail/create/update/delete/context;
- `OrganizationMembershipService` for list/add/change-role;
- ports such as `IOrganizationStore` and organization cleanup/session-context
  operations;
- explicit actor `UserId` and current `SessionId` inputs.

Application coordinates business rules but does not know DbContext, PostgreSQL,
HTTP status codes or Problem Details.

### Infrastructure

Infrastructure owns EF entities/configurations, PostgreSQL transactions, row
locks, bounded uniqueness retries and port implementations. Configuration is
split into focused classes rather than expanding the DbContext method into one
large organization mapper.

### Api

A dedicated organization endpoint module uses the existing authenticated
`/api/v1` route group. It owns strict request DTO validation, Problem Details
mapping, CSRF metadata, OpenAPI and safe security-event logging.

### Web

Next.js uses only generated SDK functions. Cookie-bearing SSR reads use the
existing allow-listed server client and
`X-Template-Session-Renewal: suppress`. Browser writes obtain a new CSRF token
and use a shared CSRF-first helper. Prisma, Better Auth, Server Actions, raw
fetch DTO duplication and browser token storage remain prohibited.

## 7. PostgreSQL model

### Context and schemas

- rename `AuthDbContext` and its factory/snapshot references to
  `TemplateDbContext`;
- keep EF migration history in `auth.__ef_migrations_history`;
- retain existing Identity, session, Data Protection and OpenIddict mappings;
- create product tables in schema `organizations`.

### `organizations.organizations`

- `id uuid` UUIDv7 primary key;
- `name varchar(50)`;
- `slug varchar(...)` globally unique, canonical lowercase ASCII;
- `created_at`, `updated_at` as `timestamp with time zone`;
- DB checks matching the target length/shape invariants where practical.

No generic metadata column is introduced. The only currently required metadata,
allowed email domains, receives an explicit relational model. Logo is omitted
until a concrete upload/URL lifecycle exists.

### `organizations.members`

- UUIDv7 `id` primary key;
- `organization_id` FK, cascade delete;
- `user_id` FK to `auth.users`, cascade delete;
- `role` closed check `owner|admin|member`;
- `joined_at`, `updated_at`;
- unique `(organization_id, user_id)`;
- indexes `(user_id, organization_id)`, `(user_id, joined_at, id)`, and
  `(organization_id, joined_at, id)`.

### `organizations.allowed_email_domains`

- `organization_id` FK, cascade delete;
- normalized exact `domain`;
- composite primary key `(organization_id, domain)`;
- deterministic ordering by domain in projections.

### `auth.sessions`

Add nullable `active_organization_id` with an index and FK to organization
`ON DELETE SET NULL`. It is a relational preference for one persistent session,
not part of the protected ticket or principal claims. Ticket renew updates must
not overwrite it.

## 8. Identifiers, validation and canonical routing

### Organization ID and key

New IDs are UUIDv7. `organizationKey` accepts either canonical UUID text or a
non-UUID-shaped slug. The namespaces are disjoint: UUID keys resolve only by id,
while other keys resolve only by slug. API detail resolution returns
`canonicalKey`, which is the slug. UI links prefer slug; UUID routes remain valid
and redirect to canonical slug routes where the reference does so.

All iteration-5 organization/member UUID route segments require a parsed
`D` rendering equal to the original route text with ordinal-ignore-case
semantics. This preserves published upper/lower or mixed hex casing but rejects
leading, trailing or wrapped whitespace and every other normalized spelling.
The check happens after actor resolution inside the existing operation audit;
invalid organization/member ids retain `400 validation_failed`, omit the
invalid opaque id and raw/encoded text from the exactly-once audit/log surface,
and never reach Application or persistence. The detail key uses the same
canonical comparison but retains its non-disclosing `404
organization_not_found`. No other iteration-5 organization route UUID parser is
outside these validators; typed request-body UUIDs remain unchanged.

A deep link selects its URL organization for the request but never changes the
session preference. Only an explicit switch mutation updates active context.

### Name

- trim outer whitespace;
- non-empty, maximum 50 UTF-16 code units to preserve browser/.NET parity;
- Unicode letters and digits plus ordinary spaces, `-` and `_`;
- controls, newline-style whitespace and unsupported punctuation are rejected.

This intentionally tightens the reference regex, which admits control
whitespace through `\s`.

Organization names obey a per-user accessible-name graph invariant: for every
user, no two organizations in that user's committed accessible set may have
equal PostgreSQL `lower(name)` values. This is not global name uniqueness;
organizations with disjoint member sets may retain case-insensitively equal
names. Create checks the actor because it adds only that actor, a name-changing
update checks every current member affected by the rename, and add-member checks
the target user's other organizations before adding the new edge.

All three paths take the same transaction-scoped, two-key PostgreSQL advisory
lock before their exact conflict query. The first key is reserved for
organization-name decisions; the second is
`hashtext(lower(candidateName))`, using the exact PostgreSQL normalization that
the queries compare. A hash collision can only serialize unrelated names: the
exact query remains authoritative and therefore cannot reject globally
duplicated names belonging to disjoint member graphs. Each operation takes at
most one name lock after its authorization/user row locks, always in the same
order, and the lock follows transaction commit, rollback, timeout, and
cancellation automatically. This deliberately strengthens the immutable
reference, whose create/update checks are actor-scoped and whose add-member path
can admit indistinguishable switcher names.

### Slug

PATCH slugs trim and lowercase, then require
`^[a-z0-9]+(?:-[a-z0-9]+)*$` and reject UUID-shaped normalized values. Generated
slugs strip unsupported characters, collapse separators, use a 48-character
base, fall back to `workspace`, prefix a UUID-shaped base with `workspace-`, and
preserve the readable `base`, `base-2`, …, `base-5` candidates. When all five
already exist, create uses a collision-resistant lowercase 32-hex organization-ID
suffix and truncates the base as needed to keep the complete slug at most 64
characters. Existing candidates do not consume the separate five-attempt budget
for global-unique-index races.

### Allowed domains

Each value trims, lowercases, removes at most one leading `@`, validates a DNS-
like exact domain of at most 253 characters and de-duplicates. Empty collection
disables the policy. Subdomains do not match unless listed explicitly.
Changing policy never removes existing members; projections mark out-of-policy
members. Organization PATCH accepts at most 100 raw `allowedEmailDomains` array
items, inclusive, and checks that request-resource bound before normalization so
duplicates cannot bypass it.

## 9. Roles and permissions

| Capability                            | member | admin | owner |
| ------------------------------------- | ------ | ----- | ----- |
| Read safe organization/member context | yes    | yes   | yes   |
| Update organization/settings          | no     | yes   | yes   |
| Direct-add member                     | no     | yes   | yes   |
| Assign `member` or `admin`            | no     | yes   | yes   |
| Assign or mutate `owner`              | no     | no    | yes   |
| Delete organization                   | no     | no    | yes   |

Additional invariants:

- no self-role update;
- unknown/multiple roles cannot exist in the clean target schema;
- unchanged role returns conflict;
- every organization always retains at least one owner;
- permissions are recomputed from current membership for each operation;
- UI capabilities are presentation hints, never authorization authority.

## 10. REST contract

All success bodies use `{ "data": ... }`; failures use the established RFC
Problem Details contract. All routes require `Api.BrowserSession`. Every unsafe
route requires `X-CSRF-TOKEN`. Responses are `Cache-Control: no-store`.

### Organization endpoints

| Method | Route                                            | Request                       | Success                    |
| ------ | ------------------------------------------------ | ----------------------------- | -------------------------- |
| GET    | `/api/v1/organizations?cursor=&limit=`           | query                         | `200 OrganizationPage`     |
| POST   | `/api/v1/organizations`                          | `{ name }`                    | `201 OrganizationDetail`   |
| GET    | `/api/v1/organizations/by-key/{organizationKey}` | path                          | `200 OrganizationDetail`   |
| PATCH  | `/api/v1/organizations/{organizationId}`         | strict non-empty partial body | `200 OrganizationDetail`   |
| DELETE | `/api/v1/organizations/{organizationId}`         | `{ confirmationName }`        | `200 OrganizationDeletion` |
| PUT    | `/api/v1/auth/session/active-organization`       | `{ organizationId }`          | `200 ActiveOrganization`   |

`GET /api/v1/auth/session` adds nullable `activeOrganizationId` only in the
authenticated session projection.

### Membership endpoints

| Method | Route                                                           | Request                                           | Success                      |
| ------ | --------------------------------------------------------------- | ------------------------------------------------- | ---------------------------- |
| GET    | `/api/v1/organizations/{organizationId}/members?cursor=&limit=` | query                                             | `200 OrganizationMemberPage` |
| POST   | `/api/v1/organizations/{organizationId}/members`                | `{ userId, role, acknowledgeDomainRestriction? }` | `201 OrganizationMember`     |
| PATCH  | `/api/v1/organizations/{organizationId}/members/{memberId}`     | `{ role }`                                        | `200 OrganizationMember`     |

There is no member DELETE endpoint.

### Projections

Organization summary includes `id`, `name`, `slug`, `canonicalKey`, timestamps,
`currentRole` and closed server-computed capabilities. Detail additionally
includes ordered `allowedEmailDomains`.

Member includes `id`, `userId`, safe account name/email/image, one role,
`joinedAt`, `emailDomain` and `isOutsideAllowedEmailDomains`.

## 11. Pagination and filtering

Both collections use opaque versioned base64url cursors with checksum validation,
following the existing session-cursor discipline. Clients return cursors
verbatim and never decode or synthesize them.

- organizations: the actor's immutable membership edge
  `(membership.joinedAt ASC, membership.id ASC)`;
- members: `(member.joinedAt ASC, member.id ASC)`;
- default limit 50;
- accepted range 1–100;
- canonical cursor corruption returns `400 invalid_cursor`;
- the immutable organization-list timestamp payload uses a cursor kind distinct
  from both the legacy mutable-name layout and the member-list layout, even
  though the two current positions contain UTC ticks plus a membership UUID;
  legacy layouts, wrong kind/version, noncanonical base64url, corrupt checksum,
  out-of-range ticks, non-UTC encode inputs, and extra bytes are rejected before
  persistence;
- no free-text search, role filter or global candidate listing in iteration 5.

The UI renders the first page and explicit continuation/load-more behavior.
Active organization resolution and current actor context never depend on an item
being present in the first collection page.

This is an intentional improvement over reference unbounded lists.

## 12. Authorization, errors and disclosure

Missing and foreign organizations share `404 organization_not_found`, preventing
existence disclosure. The web route can still reproduce the visible reference
state: zero accessible organizations render onboarding; otherwise an unresolved
`/w/{key}` renders the protected 403 page.

Stable organization codes include:

- `organization_not_found` — 404;
- `organization_permission_denied` — 403;
- `organization_name_conflict` — 409;
- `organization_slug_conflict` — 409;
- `last_organization_required` — 409;
- `organization_confirmation_mismatch` — 400;
- `member_not_found`, `target_user_not_found` — 404;
- `member_already_exists`, `member_role_unchanged` — 409;
- `role_assignment_forbidden` — 403;
- `member_domain_acknowledgement_required` — 409;
- `organization_ownership_transfer_required` — 409;
- existing `invalid_cursor` and `concurrency_conflict`.

The domain-acknowledgement problem may include target email, normalized email
domain and ordered allowed domains because the actor already holds member-create
permission and supplied the exact target ID. The UI asks for explicit confirmation
and repeats the POST with acknowledgement; the first request performs no write.
`emailDomain` is explicitly nullable when the verified email suffix is not a
DNS-like domain. That valid null keeps the acknowledgement flow available and
the UI renders fixed localized unknown-domain copy.

Logs record operation, outcome, actor user/session ID, organization/member opaque
IDs and trace ID. They never include names, emails, domains, bodies, cookies,
credentials or cursor values.

After an organization endpoint resolves the authenticated actor, its HTTP
boundary parsing and validation execute inside one API-level audit boundary.
`ApiValidationException` is audited as `validation_failed`; the stable
`invalid_request` raised by manual JSON reading is audited with that same code.
The original exception is rethrown unchanged, so status, Problem Details,
authorization/validation precedence, no-store behavior and CSRF semantics do
not change. The boundary audit may project only non-empty canonical `D` UUIDs
from organization/member route segments. Invalid route text and every request
body/query value remain excluded, including names, slugs, emails, domains,
target-user IDs, confirmation values and cursors. Application/Domain calls sit
outside this boundary and retain their single `RequireSuccess` audit, preventing
double audit for successful and business-failure outcomes. Authentication and
antiforgery failures that happen before actor resolution are not represented as
organization operation attempts.

Organization detail keys and list limits are intentionally accepted as raw HTTP
text only so the framework cannot reject them before the actor-aware audit
boundary. After actor resolution, detail accepts only a canonical `D` UUID or
canonical `OrganizationSlug`. A parsed UUID must also equal its `D` rendering
with ordinal-ignore-case comparison, preserving the published upper/lower hex
casing while rejecting surrounding whitespace and every normalized spelling;
all other text maps to the existing
non-disclosing `404 organization_not_found`, emits exactly one safe
`organization_get` audit, and never reaches Application or persistence. Both
list endpoints parse `limit` inside that same boundary with invariant integer
semantics, default `50`, and range `1..100`. Malformed, overflowing, zero and
over-limit values are `400 validation_failed`, are never logged, and never
reach Application or persistence. Despite raw internal binding, OpenAPI keeps
the public optional `integer`/`int32` pagination contract with minimum `1`,
maximum `100`, and default `50`.

## 13. Transactions and concurrency

### Create

One transaction locks the actor user as the per-actor create serialization point,
checks case-insensitive accessible-name duplication, chooses/retries the slug,
creates Organization, creates owner membership and updates the current session
active organization. Failure rolls everything back.

### Set active

One membership-qualified update changes only the current unexpired session.
It does not take an exclusive organization `FOR UPDATE` lock, so independent
selectors and nonmembers do not serialize through organization mutations.
Foreign/non-member organization produces the non-disclosing not-found result.
If organization deletion wins after the statement snapshot, only PostgreSQL
`23503` for `fk_sessions_organizations_active_organization_id` (including an EF
wrapper) maps to that same not-found result; other FK defects are not swallowed
and serialization/deadlock mapping is unchanged.

### Organization detail

Accessible organization row/role and ordered allowed domains are read without
exclusive locks from one repeatable-read snapshot. Concurrent detail reads
progress together, and a concurrent update yields a wholly pre-update or
post-update projection rather than mixed identity/domain state. Existing
transaction nesting is reused rather than opening a nested transaction.
Serialization/deadlock exhaustion maps through `ConcurrencyConflict` to the
published `409 concurrency_conflict`; the exact OpenAPI response set and
generated `GetOrganizationByKeyErrors` union include that runtime outcome.

### List members

The read path does not acquire organization or membership `FOR UPDATE` locks.
Authorization, allowed domains, and the stable paged projection are read from
one PostgreSQL repeatable-read snapshot. Concurrent GETs for the same
organization therefore progress together; a concurrent organization/access
deletion yields the authorized snapshot or the same non-disclosing not-found
result without weakening any mutation lock.

### Update

Lock organization and actor membership, re-evaluate permission, validate any
name/slug conflict, replace allowed-domain rows and update the organization in
one transaction. For a name change, acquire the proposed-name advisory key and
reject when any current member can access another organization with an equal
PostgreSQL-lowered name; the current organization is excluded so a case-only
rename remains valid. The global unique index is authoritative for slug races.

### Delete

Lock organization, actor membership and the actor's accessible membership set;
require owner permission, exact case-sensitive name confirmation and more than
one accessible organization. Delete cascades members/domains and clears active
session FKs through `SET NULL`. Dashboard fallback remains deterministic.

### Add member

Lock/recheck actor role, then lock the target user and check target existence
and exact current membership. Acquire the locked organization's current-name
advisory key and reject when the target can access another organization with an
equal PostgreSQL-lowered name. This target-name conflict reuses
`member_already_exists`, carries no acknowledgement metadata, and is checked
before domain policy to avoid disclosing the target's unrelated organization
graph. The warning response writes nothing. An acknowledged request inserts
exactly one membership; the unique key maps a race to
`member_already_exists`.

### Change role

Lock actor, target and the organization owner set. Recompute the assignment
matrix, block self edits, redundant changes and invalid owner mutation, preserve
at least one owner, then write atomically.

## 14. Account deletion and local cleanup

Iteration-4 hard account deletion and local automation cleanup must become
organization-aware:

- organizations in which the user is the only member are deleted;
- membership is removed when another owner remains;
- deletion is rejected with `organization_ownership_transfer_required` when the
  user is sole owner of a multi-member organization;
- the account-deletion dialog maps only that exact code to localized
  promote/share-ownership guidance and keeps all other failures generic and safe;
- session active-organization references are cleared when access disappears;
- local cleanup returns the real count of deleted sole-member organizations;
- account deletion, organization cleanup and user deletion share one transaction.

Membership mutations and user lifecycle cleanup use one canonical lock order:
affected organization rows are locked by ascending ID before user and ordered
membership/owner rows. Lifecycle discovery is rechecked after those locks; a
changed organization-membership set rolls back and retries the whole transaction
up to the bounded application limit, then returns the stable concurrency
conflict rather than leaking a database deadlock.

This intentionally closes the reference orphan-owner gap and prevents product
data from surviving local E2E cleanup.

## 15. Web behavior

### `/welcome`

A protected zero-organization onboarding surface offers creation and account
settings. The reference `Review Invitations` action is omitted until iteration 6
rather than linking to a dead route. A user who already has an accessible
organization is redirected through `/dashboard` instead of seeing first-workspace
onboarding again.

### `/workspaces`

Shows accessible organization cards, create action, paging continuation and safe
loading/error/empty states. Cards link to canonical workspace and settings
routes. Delete is shown only for an owner with more than one accessible
organization.

The shared create dialog uses separate permanent-attachment and Activity
visibility lifecycles. Actual deletion makes a post-transport completion fully
inert. An attached Activity-hidden completion settles local pending/request
state but never performs stale global navigation; hidden success closes the
dialog and queues only one origin-surface refresh, drained exactly once on
reveal. A visible current completion retains canonical push plus refresh, and
StrictMode replay does not invalidate the live instance.

### `/dashboard`

SSR resolves the active organization when still accessible, otherwise the first
organization in deterministic list order, otherwise redirects to `/welcome`.

### `/w/[organizationKey]`

The root validates REST access and redirects to canonical
`/w/{slug}/dashboard`. A UUID-compatible link remains valid. Deep-link rendering
does not update active session context. A successful direct
`/w/{nonCanonicalKey}/dashboard` lookup applies the same canonical redirect
before rendering.

The organization dashboard is a minimal organization-aware context page; charts,
data table and final shell remain iteration 9.

The route-owned parallel switcher has three independent client lifetimes:
insertion-effect attachment for permanent existence, layout-effect visibility
for React Activity, and an incrementing committed-pathname generation. Each
set-active attempt captures its exact origin. Permanent deletion stops the
continuation before ref, state, or router effects. Activity-hidden completion
may settle live local failure/success state, but successful global work is
reduced to one queued refresh for the same still-current generation; reveal
never replays the stale push, drains the refresh once, and repeat hide/reveal
does not repeat it. Any pathname transition permanently invalidates the old
attempt even if the slot survives, temporarily returns `null`, or later returns
to the same pathname. Visible current-generation success preserves the approved
suffix-aware canonical push plus refresh and the active-id no-op.
Queue invalidation and reveal-time origin-generation comparison intentionally
remain independent defenses: a hidden A queue is discarded by committed A→B→A
pathname updates and cannot drain even if one protection later regresses.

### Settings

- settings root redirects to `/settings/workspace`;
- workspace page edits name, slug and domains when permitted, otherwise renders
  read-only values;
- client validation rejects an exact normalized D-format UUID-shaped slug before
  transport, while preserving other canonical hexadecimal/hyphen slugs;
- workspace PATCH sends only normalized fields that differ from the latest
  confirmed detail response; an unchanged form is a disabled no-op, and every
  successful response replaces the comparison baseline;
- the settings client applies the inclusive 100-domain maximum after
  normalization and de-duplication; 101 distinct domains render a localized
  field error before transport, while exactly 100 distinct generated-array
  items remain valid even when they came from more than 100 raw tokens;
- a mounted form takes update permission from the latest incoming RSC projection,
  so demotion revokes controls and submit immediately while local dirty values
  and the latest mutation-confirmed field baseline remain intact;
- the server/client settings-form boundary is keyed by the resolved immutable
  organization id, not slug/pathname: an RSC projection for a different id
  synchronously remounts local baseline, inputs, feedback and pending identity,
  while a same-id projection preserves mounted dirty/confirmed state and applies
  the latest capability;
- every stateful organization-owned client boundary rendered by this settings
  server-route family uses the resolved immutable organization id as its React
  identity whenever retained state can coordinate reads or mutations. A mutable,
  releasable slug/pathname is navigation input, never a client-state identity;
  same-id RSC refreshes preserve the established local semantics instead of
  remounting merely because serialized props changed;
- the users page keys `OrganizationMemberDirectory` by that id. A different-id
  projection remounts reducer pages/tails, feedback, refresh recovery, confirmed
  overlays and the nested direct-add domain acknowledgement; the existing
  directory unmount cleanup aborts and supersedes its active GET before the new
  instance can issue organization-B transport. A same-id projection continues
  to reconcile its authoritative first page while preserving loaded progress,
  overlays and active-read coordination;
- the directly analogous `OrganizationDeleteDialog` on workspace settings is
  keyed by the same id, so confirmation text and pending destructive identity
  cannot be retained when one pathname is re-resolved from organization A to B.
  The settings form was already keyed; the fixed roles page and settings
  navigation own no analogous organization mutation/read state;
- key replacement also invalidates asynchronous continuations, not only rendered
  state. The directory, add-member control, role control and delete dialog each
  own a permanent-attachment marker whose insertion-effect cleanup runs for
  actual keyed deletion but not temporary React Activity hiding. Add/role leaves
  guard only their own post-await state/ref writes: a successful authorized
  mutation still reaches the captured directory confirmation when a same-id
  capability refresh removes that leaf. The directory's attachment is the
  immutable-identity authority that either publishes the confirmed overlay and
  canonical GET or makes a different-id completion inert. Delete checks its own
  attachment before `onDeleted` and again after that awaited callback before
  router replacement/refresh;
- member-directory read detachment is one idempotent operation used by both
  passive Activity cleanup and insertion keyed-deletion cleanup. Hiding aborts
  the read active at hide time and dispatches a generation-matched cancellation
  that clears reducer `activeRead` without failure feedback or disturbing a
  newer read. Reveal therefore restores a usable load-more or GET-only recovery
  control. The directory attachment remains live, so a valid hidden same-id
  mutation may still confirm and start recovery. If that hidden recovery read
  is followed by keyed A→B deletion, insertion cleanup aborts it and resolves
  its superseded race even though passive cleanup already ran; actual deletion
  dispatches no stale reducer work. Visible, same-id and Activity-preserved
  completion semantics remain active without allowing an old organization read
  to outlive deletion;
- each form instance owns separate attachment and visibility lifecycles. An
  insertion-effect cleanup invalidates the attachment marker during actual
  keyed deletion before replacement layout work, and every completed mutation
  checks it immediately after transport, before ref/state writes or router
  replace/refresh. React Activity hiding retains that lifetime while cleaning
  layout effects and detaching host refs: an attached hidden completion clears
  its lock/pending state and reconciles failure or the confirmed success
  baseline, queues global router effects, and layout setup flushes the queue
  exactly once on reveal. Visible and StrictMode-replayed instances keep normal
  completion behavior;
- the workspace delete dialog owns the same distinct lifetimes. An attached
  hidden success settles its request lock/local state and invokes a live
  `onDeleted`, but queues the now-required `/workspaces` replace plus refresh
  instead of executing global router work. Reveal drains that queue exactly
  once, including after repeat hide/reveal; actual insertion deletion clears it.
  Hidden failure stays locally retryable without navigation. Visible and
  StrictMode success remains immediate, and the immutable-id key continues to
  suppress every different-organization late completion;
- canonical URL is replaced after slug change;
- danger control requires owner/delete capability and another accessible org;
- users page separates the current actor, pages other members, exposes direct
  add/role controls only when allowed and never renders member removal;
- roles page documents the three fixed roles without custom-role mutation;
- invitations, teams and API keys are absent from navigation.

### Switching and mutation recovery

A minimal workspace switcher explicitly updates session preference, then
preserves known single-key routes such as settings users/workspace/roles.
Unknown/complex workspace paths fall back to the selected organization dashboard.
Selecting the already-routed workspace skips that mutation only when its id also
matches the persistent session preference. Thus an explicit selection after a
deep link persists the routed workspace for a later `/dashboard`.

The shared authenticated site-header/account-navigation guard mounts exactly one
browser `getAuthSession` refresh after a complete authenticated projection.
This unmarked call owns sliding renewal for `/welcome`, `/workspaces`, `/w/**`,
and `/user/**`, then refreshes the server projection once; page-local dashboard
refresh mounts are prohibited. The transient `/dashboard` resolver defers to
its final protected destination. Document-local pathname-cycle state coalesces
concurrent mounts plus the successful refresh/remount for the current path,
starts another unmarked renewal after a soft navigation to a different
protected pathname, and releases a failed cycle for later retry. A stale
request cannot clear or refresh the newer pathname cycle. Anonymous, malformed,
and API-failure projections mount no renewal.

Protected organization pages redirect only an explicit anonymous session to a
route-specific login URL. Transport, configuration and malformed-projection
failures remain safe availability states. Workspace detail success is
authoritative over the independent organization-list read; the list is awaited
only when a missing detail must be distinguished between zero-organization
onboarding and forbidden access. Next.js `forbidden()` is backed by the
version-required `experimental.authInterrupts` configuration.

The site header receives a route-owned Next.js parallel slot. Every current site
route has explicit slot coverage and `default.tsx` provides the hard-navigation
fallback: `/w/{key}` and its dashboard render the server-resolved switcher,
while every non-workspace page renders `null`. The slot therefore participates
in initial server HTML and is atomically replaced during soft navigation rather
than reverse-registering state through a client effect. It projects only
`id`/`name`/`canonicalKey`, includes the resolved current detail even when it is
beyond the first 50 list items, and shares request-time session/list/detail
reads through React request memoization.

`/workspaces` server-renders only the authoritative first page at its canonical
URL. Every explicit continuation is an unmarked same-origin browser call to the
generated organizations GET operation with the last opaque cursor; the URL does
not advance or accumulate cursor state. Old `?cursor=` bookmarks redirect to the
canonical route and therefore restart from page one. Client state appends in API
order, de-duplicates ids, supports an arbitrary practical number of sequential
clicks, and keeps its one cookie jar. It records continuation-row ids as page
provenance. An authoritative first-page refresh replaces every former
first-page row, retaining only still-known continuation rows plus confirmed
local mutation overlays; a continuation row adopted by a later first page loses
tail provenance. A delayed continuation cannot restore provenance for an id in
the currently committed authoritative first page, so reconciliation remains
deterministic whichever reducer action lands first. A failed continuation does
not discard successful pages or advance the cursor: stable localized failure
copy and the same ready retry remain available.

Mutation response DTOs are authoritative. If a follow-up refresh fails after a
successful write, the UI does not report the mutation as failed and never repeats
it. It retains a conservative confirmed projection and offers a separate
refresh retry.

The settings parent shell never owns anonymous navigation because doing so can
race the segment-specific return URL; Workspace, Users, and Roles each own their
exact protected redirect. Member directory client state keeps ordered server
pages separate from confirmed mutation overlays. A monotonically increasing
read generation makes overlays causal: a read begun before a mutation cannot
retire its projection, while the first successful later read whose page contains
that member replaces it with authoritative server state. Added-member overlays
therefore remain only while successful later pages do not contain that member.
An abort controller plus an explicit supersede settlement releases the older
mutation callback even if its transport never settles. A first-page refresh
preserves already loaded tail pages and their opaque last cursor, and a recovery
action performs GET only. A later RSC member page immediately replaces only
server page zero and is then committed through the reducer; loaded tail pages
and their last opaque cursor, confirmed overlays/order, active read coordination
and generation, feedback, and GET-only recovery remain in force. Because the RSC
page has unknown mutation causality, it never retires a confirmed overlay; only
the existing successful generated read that began after the mutation may do so.
The workspace list similarly tombstones a confirmed deletion so accumulated or
refreshed props cannot resurrect it and delete eligibility is recomputed
immediately. Refreshed/generated incoming list entries replace older duplicates
by id—including name, slug, role, and capabilities—while confirmed deletion
tombstones and locally accumulated tail entries absent from the refreshed first
page remain in force. Current-actor domain eligibility mirrors the domain
policy's exact email/domain syntax but serializes only the resulting
outside-policy boolean. A direct-add domain override is offered only for exact
HTTP 409
`member_domain_acknowledgement_required` responses carrying nonempty email, an
explicit nullable email-domain field, and a nonempty allowed-domain list.
Explicit null renders localized unknown-domain copy; omitted, blank, or
wrong-typed metadata fails closed.

## 16. Test-first implementation order

1. Domain tests for slug/domain/role/permission/last-owner rules.
2. Application tests for organization lifecycle, context and membership.
3. DbContext code-only rename with no EF model drift.
4. EF entities/configurations, additive migration and PostgreSQL persistence
   tests.
5. Organization-aware account/local cleanup tests and implementation.
6. API boundary tests, then endpoint implementation.
7. OpenAPI tests/export and generated SDK regeneration.
8. Web adapters/components/routes with Jest tests first.
9. Multi-user deterministic Playwright scenarios.
10. Durable docs and full acceptance evidence.

Focused tests are run red before production code and green after each slice.

## 17. Test matrix

### Domain/Application

- role matrix and assignability;
- self edit, owner edit and last-owner invariant;
- name/slug/domain normalization;
- deterministic cursor rules;
- create/update/delete/switch/add/change-role outcomes;
- account deletion ownership classification.

### Infrastructure

- clean migration shape, constraints, indexes and FKs;
- atomic create + owner + active session;
- tenant-qualified reads;
- cascade and `SET NULL` behavior;
- slug collision, duplicate member and role/delete races;
- per-user accessible-name graph checks for sequential add, other-admin rename,
  case-only self-exclusion, and domain-warning precedence;
- same-name create/update/add races, including add/add and other-admin
  rename/add interleavings plus target create/add user-before-name lock order
  and post-wait visibility, while disjoint member graphs may retain duplicate
  names;
- sixth-and-later shared generated-slug collisions, including non-ASCII names;
- account/local cleanup transaction and count.

### API

- 401, CSRF, strict JSON and validation;
- no-store and secure error envelope;
- missing/foreign indistinguishability;
- permission matrix and stable problem codes;
- cursor range/corruption;
- raw 100-item allowed-domain acceptance, 101-item rejection, and OpenAPI
  `maxItems`;
- OpenAPI security, headers, enums, bounds and strict schemas.

### Web/Jest

- server loader allow-list and renewal suppression;
- zero-org, list/loading/error and canonical routing;
- form validation, exact normalized-domain 100 acceptance/101 rejection, and
  domain acknowledgement;
- role-aware controls and safe partial-success recovery;
- switch navigation behavior;
- en/ru copy.

### Playwright

- zero-org `/dashboard` to `/welcome` while `/workspaces` and `/user/*` remain
  reachable;
- first organization creation and `-2` slug collision;
- active/fallback routing, slug/UUID access and foreign isolation;
- owner settings update/domain normalization and member read-only behavior;
- last accessible organization delete protection;
- direct add with domain acknowledgement;
- owner role change and member control absence;
- route-preserving explicit workspace switch;
- organization-aware local cleanup.

## 18. Acceptance commands

Required completion gates include:

```bash
dotnet restore Template.sln
dotnet build Template.sln --no-restore
dotnet test Template.sln --no-restore
dotnet format Template.sln --no-restore --verify-no-changes
```

Also required:

- clean PostgreSQL migration apply;
- EF `has-pending-model-changes`;
- non-empty idempotent migration script inspection;
- exact OpenAPI export twice, stable hash and committed-contract diff;
- `cd apps/web && npm run api:check`;
- `npm run boundaries:check`;
- `npm run format:check`, `npm run lint`, `npm run typecheck`;
- full Jest;
- clean production Next.js build and standalone artifact check;
- deterministic Playwright E2E;
- production npm and full NuGet vulnerability checks;
- `git diff --check`;
- empty working-tree and branch-range diffs for `template/`.

Before Next.js edits, implementation reads the installed Next.js documentation.
Version-sensitive .NET/EF/Next.js decisions are checked against installed or
current official documentation rather than memory.

## 19. Durable documentation

The implementation updates:

- `docs/api-conventions.md` — organization REST, authorization, active context,
  errors and cursor contract;
- `docs/web-conventions.md` — routing, SSR loading, switcher and mutation recovery;
- `docs/authentication-persistence-operations.md` — renamed DbContext, migration
  commands, schemas and cleanup behavior;
- `docs/aspnetcore-migration-plan.md` — iteration scope/status, correspondence,
  evidence, intentional differences and next gate.

No durable architecture/security/migration decision remains only in chat or PR
comments.

## 20. Intentional differences from reference

- single-role checked schema replaces CSV-compatible role parsing;
- active organization has a real FK and transactional create/context update;
- missing/foreign API resources share non-disclosing 404;
- last-organization state conflict uses 409 instead of reference 400;
- collection APIs are cursor-paginated rather than unbounded;
- name validation rejects control whitespace;
- allowed domains use an explicit table instead of generic JSON metadata;
- account deletion prevents ownerless shared organizations;
- invitation CTA and routes remain absent until iteration 6;
- product dashboard/app shell visuals remain iteration 9;
- RFC Problem Details and generated REST SDK replace Server Actions/Better Auth.

## 21. Delivery and review workflow

Implementation is decomposed among subagents with explicit file ownership and
parent integration/review. Each subtask follows test-first order; the parent
runs focused and complete gates and verifies the immutable reference.

After implementation:

1. commit intentional changes and push the branch;
2. create a ready, non-draft PR;
3. wait for the repository's automatic review;
4. inspect every actionable review comment;
5. reproduce issues with a failing test when applicable, fix, rerun relevant and
   full gates, commit and push;
6. repeat review rounds until the reviewer reports no actionable comments;
7. record final verification and known external gaps without overstating live
   evidence.
