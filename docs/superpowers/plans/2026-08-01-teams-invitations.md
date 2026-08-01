# Teams and Invitations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver migration iteration 6 as a test-first vertical slice for team management and secure workspace invitations through ASP.NET Core REST and a generated-SDK Next.js UI.

**Architecture:** Add a bounded `Collaboration` feature with Domain policies, separate Team and Invitation Application services/ports, and EF Core stores in the shared `TemplateDbContext`. PostgreSQL schema `organizations` enforces tenant-qualified team membership and atomic invitation acceptance; API owns cookie authorization, CSRF, rate limits, validation, Problem Details and audit; Next.js remains a REST-only presentation client.

**Tech Stack:** .NET SDK 10.0.302, ASP.NET Core/EF Core 10.0.10, Npgsql 10.0.3, PostgreSQL 18.4 Testcontainers, xUnit v3, OpenAPI 3.1, Next.js 16.2.11, React 19.2.8, TypeScript 6.0.3, next-intl 4.13.4, generated `@hey-api/openapi-ts` 0.99.0 SDK, Jest 30.4.2, Playwright 1.61.1.

## Global Constraints

- `template/` is immutable reference material: never edit, format, move, delete, install into, or run migrations inside it.
- Do not create an OpenSpec change/spec.
- Dependencies remain `Api → Application`, `Infrastructure → Application/Domain`, `Application → Domain`; Domain has no HTTP, EF, Identity, logging or notifier dependency.
- ASP.NET Core owns every `/api/**` route, business rule, authorization decision, database write and external-adapter boundary.
- Next.js uses generated REST SDK functions only: no Prisma, Better Auth, Server Actions, direct database access, handwritten transport DTOs or bearer tokens in browser storage.
- Browser authentication remains the secure HttpOnly same-origin `__Host-template.session` cookie; every unsafe browser operation obtains a fresh CSRF token.
- Stored invitation statuses are exactly `pending|accepted|rejected|canceled`; `expired` is derived; expiry is exactly 48 hours.
- Invitation creation permits at most 100 unexpired pending invitations per `(organization, inviter)`; API create limit is 20 requests/user/minute and accept/reject share 30 requests/user/minute, with no queue.
- Real email delivery, outbox, background worker, cancellation/resend UI, organization-member deletion, active team, custom roles, API keys, product dashboard, YARP, Docker and Aspire remain outside iteration 6.
- Logs never contain names, emails, candidate queries, role/body values, invitation paths, cookies, cursors or raw invalid route text.
- Every behavior starts with a focused failing test, an observed RED for the intended reason, the minimum implementation, and observed GREEN.
- Before Next.js production edits, read the installed Next.js 16.2.11 documentation listed in Task 10.
- Required completion gates include `dotnet restore Template.sln`, `dotnet build Template.sln --no-restore`, and `dotnet test Template.sln --no-restore` plus Task 14 contract/web/E2E/security checks.
- Before and after every task, `git diff -- template/`, `git status --short -- template/`, and the untracked-file check under `template/` must be empty.

---

## File Structure

### Domain and Application

- `apps/api/src/Template.Domain/Collaboration/TeamId.cs` — UUID team value.
- `apps/api/src/Template.Domain/Collaboration/TeamMemberId.cs` — UUID team-membership value.
- `apps/api/src/Template.Domain/Collaboration/InvitationId.cs` — cryptographically random UUID invitation value.
- `apps/api/src/Template.Domain/Collaboration/TeamName.cs` — team-name policy.
- `apps/api/src/Template.Domain/Collaboration/InvitationStatus.cs` — stored status values.
- `apps/api/src/Template.Domain/Collaboration/InvitationPolicy.cs` — display state and transition rules.
- `apps/api/src/Template.Domain/Organizations/OrganizationPermissionPolicy.cs` — add team/invitation capabilities without duplicating role assignment.
- `apps/api/src/Template.Application/Collaboration/TeamModels.cs` — team projections, commands and outcomes.
- `apps/api/src/Template.Application/Collaboration/TeamCursor.cs` — team/member/candidate cursors.
- `apps/api/src/Template.Application/Collaboration/Ports/ITeamStore.cs` — atomic team persistence port.
- `apps/api/src/Template.Application/Collaboration/TeamService.cs` — team use cases.
- `apps/api/src/Template.Application/Collaboration/InvitationModels.cs` — invitation projections, commands and outcomes.
- `apps/api/src/Template.Application/Collaboration/InvitationCursor.cs` — organization/account invitation cursors.
- `apps/api/src/Template.Application/Collaboration/Ports/IInvitationStore.cs` — atomic invitation persistence port.
- `apps/api/src/Template.Application/Collaboration/Ports/IInvitationNotifier.cs` — post-commit notification boundary.
- `apps/api/src/Template.Application/Collaboration/InvitationService.cs` — invitation use cases.

### Infrastructure

- `apps/api/src/Template.Infrastructure/Collaboration/*Entity.cs` — Team, TeamMember and Invitation rows.
- `apps/api/src/Template.Infrastructure/Collaboration/*EntityConfiguration.cs` — schema, keys, checks, FKs and indexes.
- `apps/api/src/Template.Infrastructure/Collaboration/EfTeamStore.cs` — team reads/mutations.
- `apps/api/src/Template.Infrastructure/Collaboration/EfInvitationStore.cs` — invitation reads/state transitions.
- `apps/api/src/Template.Infrastructure/Collaboration/NoOpInvitationNotifier.cs` — no-network adapter.
- `apps/api/src/Template.Infrastructure/Collaboration/SafeInvitationNotifier.cs` — exception-to-outcome/logging decorator.
- `apps/api/src/Template.Infrastructure/Persistence/Migrations/*_TeamsInvitations.cs` — one additive migration plus designer/snapshot.
- Modify `TemplateDbContext`, organization-member mapping, DI and organization-aware cleanup tests.

### API and contract

- `apps/api/src/Template.Api/Features/Collaboration/TeamContracts.cs` — strict team HTTP contracts.
- `apps/api/src/Template.Api/Features/Collaboration/TeamEndpointModule.cs` — eight team operations.
- `apps/api/src/Template.Api/Features/Collaboration/InvitationContracts.cs` — strict invitation HTTP contracts.
- `apps/api/src/Template.Api/Features/Collaboration/InvitationEndpointModule.cs` — six invitation operations.
- `apps/api/src/Template.Api/Features/Collaboration/CollaborationEndpointBoundary.cs` — actor, canonical UUID, safe audit and result mapping helpers.
- `apps/api/src/Template.Api/Features/Collaboration/CollaborationSecurityEvents.cs` — safe structured event.
- `apps/api/src/Template.Api/OpenApi/CollaborationContractSchemaTransformer.cs` and `CollaborationContractOperationTransformer.cs` — exact schemas/responses.
- Modify auth local-only contracts/service/endpoint, rate-limit registration/order, Problem codes/details, module/DI registration and `contracts/openapi/v1.json`.

### Web

- `apps/web/src/features/collaboration/collaboration-routes.ts` — typed settings/account/decision routes.
- `apps/web/src/lib/api/collaboration/server/*` — generated-SDK SSR loaders.
- `apps/web/src/lib/api/collaboration/browser/collaboration-mutations.ts` — generated-SDK CSRF mutations.
- `apps/web/src/components/collaboration/*` — focused team/invitation components.
- Add App Router pages under organization settings, `/user/invitations`, `/invite/[invitationId]`, loading/error boundaries and organization-switcher parallel slots.
- `apps/web/src/messages/collaboration.{en,ru}.json` — fixed-locale collaboration copy.
- Modify organization/account routes, navigation, onboarding, i18n catalog composition and generated SDK.

