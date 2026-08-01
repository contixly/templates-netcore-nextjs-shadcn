# Итерация 6: teams и invitations

**Дата:** 2026-08-01  
**Статус:** утверждённый дизайн  
**Ветка:** `codex/iteration-6-teams-invitations`

## 1. Цель

Восстановить collaboration workflows из immutable reference `template/` как
один завершённый вертикальный срез на целевой архитектуре:

- ASP.NET Core 10 владеет REST, бизнес-правилами, авторизацией и PostgreSQL;
- Next.js является отдельным UI и использует только generated REST SDK;
- пользователь может создавать и администрировать команды, управлять их
  составом, создавать приглашения в workspace или конкретную команду, а
  получатель — просматривать, принимать или отклонять приглашение;
- существующая fixed-role модель `owner | admin | member` остаётся источником
  правды для organization membership и invitation role assignment;
- архитектура оставляет явную границу будущей доставки уведомлений, но не
  добавляет внешний email-провайдер, background worker или outbox.

Итерация должна закончиться работающими API/UI/E2E-сценариями, обновлёнными
OpenAPI/generated contract и документированной acceptance evidence. Она не
начинает итерацию 7 или другие предметные области.

## 2. Изученный контекст

### Обязательные документы

- `AGENTS.md`;
- `docs/aspnetcore-migration-plan.md`;
- `docs/api-conventions.md`;
- `docs/web-conventions.md`;
- `docs/authentication-persistence-operations.md`;
- design и implementation plan итерации 5.

### Reference routes и страницы

- `/w/[organizationKey]/settings/teams`;
- `/w/[organizationKey]/settings/invitations`;
- `/user/invitations`;
- `/invite/[invitationId]`;
- organization settings/users/roles и zero-workspace onboarding как смежные
  поверхности.

### Reference feature-код

- `template/src/features/workspaces/actions/*team*`;
- `template/src/features/workspaces/actions/*invitation*`;
- `template/src/features/workspaces/workspaces-teams-*`;
- `template/src/features/workspaces/workspaces-invitations-*`;
- `template/src/features/workspaces/workspaces-permissions.ts`;
- `template/src/features/workspaces/workspaces-roles.ts`;
- `template/src/server/auth/organization-access.ts`;
- `template/src/server/auth/organization-hooks.ts`;
- `template/prisma/schema.prisma` и team/invitation migrations.

### Reference tests и user journeys

- `template/e2e/specs/workspace-team-management/workspace-teams.spec.ts`;
- `template/e2e/specs/workspace-invitation-management/*.spec.ts`;
- `template/e2e/specs/workspace-onboarding-guard/zero-workspace.spec.ts`;
- team/invitation action, schema, loader, repository, component и page tests
  под `template/test/features/workspaces/`;
- account invitations page tests.

Reference не отправляет реальное invitation email: после создания UI показывает
invitation link. Better Auth organization plugin в reference не переопределяет
`invitationExpiresIn`; его документированный default равен 48 часам. Целевая
реализация фиксирует 48 часов явно, чтобы поведение не зависело от стороннего
default.

## 3. Dependency gate и scope

### Зависимости выполнены

- iteration 5 завершила Organization, membership, роли, active organization,
  domain restrictions, settings/users UI и organization-aware cleanup;
- `TemplateDbContext` является общей persistence/transaction boundary;
- cookie session, CSRF, Problem Details, auditing, OpenAPI export и generated
  TypeScript SDK уже приняты;
- UI имеет organization-aware routing и settings shell.

### Входит

- Team CRUD;
- team membership add/remove и read-only projection;
- searchable assignable organization-member projection;
- organization invitation activity и create flow;
- workspace-only и team-targeted invitations;
- pending invitations текущего пользователя;
- invitation decision detail, accept и reject;
- expiry, recipient/email-verification/domain/team/role security;
- reuse существующего member role-change API;
- `IInvitationNotifier` и безопасный no-op/local-preview adapter;
- local-only automation email-confirmation support для black-box E2E;
- schema migration, REST/OpenAPI/generated SDK, UI, Jest и Playwright;
- durable docs и migration register/evidence.