### Tests and docs

- Application tests under `apps/api/tests/Template.Application.Tests/Collaboration/`.
- PostgreSQL/API tests under `apps/api/tests/Template.Api.Tests/Collaboration/`.
- Focused Jest tests under `apps/web/test/{app,components,features,lib/api}/`.
- `apps/web/e2e/collaboration.spec.ts` and generated-only collaboration helpers.
- Update `docs/api-conventions.md`, `docs/web-conventions.md`, `docs/authentication-persistence-operations.md`, and `docs/aspnetcore-migration-plan.md`.

---

### Task 1: Collaboration Domain Values and Capabilities

**Files:**

- Create the six Domain files listed under Domain above.
- Modify: `apps/api/src/Template.Domain/Organizations/OrganizationPermissionPolicy.cs`
- Test: `apps/api/tests/Template.Application.Tests/Collaboration/CollaborationDomainTests.cs`
- Test: `apps/api/tests/Template.Application.Tests/Organizations/OrganizationDomainTests.cs`

**Interfaces:**

- Consumes: `OrganizationRole` and existing UUID value-object conventions.
- Produces: `TeamId`, `TeamMemberId`, `InvitationId`, `TeamName.TryCreate`, `InvitationStatus.TryParse`, `InvitationPolicy.GetDisplayState`, and capabilities `CanManageTeams`/`CanManageInvitations`.

- [ ] **Step 1: Write failing value/policy tests**

```csharp
[Theory]
[InlineData(" Design ", "Design")]
[InlineData("Команда_1", "Команда_1")]
public void Team_names_are_trimmed_and_unicode_safe(string input, string expected)
{
    Assert.True(TeamName.TryCreate(input, out var name));
    Assert.Equal(expected, name.Value);
}

[Theory]
[InlineData("", false)]
[InlineData("name\nother", false)]
[InlineData("abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz", false)]
public void Invalid_team_names_are_rejected(string input, bool expected) =>
    Assert.Equal(expected, TeamName.TryCreate(input, out _));

[Fact]
public void Pending_invitation_at_expiry_is_displayed_as_expired() =>
    Assert.Equal(
        InvitationDisplayState.Expired,
        InvitationPolicy.GetDisplayState(
            InvitationStatus.Pending,
            DateTimeOffset.Parse("2026-08-03T00:00:00Z"),
            DateTimeOffset.Parse("2026-08-03T00:00:00Z")));
```

Also assert that admin/owner manage teams and invitations, member does not, and only owner can assign owner through the existing `CanAssign` policy.

- [ ] **Step 2: Run the focused tests and observe RED**

```bash
dotnet test apps/api/tests/Template.Application.Tests/Template.Application.Tests.csproj \
  --no-restore --filter FullyQualifiedName~CollaborationDomainTests
```

Expected: compilation fails because the collaboration types do not exist.

- [ ] **Step 3: Implement the exact public values**

```csharp
public readonly record struct TeamId(Guid Value)
{
    public static TeamId New(DateTimeOffset now) => new(Guid.CreateVersion7(now));
}

public readonly record struct InvitationId(Guid Value)
{
    public static InvitationId New() => new(Guid.NewGuid());
}

public readonly record struct TeamName
{
    public const int MaximumLength = 50;
    public string Value { get; }
    private TeamName(string value) => Value = value;
    public static bool TryCreate(string? value, out TeamName name)
    {
        name = default;
        var normalized = value?.Trim();
        if (normalized is null or { Length: < 1 or > MaximumLength } ||
            normalized.Any(char.IsControl) ||
            normalized.Any(character =>
                !char.IsLetterOrDigit(character) &&
                character is not ' ' and not '-' and not '_'))
        {
            return false;
        }

        name = new TeamName(normalized);
        return true;
    }
    public override string ToString() => Value;
}
```

Implement `TeamMemberId` like `TeamId`; implement closed invitation stored/display states and transition predicates. Extend the existing record exactly to:

```csharp
public sealed record OrganizationCapabilities(
    bool CanUpdateOrganization,
    bool CanDeleteOrganization,
    bool CanAddMembers,
    bool CanUpdateMemberRoles,
    bool CanManageTeams,
    bool CanManageInvitations);
```

- [ ] **Step 4: Run Domain/Application regression tests**

```bash
dotnet test apps/api/tests/Template.Application.Tests/Template.Application.Tests.csproj \
  --no-restore --filter 'FullyQualifiedName~CollaborationDomainTests|FullyQualifiedName~OrganizationDomainTests'
```

Expected: PASS; existing role behavior remains unchanged.

- [ ] **Step 5: Commit**

```bash
git add apps/api/src/Template.Domain apps/api/tests/Template.Application.Tests
git commit -m "feat: define collaboration domain policies"
```

---

### Task 2: Team Application Contract and Service

**Files:**

- Create: `apps/api/src/Template.Application/Collaboration/TeamModels.cs`
- Create: `apps/api/src/Template.Application/Collaboration/TeamCursor.cs`
- Create: `apps/api/src/Template.Application/Collaboration/Ports/ITeamStore.cs`
- Create: `apps/api/src/Template.Application/Collaboration/TeamService.cs`
- Test: `apps/api/tests/Template.Application.Tests/Collaboration/TeamCursorTests.cs`
- Test: `apps/api/tests/Template.Application.Tests/Collaboration/TeamServiceTests.cs`

**Interfaces:**

- Consumes: Task 1 values, `UserId`, `OrganizationId`, `OrganizationMemberId`, `OrganizationRole`.
- Produces: paged team/member/candidate projections, `TeamFailure`, commands, `ITeamStore`, `TeamService`.

- [ ] **Step 1: Write failing cursor and service tests**

```csharp
[Fact]
public void Team_cursor_rejects_a_member_cursor()
{
    var member = TeamCursor.Encode(new TeamMemberCursorPosition(
        DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
        new TeamMemberId(Guid.Parse("00000000-0000-0000-0000-000000000001"))));
    Assert.False(TeamCursor.TryDecode(member, out TeamCursorPosition _));
}

[Fact]
public async Task Create_normalizes_before_calling_the_store()
{
    var result = await service.CreateAsync(actor, organization, " Design ", CancellationToken.None);
    Assert.True(result.Succeeded);
    Assert.Equal("Design", store.LastCreate!.Name.Value);
}
```

Cover limit 0/101, corrupt/noncanonical cursor, invalid/unchanged name, 101-character query, and propagation of every store failure.

- [ ] **Step 2: Run the focused tests and observe RED**

```bash
dotnet test apps/api/tests/Template.Application.Tests/Template.Application.Tests.csproj \
  --no-restore --filter 'FullyQualifiedName~TeamCursorTests|FullyQualifiedName~TeamServiceTests'
```

Expected: compilation fails on missing Team Application types.

- [ ] **Step 3: Define the exact models and port**

```csharp
public sealed record TeamMemberView(
    TeamMemberId Id, UserId UserId, string Name, string Email, string? ImageUrl,
    OrganizationRole Role, DateTimeOffset OrganizationJoinedAt, DateTimeOffset TeamJoinedAt);
public sealed record TeamCandidate(
    OrganizationMemberId MemberId, UserId UserId, string Name, string Email,
    string? ImageUrl, OrganizationRole Role, DateTimeOffset JoinedAt);
public sealed record TeamSummary(
    TeamId Id, OrganizationId OrganizationId, TeamName Name, int MemberCount,
    TeamMemberPage Members, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record TeamPage(IReadOnlyList<TeamSummary> Items, string? NextCursor);
public sealed record TeamMemberPage(IReadOnlyList<TeamMemberView> Items, string? NextCursor);
public sealed record TeamCandidatePage(IReadOnlyList<TeamCandidate> Items, string? NextCursor);
```

Define `TeamFailure` exactly as `InvalidName, InvalidCursor, NotFound, PermissionDenied, NameConflict, NameUnchanged, MemberNotFound, MemberAlreadyExists, ConcurrencyConflict`. The port exposes list/create/update/delete, list/add/remove members and list candidates using typed cursor positions and commands.

- [ ] **Step 4: Implement the minimum TeamService**

`TeamService` validates 1..100 limits, maximum 100-character candidate query, distinct cursor kinds and `TeamName`; it never authorizes from client capabilities. Use the same `OperationResult<T>` pattern as organizations and encode only a store-returned next position.

- [ ] **Step 5: Run focused and full Application tests**

```bash
dotnet test apps/api/tests/Template.Application.Tests/Template.Application.Tests.csproj \
  --no-restore --filter 'FullyQualifiedName~Team'
dotnet test apps/api/tests/Template.Application.Tests/Template.Application.Tests.csproj --no-restore
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add apps/api/src/Template.Application/Collaboration apps/api/tests/Template.Application.Tests/Collaboration
git commit -m "feat: add team application services"
```

---

### Task 3: Invitation Application Contract, Cursor and Notifier Boundary

**Files:**

- Create: `apps/api/src/Template.Application/Collaboration/InvitationModels.cs`
- Create: `apps/api/src/Template.Application/Collaboration/InvitationCursor.cs`
- Create: `apps/api/src/Template.Application/Collaboration/Ports/IInvitationStore.cs`
- Create: `apps/api/src/Template.Application/Collaboration/Ports/IInvitationNotifier.cs`
- Create: `apps/api/src/Template.Application/Collaboration/InvitationService.cs`
- Test: `apps/api/tests/Template.Application.Tests/Collaboration/InvitationCursorTests.cs`
- Test: `apps/api/tests/Template.Application.Tests/Collaboration/InvitationServiceTests.cs`

**Interfaces:**

- Consumes: Task 1 values, `OrganizationRole`, `SessionId`, `TimeProvider`.
- Produces: invitation pages/decision/accepted result, commands, failures, store/notifier ports and post-commit service ordering.

- [ ] **Step 1: Write failing lifecycle, cursor and notifier tests**

```csharp
[Fact]
public async Task Successful_create_notifies_after_the_store_returns_success()
{
    store.CreateResult = InvitationOperationResult<InvitationView>.Success(invitation);
    var result = await service.CreateAsync(command, CancellationToken.None);
    Assert.True(result.Succeeded);
    Assert.Equal(new[] { "store", "notify" }, calls);
    Assert.Equal($"/invite/{invitation.Id.Value:D}", notifier.Last!.InvitationPath);
}

[Fact]
public async Task Notification_failure_does_not_replace_committed_success()
{
    notifier.Result = InvitationNotificationOutcome.Failed;
    var result = await service.CreateAsync(command, CancellationToken.None);
    Assert.True(result.Succeeded);
}
```

Also prove 48-hour expiry is supplied from `TimeProvider`, cursor kinds are not interchangeable, and store failure does not invoke notifier.

- [ ] **Step 2: Run the focused tests and observe RED**

```bash
dotnet test apps/api/tests/Template.Application.Tests/Template.Application.Tests.csproj \
  --no-restore --filter 'FullyQualifiedName~InvitationCursorTests|FullyQualifiedName~InvitationServiceTests'
```

Expected: compilation fails on missing invitation types.

- [ ] **Step 3: Define the exact projections and outcomes**

```csharp
public sealed record InvitationView(
    InvitationId Id, OrganizationId OrganizationId, string OrganizationName,
    string CanonicalOrganizationKey, TeamId? TeamId, string? TeamName,
    string Email, OrganizationRole Role, InvitationStatus Status,
    InvitationDisplayState DisplayState, DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt, UserId InviterId, string InviterName);
public sealed record InvitationDecision(
    InvitationView? Invitation, InvitationDecisionState State, bool CanRespond);
public sealed record AcceptedInvitation(
    InvitationId InvitationId, OrganizationId OrganizationId, string CanonicalOrganizationKey);
public sealed record InvitationNotification(string RecipientEmail, string InvitationPath);
public enum InvitationNotificationOutcome { Completed, Skipped, Failed }
```

Define `InvitationFailure` exactly as `InvalidCursor, NotFound, PermissionDenied, AlreadyExists, RecipientAlreadyMember, TeamInvalid, DomainRestricted, RecipientMismatch, EmailVerificationRequired, Expired, NotPending, MembershipConflict, LimitReached, ConcurrencyConflict`.

- [ ] **Step 4: Define the ports and implement InvitationService**

`IInvitationStore` exposes organization/account list, decision, create, accept and reject methods. `IInvitationNotifier.NotifyCreatedAsync` returns `InvitationNotificationOutcome`. `InvitationService.CreateAsync` calls the store, then notifier only after a successful store result, builds `/invite/{uuid:D}`, and returns the committed store result for Completed/Skipped/Failed.

- [ ] **Step 5: Run focused and full Application tests**

```bash
dotnet test apps/api/tests/Template.Application.Tests/Template.Application.Tests.csproj \
  --no-restore --filter 'FullyQualifiedName~Invitation'
dotnet test apps/api/tests/Template.Application.Tests/Template.Application.Tests.csproj --no-restore
```

- [ ] **Step 6: Commit**

```bash
git add apps/api/src/Template.Application/Collaboration apps/api/tests/Template.Application.Tests/Collaboration
git commit -m "feat: add invitation application lifecycle"
```

---

### Task 4: EF Collaboration Model and Additive Migration

**Files:**

- Create: `TeamEntity.cs`, `TeamMemberEntity.cs`, `InvitationEntity.cs` and their configurations under `apps/api/src/Template.Infrastructure/Collaboration/`.
- Modify: `apps/api/src/Template.Infrastructure/Persistence/TemplateDbContext.cs`
- Modify: `apps/api/src/Template.Infrastructure/Organizations/OrganizationMemberEntityConfiguration.cs`
- Create: `apps/api/src/Template.Infrastructure/Persistence/Migrations/*_TeamsInvitations.cs`
- Modify generated designer/snapshot.
- Test: `apps/api/tests/Template.Api.Tests/Collaboration/CollaborationPersistenceModelTests.cs`

**Interfaces:**

- Consumes: current `organizations.organizations`, `organizations.members`, `auth.users`, `auth.sessions`.
- Produces: exact `teams`, `team_members`, `invitations` model and tenant-qualified alternate/composite keys.

- [ ] **Step 1: Write failing EF metadata tests**

Assert table/schema, UUID keys, checks, alternate keys, FK delete behavior, partial invitation index metadata, required timestamps and no `active_team_id` property:

```csharp
[Fact]
public void Team_members_use_tenant_qualified_foreign_keys()
{
    var entity = db.Model.FindEntityType(typeof(TeamMemberEntity))!;
    var foreignKeys = entity.GetForeignKeys().Select(fk =>
        fk.Properties.Select(property => property.Name).ToArray()).ToArray();
    Assert.Contains(foreignKeys, value =>
        value.SequenceEqual(["OrganizationId", "TeamId"]));
    Assert.Contains(foreignKeys, value =>
        value.SequenceEqual(["OrganizationId", "OrganizationMemberId"]));
}
```