### Не входит

- внешний SMTP/email/SaaS provider;
- outbox, message broker, background delivery/retry worker;
- invitation cancellation/resend UI;
- удаление organization members;
- active team session/context и active-team UI;
- custom roles;
- API keys и public `/api/v1` machine surface итерации 7;
- product dashboard, documents/search, YARP, Docker, Aspire или production
  orchestration;
- data/session migration из `template/`;
- OpenSpec change/spec.

## 4. Рассмотренные архитектурные варианты

### A — отдельный collaboration slice в общем DbContext — выбран

Teams и invitations получают собственные Domain/Application policies, services
и ports. Infrastructure реализует их через тот же `TemplateDbContext` и схему
`organizations`. Это сохраняет реальные PostgreSQL FK и единые транзакции с
Organization, membership и текущей session, не раздувая существующие
`OrganizationService`/`IOrganizationStore`.

### B — добавить всё в OrganizationService/IOrganizationStore

Требует меньше новых типов, но смешивает organization lifecycle, member role
management, teams и invitation state machine в одном сервисе/порту. Это ухудшает
изолированное тестирование и превращает organization feature в монолит.

### C — отдельный CollaborationDbContext

Даёт формальную persistence-изоляцию, но разрушает простую атомарность invitation
accept, требует межконтекстной координации и ослабляет FK между team membership,
organization membership и session. Для одной PostgreSQL базы это преждевременная
сложность.

## 5. Reference correspondence

| Reference | Новый API | Новый UI | Acceptance tests |
| --- | --- | --- | --- |
| team actions/repository и Prisma `Team` | team collection/item CRUD | `/w/{key}/settings/teams` | Domain/Application, PostgreSQL, API, Jest, Playwright |
| add/remove team member и `TeamMember` | team members + candidates | team cards/member controls | tenant/permission/race tests и multi-user E2E |
| create/list invitations | organization invitations | `/w/{key}/settings/invitations` | duplicate/domain/role/team/expiry coverage |
| pending invitation loader | account invitation collection | `/user/invitations` и onboarding CTA | filtering/pagination/Jest/E2E |
| decision loader + accept/reject actions | decision/accept/reject endpoints | `/invite/{id}` | recipient/verification/atomicity/E2E |
| Better Auth notification hook boundary | `IInvitationNotifier` | same-origin link shown after create | adapter contract and safe-failure tests |
| existing member-role action | existing member PATCH | existing users role control | iteration-5 regression coverage |

## 6. Layer architecture

### Domain

- `TeamId` and `InvitationId` UUID value objects;
- team-name normalization/validation policy;
- `InvitationStatus` closed values: pending, accepted, rejected, canceled;
- derived invitation display status adds expired without persisting it;
- invitation transition policy;
- organization-role capability additions for teams and invitations;
- invitation role assignment delegates to the existing organization role
  policy rather than creating a second matrix.

Domain has no EF, HTTP, Identity, clock or notifier dependency.

### Application

- `TeamService` owns team commands, list/candidate cursors and validation that is
  independent of HTTP;
- `InvitationService` owns create/list/decision/accept/reject orchestration;
- `ITeamStore` and `IInvitationStore` express atomic persistence operations;
- `IInvitationNotifier` receives an already-committed safe notification model;
- an injectable clock supplies expiry and deterministic tests;
- commands carry typed actor user/session/organization/team/invitation IDs.

### Infrastructure

- EF entities/configurations and one additive migration;
- `EfTeamStore` and `EfInvitationStore` use the shared `TemplateDbContext`;
- no-op production-safe notifier is the default until a future delivery
  iteration configures a provider;
- a test/local preview adapter can capture safe notification metadata in process
  but does not become a production inbox or persistence source of truth.

### Api

- `TeamEndpointModule` and `InvitationEndpointModule`;
- strict request DTOs, canonical UUID parsing, browser-session policy, CSRF,
  no-store, rate limits, Problem Details and one safe audit per operation;
- API maps Application failures to stable codes and never exposes persistence or
  notifier exceptions.

### Web