- [ ] **Step 2: Run the model test and observe RED**

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore --filter FullyQualifiedName~CollaborationPersistenceModelTests
```

- [ ] **Step 3: Implement entities/configurations and DbSets**

Use table names `teams`, `team_members`, `invitations` in schema `organizations`. Add alternate keys `(OrganizationId, Id)` to Team and OrganizationMember. Configure composite cascade FKs for TeamMember and composite restrictive Team FK for Invitation. Configure partial uniqueness with filter `status = 'pending'`; keep the case-insensitive team-name expression index for raw migration SQL.

- [ ] **Step 4: Generate and inspect the migration**

```bash
dotnet tool restore
dotnet ef migrations add TeamsInvitations \
  --project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --startup-project apps/api/src/Template.Api/Template.Api.csproj \
  --context TemplateDbContext \
  --output-dir Persistence/Migrations
```

Edit only the generated target migration to add:

```sql
CREATE UNIQUE INDEX ux_teams_organization_id_lower_name
ON organizations.teams (organization_id, lower(name));
```

and drop that exact index in `Down`. Do not hand-edit the designer or snapshot.

- [ ] **Step 5: Run model, build and drift checks**

```bash
dotnet build Template.sln --no-restore
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore --filter FullyQualifiedName~CollaborationPersistenceModelTests
dotnet ef migrations has-pending-model-changes \
  --project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --startup-project apps/api/src/Template.Api/Template.Api.csproj \
  --context TemplateDbContext
```

Expected: PASS and no pending model changes.

- [ ] **Step 6: Commit**

```bash
git add apps/api/src/Template.Infrastructure apps/api/tests/Template.Api.Tests/Collaboration
git commit -m "feat: add collaboration persistence schema"
```

---

### Task 5: Atomic EF Team Store

**Files:**

- Create: `apps/api/src/Template.Infrastructure/Collaboration/EfTeamStore.cs`
- Modify: `apps/api/src/Template.Infrastructure/Persistence/InfrastructureServiceCollectionExtensions.cs`
- Test: `apps/api/tests/Template.Api.Tests/Collaboration/TeamStoreTests.cs`
- Test: `apps/api/tests/Template.Api.Tests/Collaboration/TeamConcurrencyTests.cs`

**Interfaces:**

- Consumes: Task 2 `ITeamStore`, Task 4 model, current organization permission policy and PostgreSQL fixture.
- Produces: tenant-qualified team pages, candidates, member pages and atomic mutations.

- [ ] **Step 1: Write failing real-PostgreSQL store tests**

Cover member read access, member mutation denial, owner/admin CRUD, Unicode name preservation, case-insensitive duplicate names, stable paging, candidate filtering, cross-organization target rejection, duplicate team membership and delete clearing invitation target. Use `PostgreSqlContainerFixture`; never mock EF.

```csharp
[Fact]
public async Task Cross_organization_membership_is_rejected_without_a_write()
{
    var result = await store.AddMemberAsync(
        new AddTeamMemberCommand(ownerA, organizationA, teamA, userB),
        CancellationToken.None);
    Assert.Equal(TeamFailure.MemberNotFound, result.Failure);
    Assert.Empty(await db.TeamMembers.AsNoTracking().ToListAsync());
}
```

- [ ] **Step 2: Run the focused store tests and observe RED**

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore --filter FullyQualifiedName~TeamStoreTests
```

- [ ] **Step 3: Implement safe reads and projections**

Use `AsNoTracking`, tenant-qualified joins, immutable cursor predicates and `limit + 1`. Team list returns the first 50 members per team and a per-team next member cursor. Candidate search is trimmed case-insensitive name/email filtering within the organization and excludes current team members.

- [ ] **Step 4: Implement mutations with lock/recheck**

Each mutation begins/reuses a transaction, locks organization then actor membership and resource rows, recomputes permission, and saves once. Map only the exact team-name unique index to `NameConflict` and exact team/member unique index to `MemberAlreadyExists`; unrelated database errors propagate.

- [ ] **Step 5: Add deterministic race tests**

Use an interceptor/barrier, not sleeps, to prove create/create same-name has one success, add/add has one success and one classified loser, and rename/delete cannot create an orphan.

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore --filter 'FullyQualifiedName~TeamStoreTests|FullyQualifiedName~TeamConcurrencyTests'
```

- [ ] **Step 6: Run full API test regression and commit**

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj --no-restore
git add apps/api/src/Template.Infrastructure apps/api/tests/Template.Api.Tests/Collaboration
git commit -m "feat: persist team management atomically"
```

---

### Task 6: Atomic Invitation Store and Safe Notifier

**Files:**

- Create: `apps/api/src/Template.Infrastructure/Collaboration/EfInvitationStore.cs`
- Create: `apps/api/src/Template.Infrastructure/Collaboration/NoOpInvitationNotifier.cs`
- Create: `apps/api/src/Template.Infrastructure/Collaboration/SafeInvitationNotifier.cs`
- Modify: `apps/api/src/Template.Infrastructure/Persistence/InfrastructureServiceCollectionExtensions.cs`
- Test: `apps/api/tests/Template.Api.Tests/Collaboration/InvitationStoreTests.cs`
- Test: `apps/api/tests/Template.Api.Tests/Collaboration/InvitationConcurrencyTests.cs`
- Test: `apps/api/tests/Template.Api.Tests/Collaboration/InvitationNotifierTests.cs`
- Modify: organization/account/local cleanup persistence tests.

**Interfaces:**

- Consumes: Task 3 ports/models, Task 4 schema, iteration-5 organization name/domain/role invariants.
- Produces: atomic create/list/decision/accept/reject and no-network safe notifier.

- [ ] **Step 1: Write failing invitation store tests**

Cover 48-hour expiry, organization activity filters, actionable account list, recipient mismatch without projection, unverified/domain-restricted/already-member decision states, duplicate live invite, expired reinvite, role/team validation, actor pending cap and post-commit notifier behavior.

```csharp
[Fact]
public async Task Accept_creates_both_memberships_sets_active_and_marks_accepted()
{
    var result = await store.AcceptAsync(command, CancellationToken.None);
    Assert.True(result.Succeeded);
    Assert.Single(await db.OrganizationMembers.Where(x => x.UserId == invitee).ToListAsync());
    Assert.Single(await db.TeamMembers.Where(x => x.TeamId == teamId).ToListAsync());
    Assert.Equal("accepted", (await db.Invitations.SingleAsync()).Status);
    Assert.Equal(
        organizationId,
        (await db.Sessions.SingleAsync(x => x.Id == sessionId)).ActiveOrganizationId);
}
```

- [ ] **Step 2: Run focused tests and observe RED**

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore --filter FullyQualifiedName~InvitationStoreTests
```

- [ ] **Step 3: Implement list/create/decision**

Normalize recipient email once. Create locks the actor membership, checks role/domain/team/member, cancels expired pending duplicates, counts at most 100 live pending rows for the actor/organization, inserts `InvitationId.New()` with `now + 48h`, and classifies only the partial unique index. Account list filters unexpired pending rows and excludes already-accessible organizations.

- [ ] **Step 4: Implement accept/reject with canonical locks**

Read candidate IDs, then lock affected organizations ascending, target user, invitation/team and ordered membership rows, followed by the existing organization-name advisory key. Re-read every condition. Accept writes membership, optional team membership, invitation status and current unexpired session active organization before one commit. Reject changes only status. Use bounded serialization/deadlock retry and exact failure mapping.

- [ ] **Step 5: Implement notifier adapters**

```csharp
internal sealed class NoOpInvitationNotifier : IInvitationNotifier
{
    public Task<InvitationNotificationOutcome> NotifyCreatedAsync(
        InvitationNotification notification,
        CancellationToken cancellationToken) =>
        Task.FromResult(InvitationNotificationOutcome.Skipped);
}
```

`SafeInvitationNotifier` wraps the registered inner adapter, logs only outcome, catches non-cancellation exceptions as `Failed`, and propagates caller cancellation. It never logs recipient/path.

- [ ] **Step 6: Add deterministic race and cleanup tests**

Prove accept/accept and accept/reject have one winner, team deletion versus accept is fully classified, expiry boundary is deterministic with `TimeProvider`, duplicate/cap races cannot over-insert, and account/local cleanup leaves no collaboration rows.

- [ ] **Step 7: Run focus and commit**

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore --filter 'FullyQualifiedName~Invitation|FullyQualifiedName~OrganizationUserLifecycle'
git add apps/api/src/Template.Infrastructure apps/api/tests/Template.Api.Tests
git commit -m "feat: persist invitation lifecycle atomically"
```