- generated SDK only for data and mutations;
- server loaders pass only the session cookie, correlation ID and SSR renewal
  suppression marker;
- browser mutations use the existing CSRF wrapper;
- components own presentation/localized recovery, never domain authorization.

## 7. PostgreSQL model

All new tables live in schema `organizations`.

### `organizations.teams`

- `id uuid` primary key;
- `organization_id uuid` required FK to organizations with cascade delete;
- `name varchar(50)`;
- `created_at`, `updated_at` timestamptz;
- name length/content check consistent with the target policy;
- case-insensitive unique expression index on organization plus PostgreSQL
  `lower(name)`;
- alternate unique key `(organization_id, id)` for tenant-qualified composite
  references;
- stable list index on `(organization_id, created_at, id)`.

Team names trim outer whitespace, retain valid internal spacing, reject control
whitespace, and accept Unicode letters/digits plus spaces, hyphen and underscore.
The unique index is the final race arbiter.

### `organizations.team_members`

- `id uuid` primary key;
- `organization_id uuid`;
- `team_id uuid`;
- `organization_member_id uuid`;
- `joined_at timestamptz`;
- composite FK `(organization_id, team_id)` and
  `(organization_id, organization_member_id)` guarantee that team and
  organization membership belong to the same organization; the existing
  members table receives the matching alternate key `(organization_id, id)`;
- unique `(team_id, organization_member_id)`;
- list index `(team_id, joined_at, id)`;
- deleting the underlying organization membership removes its team memberships.

The table references the organization membership edge rather than only a user
ID. This intentionally makes cross-organization team assignment impossible at
the database boundary.

### `organizations.invitations`

- cryptographically random UUID v4 primary key;
- required `organization_id`, nullable `team_id`;
- normalized lowercase `email` up to 254;
- closed role `owner | admin | member`;
- closed stored status `pending | accepted | rejected | canceled`;
- `inviter_user_id`, `expires_at`, `created_at`, `updated_at`;
- organization cascade delete and inviter user cascade delete;
- tenant-qualified composite team FK is restrictive; team deletion first
  clears `team_id` in the same transaction so invitation history survives as
  workspace-only history;
- partial unique index `(organization_id, email) WHERE status = 'pending'`;
- organization activity index `(organization_id, created_at DESC, id DESC)`;
- recipient index covers `(email, status, expires_at, created_at, id)`.
- inviter-cap index covers
  `(organization_id, inviter_user_id, status, expires_at)`.

`expired` remains derived as `status == pending && expiresAt <= now`. There is
no expiry worker. A re-invite transaction cancels an expired pending row before
inserting the replacement.

### No notification table

No outbox or delivery-attempt table is created. A real reliable delivery system
requires its own operational iteration, retry policy and provider semantics.

## 8. Authorization model

| Capability | member | admin | owner |
| --- | ---: | ---: | ---: |
| Read teams and team composition | yes | yes | yes |
| Create/update/delete teams | no | yes | yes |
| Search candidates and add/remove team members | no | yes | yes |
| Read organization invitation activity | no | yes | yes |
| Create invitation with member/admin role | no | yes | yes |
| Create invitation with owner role | no | no | yes |
| Read/respond to own invitation | matching recipient | matching recipient | matching recipient |

Rules are recomputed from current database membership inside every operation.
Server capabilities are presentation hints only. Existing organization role
change behavior remains in the iteration-5 endpoint; teams do not grant an
organization role.

## 9. REST contract

All success bodies use `{ "data": ... }`; errors use established RFC Problem
Details. Every operation requires `Api.BrowserSession`; unsafe methods require
the normal antiforgery pair. Responses are no-store.

### Teams

| Method | Route | Success |
| --- | --- | --- |
| GET | `/api/v1/organizations/{organizationId}/teams?cursor=&limit=` | `200 TeamPage` |
| POST | `/api/v1/organizations/{organizationId}/teams` | `201 Team` |
| PATCH | `/api/v1/organizations/{organizationId}/teams/{teamId}` | `200 Team` |
| DELETE | `/api/v1/organizations/{organizationId}/teams/{teamId}` | `200 TeamDeletion` |
| GET | `/api/v1/organizations/{organizationId}/teams/{teamId}/members?cursor=&limit=` | `200 TeamMemberPage` |
| POST | `/api/v1/organizations/{organizationId}/teams/{teamId}/members` | `201 TeamMember` |
| DELETE | `/api/v1/organizations/{organizationId}/teams/{teamId}/members/{userId}` | `200 TeamMemberRemoval` |
| GET | `/api/v1/organizations/{organizationId}/teams/{teamId}/member-candidates?q=&cursor=&limit=` | `200 TeamCandidatePage` |

Create and update accept strict `{ name }`. Add member accepts strict
`{ userId }`. Team list items contain id, organizationId, name, timestamps,
memberCount and the first member page; the dedicated members route provides
continuation.

### Invitations

| Method | Route | Success |
| --- | --- | --- |
| GET | `/api/v1/organizations/{organizationId}/invitations?status=&cursor=&limit=` | `200 OrganizationInvitationPage` |
| POST | `/api/v1/organizations/{organizationId}/invitations` | `201 Invitation` |
| GET | `/api/v1/account/invitations?cursor=&limit=` | `200 AccountInvitationPage` |
| GET | `/api/v1/invitations/{invitationId}` | `200 InvitationDecision` |
| POST | `/api/v1/invitations/{invitationId}/accept` | `200 AcceptedInvitation` |
| POST | `/api/v1/invitations/{invitationId}/reject` | `200 InvitationDecision` |

Create accepts strict `{ email, role, teamId? }`. `teamId` null/omitted means a
workspace-only invitation. A successful response includes a relative
`invitationPath`; the browser builds the absolute link from `window.location.origin`.
The API therefore does not embed a deployment-dependent frontend origin.

The existing member-role PATCH remains the only organization-role mutation API.

## 10. Pagination and filtering

Every cursor is opaque, typed, versioned, checksum-protected canonical base64url.
Wrong collection kind/version, malformed encoding, corrupt checksum, invalid
timestamps and extra bytes fail before persistence.

- default limit 50, accepted range 1..100;
- teams: immutable `(createdAt ASC, id ASC)`;
- team members: `(joinedAt ASC, teamMemberId ASC)`;
- candidates: organization membership `(joinedAt ASC, memberId ASC)`;
- organization invitations: `(createdAt DESC, id DESC)`;
- account invitations: `(expiresAt ASC, createdAt DESC, id DESC)`;
- organization invitation `status` accepts pending, accepted, rejected,
  canceled or derived expired; omission returns all activity;
- candidate `q` is optional, trimmed, at most 100 characters and performs a
  case-insensitive name/email search within the organization without ever
  entering logs.

Clients return `nextCursor` verbatim and never decode or synthesize it.

## 11. Validation and disclosure

### Team input

- trim then length 1..50;
- Unicode letters/digits, ordinary spaces, `-`, `_`;
- reject control whitespace;
- unchanged case-insensitive normalized rename conflicts.

### Invitation input

- trim/lowercase valid email, maximum 254;
- closed role enum;
- optional canonical UUID team ID;
- team must belong to the route organization;
- recipient must not already be an organization member;
- active domain policy must allow the email;
- actor must be permitted to assign the requested role.

Every route/body UUID uses canonical `D` representation. Unknown JSON fields,
empty PATCH documents, malformed limits and unsupported filters fail at the HTTP
boundary after actor resolution and inside the safe audit boundary.

Missing and foreign organization/team/member resources share non-disclosing
not-found outcomes. Recipient mismatch returns a forbidden code with no
invitation projection. No Problem Details response exposes email, name, search,
link, database text or exception detail.

## 12. Stable failures

### Teams

- `team_not_found` — 404;
- `team_permission_denied` — 403;
- `team_name_conflict`, `team_name_unchanged` — 409;
- `team_member_not_found` — 404;
- `team_member_already_exists` — 409.

### Invitations

- `invitation_not_found` — 404;
- `invitation_permission_denied` — 403;
- `invitation_already_exists` — 409;
- `invitation_recipient_already_member` — 409;
- `invitation_team_invalid` — 400;
- `invitation_domain_restricted` — 400 on create, non-actionable decision state
  on matching-recipient detail, and 403 on mutation;