---

### Task 7: Team REST Boundary and Security Audit

**Files:**

- Create: `apps/api/src/Template.Api/Features/Collaboration/TeamContracts.cs`
- Create: `apps/api/src/Template.Api/Features/Collaboration/TeamEndpointModule.cs`
- Create: `apps/api/src/Template.Api/Features/Collaboration/CollaborationEndpointBoundary.cs`
- Create: `apps/api/src/Template.Api/Features/Collaboration/CollaborationSecurityEvents.cs`
- Modify: `apps/api/src/Template.Api/Endpoints/EndpointModuleExtensions.cs`
- Modify: `apps/api/src/Template.Api/ApiHost.cs`
- Modify: `apps/api/src/Template.Api/Features/Organizations/OrganizationContracts.cs`
- Modify: `apps/api/src/Template.Api/Features/Organizations/OrganizationEndpointModule.cs`
- Modify: `apps/api/src/Template.Api/Errors/ApiProblemCodes.cs` and defaults.
- Test: `apps/api/tests/Template.Api.Tests/Collaboration/TeamEndpointTests.cs`
- Test: `apps/api/tests/Template.Api.Tests/Collaboration/CollaborationSecurityTests.cs`

**Interfaces:**

- Consumes: Task 2 service/outcomes and existing actor/session claim conventions.
- Produces: eight versioned team operations, extended organization capabilities and safe audit.

- [ ] **Step 1: Write failing endpoint/security tests**

For every route prove 401, unsafe CSRF, no-store, strict JSON, canonical route UUID, permission matrix, safe errors and one audit after actor resolution. Include malformed/overflow limits and query >100 before store reach.

```csharp
[Fact]
public async Task Member_cannot_create_a_team_and_problem_is_safe()
{
    using var client = await factory.CreateAuthenticatedClientAsync(member);
    using var response = await client.PostWithCsrfAsync(
        $"/api/v1/organizations/{organizationId:D}/teams",
        new { name = "Design" });
    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    await response.AssertProblemAsync("team_permission_denied");
    Assert.DoesNotContain("Design", await response.Content.ReadAsStringAsync());
}
```

- [ ] **Step 2: Run focused endpoint tests and observe RED**

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore --filter 'FullyQualifiedName~TeamEndpointTests|FullyQualifiedName~CollaborationSecurityTests'
```

- [ ] **Step 3: Define strict contracts and mappings**

Use `[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]` for `{name}` and `{userId}` requests. Responses expose exact Application projections and canonical strings. Extend organization capabilities with `CanManageTeams` and `CanManageInvitations` in HTTP contracts/mapping.

- [ ] **Step 4: Implement the shared boundary and team module**

`CollaborationEndpointBoundary` resolves authenticated `UserId`/`SessionId`, parses route UUIDs by exact `D` round-trip after actor resolution, validates raw limit/query, audits `ApiValidationException`/`invalid_request` once, and excludes raw input. Map Team failures to approved codes/statuses; every unsafe route uses `RequireApiAntiforgery()`.

- [ ] **Step 5: Register services/module and run tests**

```bash
dotnet build Template.sln --no-restore
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore --filter 'FullyQualifiedName~TeamEndpointTests|FullyQualifiedName~CollaborationSecurityTests|FullyQualifiedName~OrganizationEndpointTests'
```

- [ ] **Step 6: Commit**

```bash
git add apps/api/src/Template.Api apps/api/tests/Template.Api.Tests/Collaboration
git commit -m "feat: expose team REST operations"
```

---

### Task 8: Invitation REST, Rate Limits and Local Email Confirmation

**Files:**

- Create: `apps/api/src/Template.Api/Features/Collaboration/InvitationContracts.cs`
- Create: `apps/api/src/Template.Api/Features/Collaboration/InvitationEndpointModule.cs`
- Modify: collaboration boundary/security event files.
- Modify: `apps/api/src/Template.Api/Authentication/AuthSecurityServiceCollectionExtensions.cs`
- Modify: `apps/api/src/Template.Api/ApiHost.cs`
- Modify: Auth contracts/endpoints and local-only OpenAPI metadata.
- Modify: `apps/api/src/Template.Application/Authentication/Ports/ILocalIdentityGateway.cs`
- Modify: `apps/api/src/Template.Application/Authentication/LocalAutomationAuthService.cs`
- Modify: `apps/api/src/Template.Infrastructure/Identity/IdentityGateway.cs`
- Modify Problem codes/defaults and DI/module registration.
- Test: `apps/api/tests/Template.Api.Tests/Collaboration/InvitationEndpointTests.cs`
- Test: `apps/api/tests/Template.Api.Tests/Collaboration/InvitationRateLimitTests.cs`
- Test: local auth Application/API/Identity tests.

**Interfaces:**

- Consumes: Task 3 service, Task 6 store/notifier, existing local auth/session renewal.
- Produces: organization/account/decision/accept/reject endpoints, user-partitioned limits, `POST /api/local-auth/confirm-email`.

- [ ] **Step 1: Write failing invitation HTTP tests**

Cover strict create body, admin/owner/member matrix, owner-role assignment, domain/team/duplicate/cap outcomes, activity filter/cursor, account list, decision states, recipient mismatch without payload, verified requirement and accept/reject response/CSRF/no-store.

- [ ] **Step 2: Write failing local-confirmation tests**

```csharp
[Fact]
public async Task Local_confirmation_updates_email_and_current_ticket()
{
    using var client = await factory.CreateLocalScenarioClientAsync();
    using var response = await client.PostWithCsrfAsync(
        "/api/local-auth/confirm-email",
        body: null);
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var session = await client.GetSessionAsync();
    Assert.True(session.User!.EmailVerified);
}
```

Also prove anonymous 401, non-local 403, missing CSRF, Production 404, and disabled flag 404.

- [ ] **Step 3: Run focused tests and observe RED**

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore --filter 'FullyQualifiedName~InvitationEndpointTests|FullyQualifiedName~InvitationRateLimitTests|FullyQualifiedName~LocalAutomation'
```

- [ ] **Step 4: Implement invitation contracts/module**

Create strict `{email, role, teamId?}` request and exact page/decision responses. Map `invitationPath` from the committed ID. Decision mismatch maps to 403 without response projection. Apply CSRF and named limiter to create/accept/reject only; lists/detail remain authenticated/no-store.

- [ ] **Step 5: Implement user-partitioned rate policies**

Move `app.UseAuthentication()` before `app.UseRateLimiter()` and keep authorization after both. Add policies `InvitationCreate` (20/1 minute) and `InvitationDecision` (30/1 minute), queue 0, keyed by the single canonical `ClaimTypes.NameIdentifier`; before authorization rejects a missing/invalid claim, its partition falls back to the remote IP so unrelated authenticated users can never merge into one limiter bucket.

- [ ] **Step 6: Implement local email confirmation**

Add `ILocalIdentityGateway.ConfirmEmailAsync(UserId, CancellationToken)`. `LocalAutomationAuthService.ConfirmEmailAsync` requires the current local-automation user, executes identity update and `IBrowserSessionGateway.RenewCurrentAsync` inside the application unit of work, and returns refreshed `SessionState`. Map the route with browser auth, CSRF, `WithLocalOnly()` and no-store.

- [ ] **Step 7: Run focused/full API tests and commit**

```bash
dotnet test apps/api/tests/Template.Application.Tests/Template.Application.Tests.csproj --no-restore
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj --no-restore
git add apps/api
git commit -m "feat: expose secure invitation lifecycle"
```

---

### Task 9: OpenAPI Contract and Generated TypeScript SDK

**Files:**

- Create: `apps/api/src/Template.Api/OpenApi/CollaborationContractSchemaTransformer.cs`
- Create: `apps/api/src/Template.Api/OpenApi/CollaborationContractOperationTransformer.cs`
- Modify: `apps/api/src/Template.Api/OpenApi/OpenApiServiceCollectionExtensions.cs`
- Modify: `apps/api/src/Template.Api/OpenApi/ApiContractSchemaTransformer.cs`
- Modify: `apps/api/tests/Template.Api.Tests/OpenApiContractTests.cs`
- Modify: `contracts/openapi/v1.json`
- Regenerate: `apps/web/src/lib/api/generated/**`
- Test: `apps/web/test/contracts/generated-sdk.test.ts`

**Interfaces:**

- Consumes: Tasks 7–8 endpoint names/contracts.
- Produces: deterministic OpenAPI 3.1 and generated functions/types for every collaboration/local-only operation.

- [ ] **Step 1: Write failing exact OpenAPI assertions**

Assert paths, operation IDs, cookie security, CSRF headers, no-store response surface, required Problem codes/statuses, strict `additionalProperties: false`, UUID patterns, role/status enums, query bounds, 100-character search max, invitation path and `x-local-only` confirm-email metadata.

- [ ] **Step 2: Run OpenAPI tests and observe RED**

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore --filter FullyQualifiedName~OpenApiContractTests
```

- [ ] **Step 3: Implement schema/operation transformers**

Register both transformers. Publish limit parameters as integer/int32 minimum 1 maximum 100 default 50 even when raw strings are used internally. Publish candidate query maxLength 100, invitation filter enum, exact response unions and local-only extension.

- [ ] **Step 4: Export twice and prove determinism**

```bash
dotnet build apps/api/src/Template.Api/Template.Api.csproj --no-restore \
  -p:OpenApiGenerateDocuments=true
cp contracts/openapi/v1.json /tmp/iteration6-openapi-first.json
rm -f contracts/openapi/v1.json
dotnet build apps/api/src/Template.Api/Template.Api.csproj --no-restore \
  -p:OpenApiGenerateDocuments=true
cmp /tmp/iteration6-openapi-first.json contracts/openapi/v1.json
shasum -a 256 contracts/openapi/v1.json
```

- [ ] **Step 5: Generate SDK and run contract tests**

```bash
cd apps/web
npm run api:generate
npm run api:check
npm test -- --runInBand test/contracts/generated-sdk.test.ts
cd ../..
```

Update the generated contract test with exact operation imports and ensure generated files have no manual edits.

- [ ] **Step 6: Commit**

```bash
git add apps/api/src/Template.Api/OpenApi \
  apps/api/tests/Template.Api.Tests/OpenApiContractTests.cs \
  contracts/openapi apps/web/src/lib/api/generated \
  apps/web/test/contracts/generated-sdk.test.ts
git commit -m "feat: publish collaboration REST contract"
```

---

### Task 10: Web REST Adapters, Routes and Navigation Contract

**Files:**

- Create: `apps/web/src/features/collaboration/collaboration-routes.ts`
- Create SSR loaders under `apps/web/src/lib/api/collaboration/server/`.
- Create: `apps/web/src/lib/api/collaboration/browser/collaboration-mutations.ts`
- Modify: organization/account routes and nav components.
- Modify: `apps/web/src/components/organizations/organization-onboarding.tsx`
- Create: `apps/web/src/messages/collaboration.en.json`
- Create: `apps/web/src/messages/collaboration.ru.json`
- Modify: `apps/web/src/i18n/messages.ts` and typings.
- Test: `apps/web/test/features/collaboration-routes.test.ts`
- Test: `apps/web/test/lib/api/collaboration-api.test.ts`
- Modify nav/onboarding/i18n tests.

**Interfaces:**

- Consumes: Task 9 generated functions/types and existing server/browser client factories.
- Produces: typed routes, SSR reads, CSRF mutations and navigation visibility inputs.

- [ ] **Step 1: Read installed Next.js documentation**

```bash
sed -n '1,240p' apps/web/node_modules/next/dist/docs/01-app/01-getting-started/03-layouts-and-pages.md
sed -n '1,280p' apps/web/node_modules/next/dist/docs/01-app/01-getting-started/05-server-and-client-components.md
sed -n '1,260p' apps/web/node_modules/next/dist/docs/01-app/01-getting-started/06-fetching-data.md
sed -n '1,260p' apps/web/node_modules/next/dist/docs/01-app/01-getting-started/10-error-handling.md
sed -n '1,220p' apps/web/node_modules/next/dist/docs/01-app/02-guides/redirecting.md
```

Record version-specific constraints in task notes and follow the existing dynamic rendering/`connection()` patterns.

- [ ] **Step 2: Write failing route/adapter/boundary tests**

Assert encoded organization keys, exact invitation ID path, only generated SDK functions, SSR header allow-list with renewal suppression, browser CSRF-first ordering and stable failure normalization. Add boundary-test fixtures that fail on raw `fetch` or a handwritten collaboration DTO.

- [ ] **Step 3: Run focused tests and observe RED**

```bash
cd apps/web
npm test -- --runInBand \
  test/features/collaboration-routes.test.ts \
  test/lib/api/collaboration-api.test.ts
npm run boundaries:check
```

- [ ] **Step 4: Implement exact route builders and SSR loaders**

Expose `settingsTeams(key)`, `settingsInvitations(key)`, `accountInvitations` and `invitationDecision(id)`. Server loaders call generated team/invitation operations through `createServerApiClient`, forward only approved headers and return `ApiResult<T>`.

- [ ] **Step 5: Implement CSRF browser mutations and navigation**

Use `runCsrfMutation` around generated create/update/delete/add/remove/accept/reject functions. Add Teams to every organization settings nav, Invitations only when `canManageInvitations`, Account Invitations to account nav, and Review Invitations to onboarding.

- [ ] **Step 6: Add complete en/ru messages and run web focus**

```bash
npm test -- --runInBand \
  test/features/collaboration-routes.test.ts \
  test/lib/api/collaboration-api.test.ts \
  test/components/account-nav.test.tsx \
  test/components/organization-onboarding.test.tsx \
  test/i18n/messages.test.ts
npm run boundaries:check
cd ../..
```

- [ ] **Step 7: Commit**

```bash
git add apps/web/src/features apps/web/src/lib/api \
  apps/web/src/components/account apps/web/src/components/organizations \
  apps/web/src/messages apps/web/src/i18n apps/web/test apps/web/scripts