- `invitation_recipient_mismatch` — 403 with no projection;
- `invitation_email_verification_required` — 403;
- `invitation_expired`, `invitation_not_pending` — 409 on mutation;
- `invitation_membership_conflict` — 409 when acceptance would violate the
  iteration-5 per-user accessible-organization-name invariant;
- `invitation_limit_reached` — 409 when one actor already has 100 unexpired
  pending invitations in the organization.

Existing `validation_failed`, `invalid_cursor`, `rate_limited` and
`concurrency_conflict` conventions remain in force.

## 13. Invitation lifecycle and security

### Create

One transaction locks/rechecks organization and actor membership, role
assignability, optional team ownership, domain eligibility and current member by
normalized email. It cancels an expired pending duplicate, rejects any live
pending duplicate, enforces at most 100 unexpired pending invitations per
`(organization, inviter)` and inserts a 48-hour pending invitation. Locking the
actor membership serializes this cap for one actor/organization; the partial
unique index independently classifies concurrent recipient duplicates.

After commit, Application invokes `IInvitationNotifier`. The notification model
contains only the recipient address and same-origin relative path required by a
future adapter. Adapter failure is safely logged without changing the committed
invitation or exposing its exception. The iteration-6 no-op/local-preview
adapter is deterministic and performs no network call.

### Decision read

The request requires an authenticated session. The store finds the invitation
by opaque ID and compares its normalized email with the current primary email
before returning details. Mismatch returns no invitation data. A matching
recipient receives one of pending, accepted, rejected, canceled, expired,
email-verification-required, domain-restricted or already-member.

### Accept

One transaction locks the invitation and relevant organization/membership rows,
then rechecks:

1. recipient email matches;
2. current primary email is verified;
3. status is pending and expiry is in the future;
4. domain policy still permits the recipient;
5. role remains valid;
6. optional team still belongs to the organization;
7. membership does not already exist and accessible-name invariants remain true.

It then creates organization membership, creates optional team membership,
marks the invitation accepted and sets the accepting browser session's active
organization atomically. Any failure rolls back all four effects.

Acceptance follows the established iteration-5 lock discipline: discover the
candidate IDs, lock affected organizations in ascending UUID order, then the
target user, invitation/team and ordered membership rows, and finally the one
organization-name advisory key. Every condition is re-read after the locks.

### Reject

Reject uses the same recipient, verification, status and expiry checks, locks the
same invitation row and atomically changes only its status. Accept/reject races
have one winner; the loser observes `invitation_not_pending`.

## 14. Team transactions and concurrency

- create/update/delete lock and recheck actor membership and current role;
- create/update use the normalized-name unique index as final arbitration;
- rename rejects an unchanged normalized name;
- add member locks team and target organization membership, then relies on
  `(team_id, organization_member_id)` uniqueness;
- remove requires a currently matching team membership;
- team deletion clears invitation targets and deletes the team in one
  transaction; team memberships cascade;
- bounded serialization/deadlock retry maps exhaustion to
  `concurrency_conflict`;
- teams never read, write or publish active-team session state.

## 15. Rate limiting and audit

Invitation creation uses a fixed-window limit of 20 requests per authenticated
user per minute with no queue. Accept/reject share a fixed-window limit of 30
requests per authenticated user per minute with no queue. These API limits are
independent of the 100-live-pending database cap. Partition keys use only the
actor ID and never email, candidate query or invitation link.

Audit records operation, outcome, trace ID, actor user/session and applicable
opaque organization/team/invitation IDs. It excludes names, emails, candidate
queries, role/body values, invitation paths, cookies, cursors and raw route text.
Authentication and CSRF failures before actor resolution are not misreported as
domain operation attempts.

## 16. Local automation boundary

Local automation users remain unverified at creation. A new CSRF-protected,
authenticated local-only operation confirms the current local-automation
user's primary email and renews/reissues its browser session so subsequent
invitation decisions see the updated verified state.

The operation is available only when environment is Development or Test and
`LocalAutomationAuth:Enabled=true`; Production returns `404 local_auth_disabled`
even if the flag is set. It is tagged `local-only`/`x-local-only` in OpenAPI.
Playwright uses the generated SDK rather than direct database mutation.

## 17. Web behavior

### Navigation

- organization settings adds Teams for every member;
- Invitations is visible only when the server projection says the actor may
  manage invitations;
- account navigation adds Invitations;
- zero-organization onboarding adds Review Invitations without making account
  pages organization-dependent.

### `/w/[organizationKey]/settings/teams`

- canonical-key redirect follows existing organization rules;
- SSR loads organization and first team page through generated SDK;
- members see names, counts and composition read-only;
- admins/owners see create, rename, delete, candidate search, add and remove;
- controls never mention or manipulate an active team;
- list/member continuation preserves already loaded data on a safe partial
  failure and offers explicit retry.

### `/w/[organizationKey]/settings/invitations`

- unauthorized members receive the existing protected forbidden page and no
  activity disclosure;
- admin/owner sees paged activity, status filter and create dialog;
- dialog validates email/role/optional team and displays the returned
  same-origin invitation link with copy affordance;
- safe server error copy branches only on stable problem code.

### `/user/invitations`

- shows only unexpired pending invitations matching the current email and for
  organizations the user cannot already access;
- supports continuation and empty state;
- each item links to `/invite/{invitationId}`.

### `/invite/[invitationId]`

- matching recipient sees organization, email, role, optional team, inviter and
  expiry information;
- pending verified recipient gets accept/reject controls;
- expired, accepted, rejected, canceled, domain-restricted, already-member and
  verification-required states are read-only;
- recipient mismatch maps to a safe forbidden state without details;
- accept redirects to the canonical organization dashboard after server success;
- reject stays on the decision page in rejected state;
- mutation success followed by refresh/navigation failure is represented as
  partial success and is never retried as a duplicate mutation.

All user-visible copy is present in en/ru catalogs. Controls keep accessible
labels and keyboard behavior consistent with the current shadcn primitives.

## 18. Generated contract discipline

- OpenAPI documents every success/error status, enums, strict schemas,
  pagination bounds, CSRF header and browser-cookie security;
- each collection cursor has a distinct generated type contract;
- generated TypeScript is regenerated, committed and checked for drift;
- web adapters may narrow generated types but may not duplicate API DTOs by
  hand;
- local-only email confirmation is marked explicitly and cannot be mistaken for
  production account verification.

## 19. Test-first implementation order

1. Domain tests for team names, capability matrix and invitation transitions.
2. Application tests for team and invitation services/cursors/notifier ordering.
3. EF model tests and additive migration.
4. PostgreSQL store tests for tenant constraints, transactions and races.
5. Account/local-cleanup regression tests with new relationships.
6. API boundary/security/OpenAPI tests, then endpoint implementation.
7. Export OpenAPI and regenerate the SDK.
8. Read installed Next.js documentation, then add failing Jest tests and web
   implementation.
9. Add deterministic multi-context Playwright flows.
10. Update durable docs and run all acceptance gates.

Implementation is split among subagents with explicit non-overlapping file
ownership. The controller integrates, runs focused/full gates and owns commits,
pushes, PR state and automatic-review loops.

## 20. Test matrix

### Domain/Application

- team-name normalization, invalid content and unchanged/duplicate outcomes;
- capability and invitation role assignment matrix;
- every invitation display/transition state;
- 48-hour boundary with injected clock;
- cursor kinds, canonical encoding and corruption;
- notifier only after successful commit; notifier failure does not rewrite the
  stored result.

### Infrastructure

- exact schemas, checks, indexes, composite FKs and cascade/restrict behavior;
- impossible cross-organization team membership;
- case-insensitive team name races;
- add/add and add/remove team membership races;
- duplicate/re-invite races and expired replacement;
- accept/accept, accept/reject, expiry-boundary and team-deletion races;
- atomic membership + optional team membership + invitation + active session;
- accessible-name conflict on acceptance;
- organization/account/local cleanup leaves no orphan team/invitation data.