git commit -m "feat: add collaboration web adapters"
```

---

### Task 11: Team Settings UI

**Files:**

- Create focused Team components under `apps/web/src/components/collaboration/`.
- Add pages/loading/error under `apps/web/src/app/(site)/w/[organizationKey]/settings/teams/`.
- Add matching `@organizationSwitcher` parallel-slot page.
- Test: `apps/web/test/app/team-settings-pages.test.tsx`
- Test: `apps/web/test/components/team-directory.test.tsx`
- Test: `apps/web/test/components/team-controls.test.tsx`

**Interfaces:**

- Consumes: Task 10 routes/loaders/mutations and generated Team response types.
- Produces: canonical SSR team page, read-only member view, manager CRUD/member controls and pagination recovery.

- [ ] **Step 1: Write failing page/component tests**

Cover canonical-key redirect, first-page SSR, member read-only controls, admin/owner create/rename/delete/add/remove, candidate search, no active-team copy/control, nested pagination, and confirmed mutation plus refresh-failure recovery.

```tsx
expect(screen.queryByRole("button", { name: /create team/i })).not.toBeInTheDocument();
expect(screen.queryByText(/active team|set active|clear active/i)).not.toBeInTheDocument();
expect(screen.getByRole("region", { name: "Workspace teams" })).toBeVisible();
```

- [ ] **Step 2: Run focused Jest and observe RED**

```bash
cd apps/web
npm test -- --runInBand \
  test/app/team-settings-pages.test.tsx \
  test/components/team-directory.test.tsx \
  test/components/team-controls.test.tsx
```

- [ ] **Step 3: Implement SSR page and safe identity keys**

Follow existing settings page auth/organization/canonical redirect. Key every stateful Team directory/card by immutable organization/team ID so mutable slug/name reuse cannot retain old transport identity. Render initial server data only from Task 10 loaders.

- [ ] **Step 4: Implement test-proven client controls**

Use focused components for create, rename, delete confirmation, member candidate search and add/remove. Each mutation prevents duplicate submission, branches only on stable code, confirms saved state before refresh, and makes post-unmount/Activity-hidden continuations inert using the established attachment/visibility pattern where global navigation or refresh occurs.

- [ ] **Step 5: Run focused/static tests**

```bash
npm test -- --runInBand \
  test/app/team-settings-pages.test.tsx \
  test/components/team-directory.test.tsx \
  test/components/team-controls.test.tsx \
  test/app/organization-settings-pages.test.tsx
npm run format:check
npm run lint
npm run typecheck
cd ../..
```

- [ ] **Step 6: Commit**

```bash
git add apps/web/src/app apps/web/src/components/collaboration \
  apps/web/test/app apps/web/test/components
git commit -m "feat: build team settings UI"
```

---

### Task 12: Invitation Settings, Account List and Decision UI

**Files:**

- Create Invitation components under `apps/web/src/components/collaboration/`.
- Add settings/invitations pages/loading/error and switcher slot.
- Add `/user/invitations` pages/loading/error and switcher slot.
- Add `/invite/[invitationId]` pages/loading/error and switcher slot/default required by the current parallel route.
- Modify account/organization layout tests and onboarding.
- Test invitation page/component suites under `apps/web/test/`.

**Interfaces:**

- Consumes: Task 10 adapters/routes, generated invitation types and existing organization/account shells.
- Produces: permission-gated activity/create UI, actionable account list and secure decision flow.

- [ ] **Step 1: Write failing settings/list/decision tests**

Cover member forbidden state, admin/owner activity and status filter, strict create validation, workspace-only/team-targeted link, copy affordance, pagination, account empty/list, mismatch/no-detail, verification/domain/expired/already-member/accepted/rejected/canceled states, accept redirect and reject local state.

- [ ] **Step 2: Write failing lifecycle-safety tests**

Use deferred promises and keyed replacement/React Activity to prove a completed accept navigates once only for the still-attached same invitation, reject refresh cannot update a different invitation instance, and a saved mutation with failed refresh displays partial success without re-sending POST.

- [ ] **Step 3: Run focused Jest and observe RED**

```bash
cd apps/web
npm test -- --runInBand \
  test/app/invitation-pages.test.tsx \
  test/components/invitation-activity.test.tsx \
  test/components/invitation-create-dialog.test.tsx \
  test/components/invitation-decision.test.tsx
```

- [ ] **Step 4: Implement settings and account pages**

Use SSR-generated loaders, canonical organization redirects and existing forbidden behavior. Create dialog submits normalized inputs through generated mutation, then derives the absolute copy value with `new URL(invitationPath, window.location.origin)`. Never log or persist the link in browser storage.

- [ ] **Step 5: Implement decision flow**

Render server state by stable enum/code. Accept uses generated POST and redirects to the canonical organization dashboard path returned by API; reject commits local rejected state. Capture immutable invitation ID per mutation and guard all post-await state/router effects against deletion, different-ID replacement and stale pathname generation.

- [ ] **Step 6: Run collaboration/full Jest and static checks**

```bash
npm test -- --runInBand \
  test/app/invitation-pages.test.tsx \
  test/components/invitation-activity.test.tsx \
  test/components/invitation-create-dialog.test.tsx \
  test/components/invitation-decision.test.tsx \
  test/components/account-nav.test.tsx \
  test/components/organization-onboarding.test.tsx
npm test -- --runInBand
npm run boundaries:check
npm run format:check
npm run lint
npm run typecheck
cd ../..
```

- [ ] **Step 7: Commit**

```bash
git add apps/web/src apps/web/test
git commit -m "feat: build invitation workflows"
```

---

### Task 13: Deterministic Multi-User Playwright Acceptance

**Files:**

- Create: `apps/web/e2e/collaboration.spec.ts`
- Create: `apps/web/e2e/support/generated-collaboration-api.ts`
- Modify: `apps/web/e2e/support/generated-auth-api.ts`
- Modify: `apps/web/e2e/support/organization-test-fixture.ts` or create a composition fixture.
- Test: `apps/web/test/e2e/collaboration-e2e-harness.test.ts`

**Interfaces:**

- Consumes: generated local-confirm/team/invitation operations and current teardown registry.
- Produces: black-box browser evidence with no direct SQL/database setup.

- [ ] **Step 1: Write failing harness unit tests**

Prove cleanup registration before creation, exact-once organization accounting, confirm-email call through generated SDK, multi-context cleanup after partial failure, and absence of raw fetch/SQL.

- [ ] **Step 2: Implement generated-only E2E helpers**

Add functions for local confirmation, create/update/delete team, add/remove team member, create invitation, and read IDs/paths using Task 9 generated functions plus CSRF helper. The helper must reject any origin other than the configured same-origin web URL.

- [ ] **Step 3: Add team Playwright scenario**

Owner creates organization; second local user joins through existing direct-add API; owner creates/renames team, adds/removes member and deletes team; member context proves read-only composition and absence of active-team controls.

- [ ] **Step 4: Add invitation settings scenario**

Owner and admin create invitations; member is denied; duplicate and outside-domain creates return visible safe errors; team-targeted create displays/copies the correct `/invite/{uuid}` path.

- [ ] **Step 5: Add accept/reject and zero-workspace scenario**

Create two local recipients, confirm email through `POST /api/local-auth/confirm-email`, accept one team-targeted invitation and reject another. Prove account/onboarding list, canonical accept redirect, organization/team membership visibility and rejected read-only state.

- [ ] **Step 6: Run focused and full E2E**

```bash
cd apps/web
npm test -- --runInBand test/e2e/collaboration-e2e-harness.test.ts
npx playwright test e2e/collaboration.spec.ts --workers=1
npm run e2e
cd ../..
```

Expected: collaboration focus and full deterministic suite pass; live external-provider smoke remains opt-in/skipped.

- [ ] **Step 7: Commit**

```bash
git add apps/web/e2e apps/web/test/e2e apps/api/tests/Template.E2EHost
git commit -m "test: cover collaboration workflows end to end"
```

---

### Task 14: Durable Documentation and Complete Verification

**Files:**

- Modify: `docs/api-conventions.md`
- Modify: `docs/web-conventions.md`
- Modify: `docs/authentication-persistence-operations.md`
- Modify: `docs/aspnetcore-migration-plan.md`
- Modify only implementation/tests required by failures exposed during full verification.

**Interfaces:**

- Consumes: completed Tasks 1–13.
- Produces: iteration-6 status/evidence and a clean, reproducible branch.

- [ ] **Step 1: Update durable conventions before final gates**

Record exact REST table, permission matrix, validation/errors, cursor/filter contract, 48-hour lifecycle, PostgreSQL keys/locks, notification semantics, local confirmation boundary, UI recovery and intentional differences. Update the iteration register to iteration 6 implemented but do not claim review completion before it is observed.

- [ ] **Step 2: Run required .NET gates**

```bash
dotnet restore Template.sln
dotnet build Template.sln --no-restore
dotnet test Template.sln --no-restore
dotnet format Template.sln --no-restore --verify-no-changes
dotnet list Template.sln package --vulnerable --include-transitive
```

- [ ] **Step 3: Verify EF migration from a clean database**

```bash
dotnet ef migrations has-pending-model-changes \
  --project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --startup-project apps/api/src/Template.Api/Template.Api.csproj \
  --context TemplateDbContext