### API

- 401, CSRF, no-store, strict JSON and canonical UUID validation;
- role/permission matrix and foreign-resource non-disclosure;
- stable failures and safe Problem Details;
- limit/filter/search/cursor validation before persistence;
- recipient mismatch, unverified email, expiry and domain recheck;
- rate limits and safe audit fields;
- exact OpenAPI security/error/filter/enum/local-only contract.

### Web/Jest

- server-loader header allow-list and renewal suppression;
- routes, canonical redirects, nav visibility and onboarding CTA;
- team read-only/manage controls and partial refresh recovery;
- invitation create/link/status/filter/pagination behavior;
- account list and every decision state;
- accept redirect, reject state and mutation partial-success safety;
- generated-only API boundary and en/ru catalog completeness.

### Playwright

- owner creates/renames/deletes a team and adds/removes an existing member;
- member sees the same team read-only and no active-team controls;
- owner/admin creates invitations while member is denied;
- duplicate and restricted-domain invitation are rejected;
- team-targeted invitation acceptance creates both memberships;
- recipient reviews and accepts one invitation, rejects another;
- invitation is listed from zero-workspace onboarding/account navigation;
- local-only email confirmation occurs through generated REST, never direct DB;
- teardown removes users/organizations and all collaboration dependents.

## 21. Acceptance commands

Required .NET gates:

```bash
dotnet restore Template.sln
dotnet build Template.sln --no-restore
dotnet test Template.sln --no-restore
dotnet format Template.sln --no-restore --verify-no-changes
```

Also required:

- clean migration apply to PostgreSQL;
- `dotnet ef migrations has-pending-model-changes`;
- non-empty idempotent migration script inspection;
- NuGet vulnerability scan;
- two deterministic OpenAPI exports with identical hash;
- generated SDK drift check;
- `npm ci` when dependency state requires it;
- production npm vulnerability scan;
- boundary, Prettier, ESLint, Next typegen and TypeScript checks;
- focused and full Jest;
- clean Next.js production build and standalone artifact check;
- focused and full deterministic Playwright;
- `git diff --check`;
- empty working-tree and branch-range diffs for `template/`;
- no OpenSpec artifact.

## 22. Durable documentation

Implementation updates in the same change:

- `docs/api-conventions.md` — collaboration REST, authorization, errors,
  filters, cursors, rate limits and notifier semantics;
- `docs/web-conventions.md` — routes, server loading, mutations and recovery;
- `docs/authentication-persistence-operations.md` — migration, schema,
  cleanup and local email-confirmation operations;
- `docs/aspnetcore-migration-plan.md` — iteration scope/status,
  correspondence, acceptance evidence, differences and next gate.

## 23. Intentional differences from reference

- team membership references the organization membership edge with database
  tenant constraints rather than only a user ID;
- collections are cursor-paginated and team candidates are searchable rather
  than unbounded;
- invitation expiry is explicitly fixed at 48 hours;
- invitation URL is a relative same-origin path, not an API-configured frontend
  absolute URL;
- missing/foreign resources use non-disclosing failures;
- accept is one explicit transaction including active organization and optional
  team membership;
- active team is deliberately absent;
- RFC Problem Details/generated SDK replace Server Actions/Better Auth;
- direct Playwright database email verification is replaced by a gated local-only
  API operation;
- no real email is sent in this iteration.

These differences strengthen the target architecture without changing the
accepted visible collaboration workflows.

## 24. Delivery and automatic-review workflow

1. Implement through subagents with controller-owned integration.
2. Run focused RED/GREEN loops and complete acceptance gates.
3. Commit intentional changes and push the iteration branch.
4. Create a ready, non-draft PR.
5. Wait for the repository automatic review.
6. Inspect every actionable comment and reproduce defects with a failing test
   where applicable.
7. Fix, rerun relevant and complete gates, commit and push.
8. Request/wait for a fresh review after every changed head.
9. Repeat until the reviewer reports no actionable comments and all review
   threads are resolved.
10. Record final reviewed head and evidence without claiming unobserved checks.