dotnet ef migrations script --idempotent \
  --project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --startup-project apps/api/src/Template.Api/Template.Api.csproj \
  --context TemplateDbContext --output /tmp/template-iteration6.sql
test -s /tmp/template-iteration6.sql
rg 'teams|team_members|invitations|ux_teams_organization_id_lower_name' \
  /tmp/template-iteration6.sql
```

Apply migrations to a fresh PostgreSQL 18.4 Testcontainer through the existing fixture/test and inspect all new FK/index/check names.

- [ ] **Step 4: Prove deterministic contract/generated state**

```bash
dotnet build apps/api/src/Template.Api/Template.Api.csproj --no-restore \
  -p:OpenApiGenerateDocuments=true
cp contracts/openapi/v1.json /tmp/iteration6-contract-a.json
rm contracts/openapi/v1.json
dotnet build apps/api/src/Template.Api/Template.Api.csproj --no-restore \
  -p:OpenApiGenerateDocuments=true
cmp /tmp/iteration6-contract-a.json contracts/openapi/v1.json
shasum -a 256 contracts/openapi/v1.json
cd apps/web && npm run api:check && cd ../..
```

- [ ] **Step 5: Run complete web/security gates**

```bash
cd apps/web
npm audit --omit=dev
npm run boundaries:check
npm run format:check
npm run lint
npm run typecheck
npm test -- --runInBand
rm -rf .next
npm run build
test -f .next/standalone/server.js
npm run e2e
cd ../..
```

Run full `npm audit` separately and record development-only advisories without conflating them with production audit.

- [ ] **Step 6: Run repository/reference guards**

```bash
git diff --check
git diff --quiet -- template/
git diff --quiet origin/main...HEAD -- template/
test -z "$(git status --short -- template/)"
test -z "$(git ls-files --others --exclude-standard -- template/)"
test -z "$(find openspec/changes -mindepth 1 -maxdepth 1 ! -name archive -print 2>/dev/null)"
test -z "$(find openspec/specs -type f ! -name '.gitkeep' -print 2>/dev/null)"
git status --short --branch
```

- [ ] **Step 7: Record exact observed evidence and commit**

Write actual test counts, contract hash, migration script size, E2E results, audit results and known differences into the migration plan. Do not predict a future review result.

```bash
git add docs apps contracts Directory.Packages.props
git commit -m "docs: complete teams and invitations iteration"
```

---

### Task 15: Ready PR and Automatic Review Loop

**Files:**

- Modify only files required by actionable review findings.
- Append observed review-round evidence to `docs/aspnetcore-migration-plan.md` when behavior or verification changes.

**Interfaces:**

- Consumes: green Task 14 branch and repository automatic reviewer.
- Produces: pushed ready PR whose latest reviewed head has no actionable comments and whose threads are resolved.

- [ ] **Step 1: Perform final branch review before push**

```bash
git status --short --branch
git log --oneline origin/main..HEAD
git diff --stat origin/main...HEAD
git diff --exit-code origin/main...HEAD -- template/
```

Expected: clean tree, intentional commits, no reference diff.

- [ ] **Step 2: Push and create a ready PR**

Create `/tmp/iteration6-pr-body.md` containing the reference mapping table, REST/security/transaction summary, exact acceptance results, intentional differences and out-of-scope list, then run:

```bash
git push -u origin codex/iteration-6-teams-invitations
gh pr create --base main --head codex/iteration-6-teams-invitations \
  --title "Implement teams and invitations" \
  --body-file /tmp/iteration6-pr-body.md
```

Do not pass `--draft`; verify `isDraft=false`.

- [ ] **Step 3: Wait for and inspect automatic review**

Use the connected GitHub integration or `gh` to read issue/review comments, review threads, head SHA and checks. Wait for configured automatic reviewer; never self-post a fake review. Classify each comment with code/test evidence.

- [ ] **Step 4: Fix every actionable finding test-first**

For a behavior defect, add the smallest failing regression test, observe RED, implement the minimum fix, then run the focused suite and every Task 14 gate affected by the change. For documentation-only findings, run formatting, path/link, diff and immutable-reference guards. Reject only suggestions that conflict with approved design, with evidence in the reply.

- [ ] **Step 5: Commit, push, resolve and request a fresh round**

```bash
git diff --name-only --diff-filter=ACMR
git add -- $(git diff --name-only --diff-filter=ACMR)
git commit -m "fix: address collaboration review findings"
git push
```

Reply to and resolve each fixed thread through GitHub, then wait for automatic review of the new head. Repeat Steps 3–5 until the latest review reports no actionable comments.

- [ ] **Step 6: Record and verify the final clean reviewed state**

Record only observed reviewed head, automatic-review comment, resolved/unresolved thread counts, ready/mergeable state and checks. If this evidence changes tracked docs, commit/push it and require one more automatic review of that documentation head. Finish with:

```bash
git diff --check
git diff --exit-code origin/main...HEAD -- template/
git status --short --branch
gh pr view --json number,url,isDraft,state,mergeable,mergeStateStatus,headRefOid,statusCheckRollup
```

The task is complete only when the final pushed head itself has a fresh clean automatic review and zero unresolved actionable threads.

---

## Spec Coverage Self-Check

| Design requirement | Implementing tasks |
| --- | --- |
| Domain/Application boundaries | 1–3 |
| Shared DbContext schema and tenant FKs | 4 |
| Atomic Team behavior and races | 5 |
| Invitation lifecycle, 48h expiry, notifier and cleanup | 6 |
| REST, auth, CSRF, validation, errors and audit | 7–8 |
| Rate limits and local-only confirmation | 8 |
| OpenAPI/generated SDK | 9 |
| REST-only routes/loaders/navigation | 10 |
| Team UI | 11 |
| Invitation/account/decision UI | 12 |
| Multi-user E2E | 13 |
| Durable docs and all acceptance gates | 14 |
| Ready PR and clean automatic-review loop | 15 |

Execution mode is fixed by the user: use `superpowers:subagent-driven-development`, dispatch a fresh implementation subagent for each task, perform two-stage review between tasks, and keep controller ownership of integration, final gates, push, PR and review-thread state.
