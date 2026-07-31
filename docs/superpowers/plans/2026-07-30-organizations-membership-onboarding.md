# Organizations, Membership, and Onboarding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver iteration 5 as a test-first vertical slice in which authenticated users create, select, route through, configure, and manage built-in membership roles for organization-backed workspaces through ASP.NET Core REST and a generated-SDK Next.js UI.

**Architecture:** Rename the existing persistence boundary to one `TemplateDbContext`, keep authentication tables in schema `auth`, add organization tables in schema `organizations`, and store the active organization as a nullable relational preference on the current persistent session. Domain policies own slug/domain/role rules; Application services coordinate use cases through atomic Infrastructure ports; API owns validation/authorization/Problem Details/OpenAPI; Next.js owns presentation only.

**Tech Stack:** .NET SDK 10.0.302, ASP.NET Core/EF Core 10.0.10, Npgsql 10.0.3, PostgreSQL 18.4 Testcontainers, xUnit v3, OpenAPI 3.1, Next.js 16.2.11, React 19.2.8, TypeScript 6.0.3, next-intl 4.13.4, generated `@hey-api/openapi-ts` 0.99.0 SDK, Jest 30.4.2, Playwright 1.61.1.

## Global Constraints

- `template/` is immutable reference material: never edit, format, move, delete, or run migrations inside it.
- Do not create an OpenSpec change/spec.
- Dependencies remain `Api → Application`, `Infrastructure → Application/Domain`, `Application → Domain`; Domain has no HTTP/EF dependency.
- ASP.NET Core owns every `/api/**` route, business rule, authorization decision, database write, and external integration.
- Next.js uses generated REST SDK functions only; no Prisma, Better Auth, Server Actions, direct database access, handwritten transport DTOs, or browser-stored bearer tokens.
- Browser authentication remains `__Host-template.session`, secure HttpOnly same-origin cookie; every unsafe browser request obtains a fresh CSRF token.
- Organization roles are product-domain `owner|admin|member`, never ASP.NET Identity roles or session claims.
- Direct member removal, invitations, teams, API keys, custom roles, product dashboard parity, Redis, Aspire, YARP, and production deployment remain outside iteration 5.
- All organization/membership responses are `Cache-Control: no-store`; logs never contain organization names, emails, allowed domains, request bodies, cookies, or cursor values.
- Write a failing focused test before each behavior, observe RED for the intended reason, implement the minimum behavior, then observe GREEN.
- Before completion run `dotnet restore Template.sln`, `dotnet build Template.sln --no-restore`, and `dotnet test Template.sln --no-restore` plus the contract/web/E2E gates in Task 13.
- Before and after every task, `git diff -- template/` and `git status --short -- template/` must be empty.

---

## File Structure

### Domain and Application

- `apps/api/src/Template.Domain/Organizations/OrganizationId.cs` — UUID organization value.
- `apps/api/src/Template.Domain/Organizations/OrganizationMemberId.cs` — UUID membership value.
- `apps/api/src/Template.Domain/Organizations/OrganizationSlug.cs` — user slug validation and generated-slug base.
- `apps/api/src/Template.Domain/Organizations/OrganizationRole.cs` — closed role value.
- `apps/api/src/Template.Domain/Organizations/OrganizationPermissionPolicy.cs` — capabilities and assignability.
- `apps/api/src/Template.Domain/Organizations/OrganizationEmailDomainPolicy.cs` — domain normalization and eligibility.
- `apps/api/src/Template.Application/Common/Ports/IApplicationUnitOfWork.cs` — renamed cross-domain transaction port.
- `apps/api/src/Template.Application/Organizations/OrganizationModels.cs` — projections, commands, pages, outcomes.
- `apps/api/src/Template.Application/Organizations/OrganizationCursor.cs` — opaque cursor codec.
- `apps/api/src/Template.Application/Organizations/Ports/IOrganizationStore.cs` — atomic organization/membership persistence port.
- `apps/api/src/Template.Application/Organizations/Ports/IOrganizationUserLifecycleStore.cs` — organization-aware user cleanup port.
- `apps/api/src/Template.Application/Organizations/OrganizationService.cs` — organization/context use cases.
- `apps/api/src/Template.Application/Organizations/OrganizationMembershipService.cs` — member use cases.

### Infrastructure

- Move `Persistence/AuthDbContext.cs` → `Persistence/TemplateDbContext.cs`.
- Move `Persistence/AuthDbContextFactory.cs` → `Persistence/TemplateDbContextFactory.cs`.
- Move `Persistence/EfAuthenticationUnitOfWork.cs` → `Persistence/EfApplicationUnitOfWork.cs`.
- Move `Persistence/Migrations/AuthDbContextModelSnapshot.cs` → `Persistence/Migrations/TemplateDbContextModelSnapshot.cs`.
- `apps/api/src/Template.Infrastructure/Organizations/OrganizationEntity.cs` — organization row.
- `apps/api/src/Template.Infrastructure/Organizations/OrganizationMemberEntity.cs` — membership row.
- `apps/api/src/Template.Infrastructure/Organizations/OrganizationAllowedEmailDomainEntity.cs` — normalized policy row.
- `apps/api/src/Template.Infrastructure/Organizations/OrganizationEntityConfiguration.cs` — organization mapping.
- `apps/api/src/Template.Infrastructure/Organizations/OrganizationMemberEntityConfiguration.cs` — membership mapping.
- `apps/api/src/Template.Infrastructure/Organizations/OrganizationAllowedEmailDomainEntityConfiguration.cs` — domain mapping.
- `apps/api/src/Template.Infrastructure/Organizations/EfOrganizationStore.cs` — tenant-qualified reads and atomic writes.
- `apps/api/src/Template.Infrastructure/Organizations/EfOrganizationUserLifecycleStore.cs` — account/local cleanup rules.
- `apps/api/src/Template.Infrastructure/Persistence/Migrations/*_OrganizationsMembershipOnboarding.cs` — one additive generated migration and designer.

### API and contract

- `apps/api/src/Template.Api/Features/Organizations/OrganizationContracts.cs` — strict HTTP request/response records.
- `apps/api/src/Template.Api/Features/Organizations/OrganizationEndpointModule.cs` — nine versioned operations.
- `apps/api/src/Template.Api/Features/Organizations/OrganizationSecurityEvents.cs` — safe structured outcomes.
- `apps/api/src/Template.Api/OpenApi/OrganizationContractSchemaTransformer.cs` — role, validation, cursor and projection schemas.
- `apps/api/src/Template.Api/OpenApi/OrganizationContractOperationTransformer.cs` — operation-specific responses and pagination metadata.
- Modify auth session contracts/endpoints, Problem codes/details, endpoint registration, service registration, readiness, and `contracts/openapi/v1.json`.

### Web

- `apps/web/src/features/organizations/organization-routes.ts` — typed route builders.
- `apps/web/src/features/organizations/organization-switch-navigation.ts` — route-preserving switch logic.
- `apps/web/src/lib/api/browser/run-csrf-mutation.ts` — shared CSRF-first generated-SDK mutation helper.
- `apps/web/src/lib/api/organizations/server/load-organizations.ts` — paged organization SSR read.
- `apps/web/src/lib/api/organizations/server/load-organization.ts` — route-key detail read.
- `apps/web/src/lib/api/organizations/server/load-organization-members.ts` — member page read.
- `apps/web/src/lib/api/organizations/browser/organization-mutations.ts` — generated-SDK mutations.
- `apps/web/src/components/organizations/*` — focused onboarding/list/switch/settings/member components.
- Add routes under `apps/web/src/app/(site)/welcome`, `workspaces`, and `w/[organizationKey]`.
- `apps/web/src/messages/organizations.{en,ru}.json` — fixed locale copy.

### Tests and durable docs

- Application tests under `apps/api/tests/Template.Application.Tests/Organizations/`.
- PostgreSQL/API tests under `apps/api/tests/Template.Api.Tests/Organizations/`.
- Web Jest tests under `apps/web/test/{app,components,features,lib/api}/organizations*`.
- `apps/web/e2e/organizations.spec.ts` and generated-only E2E helper updates.
- Modify `docs/api-conventions.md`, `docs/web-conventions.md`, `docs/authentication-persistence-operations.md`, and `docs/aspnetcore-migration-plan.md`.

---

### Task 1: Generalize the Persistence and Transaction Boundary

**Files:**

- Move: `apps/api/src/Template.Infrastructure/Persistence/AuthDbContext.cs` → `apps/api/src/Template.Infrastructure/Persistence/TemplateDbContext.cs`
- Move: `apps/api/src/Template.Infrastructure/Persistence/AuthDbContextFactory.cs` → `apps/api/src/Template.Infrastructure/Persistence/TemplateDbContextFactory.cs`
- Move: `apps/api/src/Template.Infrastructure/Persistence/EfAuthenticationUnitOfWork.cs` → `apps/api/src/Template.Infrastructure/Persistence/EfApplicationUnitOfWork.cs`
- Move: `apps/api/src/Template.Application/Authentication/Ports/IAuthenticationUnitOfWork.cs` → `apps/api/src/Template.Application/Common/Ports/IApplicationUnitOfWork.cs`
- Move: `apps/api/src/Template.Infrastructure/Persistence/Migrations/AuthDbContextModelSnapshot.cs` → `apps/api/src/Template.Infrastructure/Persistence/Migrations/TemplateDbContextModelSnapshot.cs`
- Modify: every current source/test reference returned by `rg -l 'AuthDbContext|AuthDbContextFactory|EfAuthenticationUnitOfWork|IAuthenticationUnitOfWork' apps/api --glob '!**/bin/**' --glob '!**/obj/**'`
- Test: `apps/api/tests/Template.Api.Tests/ArchitectureBoundaryTests.cs`

**Interfaces:**

- Consumes: the current EF model and `ExecuteAsync<T>(Func<CancellationToken,Task<T>>, CancellationToken)` behavior.
- Produces: `TemplateDbContext`, `TemplateDbContextFactory`, `IApplicationUnitOfWork`, and `EfApplicationUnitOfWork` with unchanged database model and transaction semantics.

- [x] **Step 1: Capture the pre-rename model baseline**

Run:

```bash
dotnet tool restore
dotnet restore Template.sln
dotnet ef migrations has-pending-model-changes \
  --project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --startup-project apps/api/src/Template.Api/Template.Api.csproj \
  --context AuthDbContext
sha256sum apps/api/src/Template.Infrastructure/Persistence/Migrations/AuthDbContextModelSnapshot.cs
```

Expected: no pending model changes; retain the printed snapshot hash in the task notes.

- [x] **Step 2: Write the failing architecture assertion**

Add a test that loads the Infrastructure assembly and requires the generalized context name:

```csharp
[Fact]
public void Persistence_context_is_named_for_the_whole_template()
{
    var names = typeof(Template.Infrastructure.Persistence.TemplateDbContext)
        .Assembly.GetTypes()
        .Select(type => type.Name)
        .ToArray();

    Assert.Contains("TemplateDbContext", names);
    Assert.DoesNotContain("AuthDbContext", names);
    Assert.Contains("EfApplicationUnitOfWork", names);
}
```

- [x] **Step 3: Run the assertion and observe RED**

Run:

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore --filter FullyQualifiedName~Persistence_context_is_named_for_the_whole_template
```

Expected: compilation fails because `TemplateDbContext` does not exist.

- [x] **Step 4: Perform the code-only rename**

Move the five files, rename their types/constructors/namespaces, update migration designer `[DbContext]` attributes, service registration, Data Protection/Identity/OpenIddict generics, tests, and E2E host. The transaction port must be exactly:

```csharp
namespace Template.Application.Common.Ports;

public interface IApplicationUnitOfWork
{
    Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken);
}
```

Do not add organization entities in this task.

- [x] **Step 5: Prove the rename is model-neutral**

Run:

```bash
dotnet build Template.sln --no-restore
dotnet test Template.sln --no-restore
dotnet ef migrations has-pending-model-changes \
  --project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --startup-project apps/api/src/Template.Api/Template.Api.csproj \
  --context TemplateDbContext
rg 'AuthDbContext|IAuthenticationUnitOfWork|EfAuthenticationUnitOfWork' apps/api \
  --glob '!**/bin/**' --glob '!**/obj/**'
```

Expected: build/test pass, no pending changes, and `rg` returns no source hit.

- [x] **Step 6: Commit the neutral generalization**

```bash
git add apps/api
git commit -m "refactor: generalize application persistence context"
```

---

### Task 2: Organization Domain Values and Permission Policy

**Files:**

- Create: `apps/api/src/Template.Domain/Organizations/OrganizationId.cs`
- Create: `apps/api/src/Template.Domain/Organizations/OrganizationMemberId.cs`
- Create: `apps/api/src/Template.Domain/Organizations/OrganizationSlug.cs`
- Create: `apps/api/src/Template.Domain/Organizations/OrganizationRole.cs`
- Create: `apps/api/src/Template.Domain/Organizations/OrganizationPermissionPolicy.cs`
- Create: `apps/api/src/Template.Domain/Organizations/OrganizationEmailDomainPolicy.cs`
- Create: `apps/api/tests/Template.Application.Tests/Organizations/OrganizationDomainTests.cs`

**Interfaces:**

- Consumes: `UserId` value conventions and no infrastructure dependencies.
- Produces: parseable UUID IDs; `OrganizationSlug.TryCreate`, `OrganizationSlug.GenerateBase`; closed role values; `OrganizationCapabilities`; role assignment predicates; email-domain normalization/eligibility.

- [x] **Step 1: Write failing domain tests**

Cover exact behavior with assertions such as:

```csharp
[Theory]
[InlineData("Acme Team", "acme-team")]
[InlineData("E2E-Slug", "e2e-slug")]
[InlineData("ЖЮ", "workspace")]
public void Generated_slug_base_is_canonical(string name, string expected) =>
    Assert.Equal(expected, OrganizationSlug.GenerateBase(name));

[Fact]
public void Admin_cannot_assign_owner_or_mutate_an_owner()
{
    Assert.False(OrganizationPermissionPolicy.CanAssign(
        OrganizationRole.Admin, OrganizationRole.Owner));
    Assert.False(OrganizationPermissionPolicy.CanChangeRole(
        OrganizationRole.Admin,
        actorUserId: Guid.Parse("00000000-0000-0000-0000-000000000001"),
        targetUserId: Guid.Parse("00000000-0000-0000-0000-000000000002"),
        currentTargetRole: OrganizationRole.Owner,
        requestedRole: OrganizationRole.Member,
        ownerCount: 2));
}

[Fact]
public void Allowed_domains_are_exact_normalized_and_deduplicated()
{
    var result = OrganizationEmailDomainPolicy.Normalize(
        [" Example.COM ", "@example.com", "admin.example.com"]);
    Assert.Equal(["example.com", "admin.example.com"], result.Domains);
    Assert.Empty(result.InvalidValues);
    Assert.False(OrganizationEmailDomainPolicy.IsAllowed(
        "person@sub.example.com", result.Domains));
}
```

Also assert name-independent slug max-base length 48, UUID round-trip, member assigns no role, owner assigns all roles, self edit is false, redundant change is false, owner count cannot reach zero, empty domain list disables restrictions, and invalid email produces `EmailDomain = null`.

- [x] **Step 2: Run focused tests and observe RED**

```bash
dotnet test apps/api/tests/Template.Application.Tests/Template.Application.Tests.csproj \
  --no-restore --filter FullyQualifiedName~OrganizationDomainTests
```

Expected: compilation fails for missing organization domain types.

- [x] **Step 3: Implement the closed values and policies**

Use readonly record structs. The role surface must be closed and string-backed:

```csharp
public readonly record struct OrganizationRole
{
    public static OrganizationRole Member { get; } = new("member");
    public static OrganizationRole Admin { get; } = new("admin");
    public static OrganizationRole Owner { get; } = new("owner");

    public string Value { get; }

    private OrganizationRole(string value) => Value = value;

    public static bool TryParse(string? value, out OrganizationRole role)
    {
        role = value switch
        {
            "member" => Member,
            "admin" => Admin,
            "owner" => Owner,
            _ => default
        };
        return value is "member" or "admin" or "owner";
    }

    public override string ToString() => Value;
}
```

`OrganizationPermissionPolicy.GetCapabilities(role)` returns explicit booleans
`CanUpdateOrganization`, `CanDeleteOrganization`, `CanAddMembers`, and
`CanUpdateMemberRoles`. `CanChangeRole` must reject self, unchanged role, admin
mutating owner, an unassignable requested role, and any change that would reduce
`ownerCount` to zero.

Define the capability projection in Domain so every later layer uses the same
closed shape:

```csharp
public sealed record OrganizationCapabilities(
    bool CanUpdateOrganization,
    bool CanDeleteOrganization,
    bool CanAddMembers,
    bool CanUpdateMemberRoles);
```

- [x] **Step 4: Run focused and full Application tests**

```bash
dotnet test apps/api/tests/Template.Application.Tests/Template.Application.Tests.csproj \
  --no-restore --filter FullyQualifiedName~OrganizationDomainTests
dotnet test apps/api/tests/Template.Application.Tests/Template.Application.Tests.csproj --no-restore
```

Expected: all pass.

- [x] **Step 5: Commit the domain policy**

```bash
git add apps/api/src/Template.Domain/Organizations \
  apps/api/tests/Template.Application.Tests/Organizations/OrganizationDomainTests.cs
git commit -m "feat: define organization domain policies"
```

---

### Task 3: Application Services, Outcomes, and Cursor Contract

**Files:**

- Create: `apps/api/src/Template.Application/Organizations/OrganizationModels.cs`
- Create: `apps/api/src/Template.Application/Organizations/OrganizationCursor.cs`
- Create: `apps/api/src/Template.Application/Organizations/Ports/IOrganizationStore.cs`
- Create: `apps/api/src/Template.Application/Organizations/Ports/IOrganizationUserLifecycleStore.cs`
- Create: `apps/api/src/Template.Application/Organizations/OrganizationService.cs`
- Create: `apps/api/src/Template.Application/Organizations/OrganizationMembershipService.cs`
- Create: `apps/api/tests/Template.Application.Tests/Organizations/OrganizationServiceTests.cs`
- Create: `apps/api/tests/Template.Application.Tests/Organizations/OrganizationMembershipServiceTests.cs`
- Create: `apps/api/tests/Template.Application.Tests/Organizations/OrganizationCursorTests.cs`

**Interfaces:**

- Consumes: Task 2 domain types and `UserId`/`SessionId`.
- Produces: application commands/projections, `OrganizationFailure`, opaque page cursors, `OrganizationService`, `OrganizationMembershipService`, and atomic store port signatures used by Infrastructure/API.

- [x] **Step 1: Define failing service tests with an in-memory fake port**

The tests must prove input normalization occurs before the store, invalid cursors never call the store, outside-domain add surfaces the acknowledgement result, and every command passes actor/session IDs. Example:

```csharp
[Fact]
public async Task Create_normalizes_name_and_passes_current_session()
{
    Assert.True(OrganizationSlug.TryCreate("acme-team", out var slug));
    var expected = new OrganizationDetail(
        new OrganizationId(Guid.Parse("00000000-0000-0000-0000-000000000010")),
        "Acme Team",
        slug,
        DateTimeOffset.Parse("2026-07-30T00:00:00Z"),
        DateTimeOffset.Parse("2026-07-30T00:00:00Z"),
        OrganizationRole.Owner,
        OrganizationPermissionPolicy.GetCapabilities(OrganizationRole.Owner),
        []);
    var store = new RecordingOrganizationStore
    {
        CreateResult = OrganizationOperationResult<OrganizationDetail>.Success(expected)
    };
    var service = new OrganizationService(store);

    var result = await service.CreateAsync(
        new UserId(ActorId), new SessionId(SessionId), "  Acme Team  ", default);

    Assert.True(result.Succeeded);
    Assert.Equal("Acme Team", store.LastCreate!.Name);
    Assert.Equal(new SessionId(SessionId), store.LastCreate.SessionId);
}

[Fact]
public async Task Invalid_member_cursor_is_rejected_without_store_access()
{
    var store = new RecordingOrganizationStore();
    var service = new OrganizationMembershipService(store);
    var result = await service.ListAsync(
        new UserId(ActorId), new OrganizationId(OrganizationId), "broken", 50, default);
    Assert.Equal(OrganizationFailure.InvalidCursor, result.Failure);
    Assert.Equal(0, store.ListMemberCalls);
}
```

- [x] **Step 2: Run the Application organization tests and observe RED**

```bash
dotnet test apps/api/tests/Template.Application.Tests/Template.Application.Tests.csproj \
  --no-restore --filter FullyQualifiedName~Organizations
```

Expected: compilation fails because application organization files are absent.

- [x] **Step 3: Implement coherent application models and ports**

Use these stable result shapes:

```csharp
public sealed record OrganizationSummary(
    OrganizationId Id,
    string Name,
    OrganizationSlug Slug,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    OrganizationRole CurrentRole,
    OrganizationCapabilities Capabilities);

public sealed record OrganizationDetail(
    OrganizationId Id,
    string Name,
    OrganizationSlug Slug,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    OrganizationRole CurrentRole,
    OrganizationCapabilities Capabilities,
    IReadOnlyList<string> AllowedEmailDomains);

public sealed record OrganizationMember(
    OrganizationMemberId Id,
    UserId UserId,
    string Name,
    string Email,
    string? ImageUrl,
    OrganizationRole Role,
    DateTimeOffset JoinedAt,
    string? EmailDomain,
    bool IsOutsideAllowedEmailDomains);

public sealed record OrganizationDeletion(OrganizationId OrganizationId);

public sealed record ActiveOrganization(OrganizationId OrganizationId);

public sealed record OrganizationPage(
    IReadOnlyList<OrganizationSummary> Items,
    string? NextCursor);

public sealed record OrganizationMemberPage(
    IReadOnlyList<OrganizationMember> Items,
    string? NextCursor);

public enum OrganizationFailure
{
    InvalidName, InvalidSlug, InvalidEmailDomain, InvalidCursor,
    NotFound, PermissionDenied, NameConflict, SlugConflict,
    LastAccessibleOrganization, ConfirmationMismatch,
    TargetUserNotFound, MemberNotFound, MemberAlreadyExists,
    MemberRoleUnchanged, RoleAssignmentForbidden,
    DomainAcknowledgementRequired, OwnershipTransferRequired,
    ConcurrencyConflict
}

public sealed record OrganizationOperationResult<T>(
    T? Value,
    OrganizationFailure? Failure,
    OrganizationDomainAcknowledgement? Acknowledgement)
    where T : class
{
    public bool Succeeded => Failure is null;

    public static OrganizationOperationResult<T> Success(T value) =>
        new(value, null, null);

    public static OrganizationOperationResult<T> Failed(
        OrganizationFailure failure,
        OrganizationDomainAcknowledgement? acknowledgement = null) =>
        new(null, failure, acknowledgement);
}

public sealed record OrganizationDomainAcknowledgement(
    string Email,
    string? EmailDomain,
    IReadOnlyList<string> AllowedEmailDomains);

public sealed record OrganizationCursorPosition(
    string NormalizedName,
    OrganizationId Id);

public sealed record OrganizationMemberCursorPosition(
    DateTimeOffset JoinedAt,
    OrganizationMemberId Id);

public sealed record OrganizationStorePage<TItem, TPosition>(
    IReadOnlyList<TItem> Items,
    TPosition? Next)
    where TItem : class
    where TPosition : class;
```

`OrganizationPage` and `OrganizationMemberPage` contain `Items` and
`NextCursor`. Store command records must include normalized values and explicit
`ActorUserId`, plus `SessionId` only for create/set-active.

The persistence ports must expose these exact use-case operations:

```csharp
public interface IOrganizationStore
{
    Task<OrganizationStorePage<OrganizationSummary, OrganizationCursorPosition>>
        ListAsync(UserId actorUserId, OrganizationCursorPosition? after, int limit,
            CancellationToken cancellationToken);
    Task<OrganizationOperationResult<OrganizationDetail>> GetByKeyAsync(
        UserId actorUserId, string organizationKey, CancellationToken cancellationToken);
    Task<OrganizationOperationResult<OrganizationDetail>> CreateAsync(
        CreateOrganizationCommand command, CancellationToken cancellationToken);
    Task<OrganizationOperationResult<OrganizationDetail>> UpdateAsync(
        UpdateOrganizationCommand command, CancellationToken cancellationToken);
    Task<OrganizationOperationResult<OrganizationDeletion>> DeleteAsync(
        DeleteOrganizationCommand command, CancellationToken cancellationToken);
    Task<OrganizationOperationResult<ActiveOrganization>> SetActiveAsync(
        SetActiveOrganizationCommand command, CancellationToken cancellationToken);
    Task<OrganizationStorePage<OrganizationMember, OrganizationMemberCursorPosition>>
        ListMembersAsync(UserId actorUserId, OrganizationId organizationId,
            OrganizationMemberCursorPosition? after, int limit,
            CancellationToken cancellationToken);
    Task<OrganizationOperationResult<OrganizationMember>> AddMemberAsync(
        AddOrganizationMemberCommand command, CancellationToken cancellationToken);
    Task<OrganizationOperationResult<OrganizationMember>> UpdateMemberRoleAsync(
        UpdateOrganizationMemberRoleCommand command,
        CancellationToken cancellationToken);
}

public interface IOrganizationUserLifecycleStore
{
    Task<OrganizationUserDeletionPreparation> PrepareDeletionAsync(
        UserId userId,
        CancellationToken cancellationToken);
}

public sealed record OrganizationUserDeletionPreparation(
    int DeletedOrganizations,
    bool OwnershipTransferRequired);

public sealed record CreateOrganizationCommand(
    UserId ActorUserId,
    SessionId SessionId,
    string Name);

public sealed record UpdateOrganizationCommand(
    UserId ActorUserId,
    OrganizationId OrganizationId,
    string? Name,
    OrganizationSlug? Slug,
    IReadOnlyList<string>? AllowedEmailDomains);

public sealed record DeleteOrganizationCommand(
    UserId ActorUserId,
    OrganizationId OrganizationId,
    string ConfirmationName);

public sealed record SetActiveOrganizationCommand(
    UserId ActorUserId,
    SessionId SessionId,
    OrganizationId OrganizationId);

public sealed record AddOrganizationMemberCommand(
    UserId ActorUserId,
    OrganizationId OrganizationId,
    UserId TargetUserId,
    OrganizationRole Role,
    bool AcknowledgeDomainRestriction);

public sealed record UpdateOrganizationMemberRoleCommand(
    UserId ActorUserId,
    OrganizationId OrganizationId,
    OrganizationMemberId MemberId,
    OrganizationRole Role);
```

- [x] **Step 4: Implement the canonical cursor codec**

Use a version byte, typed payload, SHA-256-derived four-byte checksum, and
base64url. Organization cursor encodes normalized name + UUID; member cursor
encodes UTC ticks + UUID. Reject padding, non-canonical re-encoding, invalid UTF-8,
wrong version/type/checksum, empty names, non-UTC ticks, and extra bytes.

- [x] **Step 5: Implement minimal Application orchestration**

- `OrganizationService`: list, get-by-key, create, update, delete, set-active.
- `OrganizationMembershipService`: list, add, update-role.
- validate page limit `1..100`; use default only at API boundary;
- validate commands using Domain types;
- encode returned continuation keys only after successful store pages;
- never translate to HTTP status codes.

- [x] **Step 6: Run all Application tests**

```bash
dotnet test apps/api/tests/Template.Application.Tests/Template.Application.Tests.csproj --no-restore
```

Expected: all existing and organization tests pass.

- [x] **Step 7: Commit the Application surface**

```bash
git add apps/api/src/Template.Application apps/api/tests/Template.Application.Tests/Organizations
git commit -m "feat: add organization application services"
```

---

### Task 4: EF Organization Model and Additive Migration

**Files:**

- Create: `apps/api/src/Template.Infrastructure/Organizations/OrganizationEntity.cs`
- Create: `apps/api/src/Template.Infrastructure/Organizations/OrganizationMemberEntity.cs`
- Create: `apps/api/src/Template.Infrastructure/Organizations/OrganizationAllowedEmailDomainEntity.cs`
- Create: three focused configuration files in the same directory
- Modify: `apps/api/src/Template.Infrastructure/Persistence/TemplateDbContext.cs`
- Modify: `apps/api/src/Template.Infrastructure/Persistence/AuthSessionEntity.cs`
- Generate: `apps/api/src/Template.Infrastructure/Persistence/Migrations/*_OrganizationsMembershipOnboarding.cs`
- Generate: matching designer and `TemplateDbContextModelSnapshot.cs`
- Create: `apps/api/tests/Template.Api.Tests/Organizations/OrganizationPersistenceModelTests.cs`
- Modify: `apps/api/tests/Template.Api.Tests/Infrastructure/ApiWebApplicationFactory.cs`
- Modify: `apps/api/tests/Template.E2EHost/Program.cs`

**Interfaces:**

- Consumes: Task 1 context name and Task 2 IDs/roles.
- Produces: schemas/tables/constraints/indexes and nullable session active-organization FK required by `EfOrganizationStore`.

- [x] **Step 1: Write failing PostgreSQL model tests**

Using the migrated Testcontainers database, query `information_schema` and
`pg_catalog` to assert:

```csharp
Assert.Equal(
    ["allowed_email_domains", "members", "organizations"],
    await ReadTablesAsync("organizations"));
Assert.Equal("SET NULL", await ReadDeleteRuleAsync(
    "auth", "sessions", "active_organization_id"));
Assert.True(await HasUniqueIndexAsync(
    "organizations", "members", "organization_id", "user_id"));
Assert.True(await HasCheckContainingAsync(
    "organizations", "members", "role", "owner", "admin", "member"));
```

Also test active preference becomes null after organization deletion and deleting
an Identity user cascades its membership.

- [x] **Step 2: Run the model tests and observe RED**

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore --filter FullyQualifiedName~OrganizationPersistenceModelTests
```

Expected: assertions fail because schema/tables/column do not exist.

- [x] **Step 3: Add entities and focused EF configurations**

Map exact tables and constraints from the design. The session relation must be:

```csharp
entity.Property(value => value.ActiveOrganizationId)
    .HasColumnName("active_organization_id");
entity.HasOne<OrganizationEntity>()
    .WithMany()
    .HasForeignKey(value => value.ActiveOrganizationId)
    .OnDelete(DeleteBehavior.SetNull)
    .HasConstraintName("fk_sessions_organizations_active_organization_id");
entity.HasIndex(value => value.ActiveOrganizationId)
    .HasDatabaseName("ix_sessions_active_organization_id");
```

Keep `TemplateDbContext` default schema `auth`; each organization configuration
must explicitly call `ToTable(..., "organizations")`.

- [x] **Step 4: Generate and inspect one additive migration**

```bash
dotnet ef migrations add OrganizationsMembershipOnboarding \
  --project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --startup-project apps/api/src/Template.Api/Template.Api.csproj \
  --context TemplateDbContext \
  --output-dir Persistence/Migrations
```

Inspect `Up` and `Down`. `Up` must create schema/tables/checks/indexes and the
session FK; it must not drop or rename an existing auth/OpenIddict/Data Protection
object. `Down` must remove only iteration-5 additions.

- [x] **Step 5: Run clean migration and model-drift tests**

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore --filter FullyQualifiedName~OrganizationPersistenceModelTests
dotnet ef migrations has-pending-model-changes \
  --project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --startup-project apps/api/src/Template.Api/Template.Api.csproj \
  --context TemplateDbContext
dotnet ef migrations script --idempotent \
  --project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --startup-project apps/api/src/Template.Api/Template.Api.csproj \
  --context TemplateDbContext > /tmp/template-iteration5-idempotent.sql
test -s /tmp/template-iteration5-idempotent.sql
```

Expected: focused tests pass, no pending changes, script is non-empty.

- [x] **Step 6: Commit the additive schema**

```bash
git add apps/api/src/Template.Infrastructure apps/api/tests/Template.Api.Tests \
  apps/api/tests/Template.E2EHost
git commit -m "feat: add organization persistence schema"
```

---

### Task 5: Atomic EF Organization and Membership Store

**Files:**

- Create: `apps/api/src/Template.Infrastructure/Organizations/EfOrganizationStore.cs`
- Modify: `apps/api/src/Template.Infrastructure/Persistence/InfrastructureServiceCollectionExtensions.cs`
- Modify: `apps/api/src/Template.Infrastructure/Authentication/BrowserSessionGateway.cs`
- Modify: `apps/api/src/Template.Application/Authentication/AuthModels.cs`
- Create: `apps/api/tests/Template.Api.Tests/Organizations/OrganizationStoreTests.cs`
- Create: `apps/api/tests/Template.Api.Tests/Organizations/OrganizationConcurrencyTests.cs`

**Interfaces:**

- Consumes: `IOrganizationStore`, EF model, `IApplicationUnitOfWork`, Domain policies.
- Produces: tenant-qualified page/detail reads and all seven atomic organization/member writes.

- [x] **Step 1: Write failing atomic behavior tests**

Create helpers that seed Identity users and sessions, then assert:

```csharp
[Fact]
public async Task Create_stores_owner_and_active_session_atomically()
{
    await using var fixture = await OrganizationStoreFixture.CreateAsync();
    var actor = await fixture.CreateUserAndSessionAsync("owner@local-agent.test");

    var result = await fixture.Store.CreateAsync(
        new CreateOrganizationCommand(actor.UserId, actor.SessionId, "Acme"),
        default);

    Assert.True(result.Succeeded);
    var detail = Assert.IsType<OrganizationDetail>(result.Value);
    await using var db = fixture.CreateDbContext();
    Assert.Equal(OrganizationRole.Owner.Value, await db.OrganizationMembers
        .Where(row => row.OrganizationId == detail.Id.Value)
        .Select(row => row.Role)
        .SingleAsync());
    Assert.Equal(detail.Id.Value, await db.Sessions
        .Where(row => row.Id == actor.SessionId.Value)
        .Select(row => row.ActiveOrganizationId)
        .SingleAsync());
}

[Fact]
public async Task Last_owner_race_allows_at_most_one_demotion()
{
    await using var fixture = await OrganizationStoreFixture.CreateWithTwoOwnersAsync();
    var attempts = await Task.WhenAll(
        fixture.DemoteOwnerAsync(fixture.FirstOwner),
        fixture.DemoteOwnerAsync(fixture.SecondOwner));

    Assert.Equal(1, attempts.Count(result => result.Succeeded));
    Assert.Equal(1, attempts.Count(result =>
        result.Failure == OrganizationFailure.RoleAssignmentForbidden ||
        result.Failure == OrganizationFailure.ConcurrencyConflict));
    Assert.Equal(1, await fixture.CountOwnersAsync());
}
```

Add tests for deterministic `(name,id)` and `(joinedAt,id)` pages, create slug
suffix, update domain replacement, last-accessible delete, case-sensitive
confirmation, duplicate member, domain acknowledgement no-write/retry, admin role
limits, no self edit, slug unique race, and `PostgresTicketStore.RenewAsync`
preserving `ActiveOrganizationId`.

`OrganizationStoreFixture` is an internal helper in
`OrganizationStoreTests.cs`; it creates a disposable PostgreSQL database,
applies migrations, builds a service provider, exposes `IOrganizationStore`,
creates independent DbContexts for concurrency, and deletes its database on
dispose. `CreateWithTwoOwnersAsync` seeds one organization with two owner
memberships and separate actor sessions.

- [x] **Step 2: Run store tests and observe RED**

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore --filter 'FullyQualifiedName~OrganizationStoreTests|FullyQualifiedName~OrganizationConcurrencyTests'
```

Expected: DI/type failures because the store is absent.

- [x] **Step 3: Implement tenant-qualified reads**

Every organization read joins `organizations.members` on `ActorUserId`. Detail
accepts slug or parsed UUID but never falls back to an unqualified organization
query. Member list first verifies actor membership and then returns only rows from
the same organization. Use `AsNoTracking()` and select only projection columns.

- [x] **Step 4: Implement atomic writes with locks and bounded retries**

Use explicit transactions and `FOR UPDATE` locks for organization/actor/target/
owners. Create retries PostgreSQL `UniqueViolation` for the slug at most five
times. Update maps the global slug unique violation. Add member maps the composite
unique violation. Role update reuses `OrganizationPermissionPolicy` after locks.
Delete rechecks actor accessible count under the transaction.

- [x] **Step 5: Project active organization through browser sessions**

Extend `BrowserSession` with `OrganizationId? ActiveOrganizationId`; map the EF
column in `BrowserSessionGateway.GetCurrentAsync`, `SignInAsync`, and
`RenewCurrentAsync`. New sessions start with null. Ticket serialization remains
unchanged and contains no organization claim.

- [x] **Step 6: Run focused store, ticket, and full .NET tests**

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore --filter 'FullyQualifiedName~Organization|FullyQualifiedName~PostgresTicketStore'
dotnet test Template.sln --no-restore
```

Expected: all pass with no flaky concurrency failure.

- [x] **Step 7: Commit the persistence behavior**

```bash
git add apps/api/src/Template.Application/Authentication/AuthModels.cs \
  apps/api/src/Template.Infrastructure apps/api/tests
git commit -m "feat: implement atomic organization persistence"
```

---

### Task 6: Organization-Aware Account and Local Automation Cleanup

**Files:**

- Create: `apps/api/src/Template.Infrastructure/Organizations/EfOrganizationUserLifecycleStore.cs`
- Modify: `apps/api/src/Template.Application/Accounts/AccountService.cs`
- Modify: `apps/api/src/Template.Application/Accounts/AccountModels.cs`
- Modify: `apps/api/src/Template.Application/Authentication/LocalAutomationAuthService.cs`
- Modify: `apps/api/src/Template.Application/Authentication/AuthModels.cs`
- Modify: `apps/api/src/Template.Infrastructure/Accounts/EfAccountStore.cs`
- Modify: `apps/api/src/Template.Infrastructure/Identity/IdentityGateway.cs`
- Modify: `apps/api/src/Template.Infrastructure/Persistence/InfrastructureServiceCollectionExtensions.cs`
- Modify: `apps/api/src/Template.Api/Errors/ApiProblemCodes.cs`
- Modify: `apps/api/src/Template.Api/Features/Account/AccountEndpointModule.cs`
- Modify: `apps/api/tests/Template.Application.Tests/Accounts/AccountServiceTests.cs`
- Modify: `apps/api/tests/Template.Application.Tests/LocalAutomationAuthServiceTests.cs`
- Create: `apps/api/tests/Template.Api.Tests/Organizations/OrganizationUserLifecycleTests.cs`

**Interfaces:**

- Consumes: `IOrganizationUserLifecycleStore.PrepareDeletionAsync(UserId, CancellationToken)` and `IApplicationUnitOfWork`.
- Produces: atomic user deletion policy, real local cleanup organization count, and `organization_ownership_transfer_required` failure.

- [x] **Step 1: Write failing cleanup classification tests**

Assert these exact cases:

```csharp
[Fact]
public async Task Sole_owner_of_shared_organization_must_transfer_ownership()
{
    await using var fixture = await OrganizationUserLifecycleFixture.CreateAsync();
    var owner = await fixture.CreateUserAsync("owner@local-agent.test");
    var member = await fixture.CreateUserAsync("member@local-agent.test");
    var organizationId = await fixture.CreateOrganizationAsync(owner, member);

    var result = await fixture.DeleteAccountAsync(owner);

    Assert.Equal(
        AccountFailure.OrganizationOwnershipTransferRequired,
        result.Failure);
    Assert.True(await fixture.UserExistsAsync(owner));
    Assert.True(await fixture.OrganizationExistsAsync(organizationId));
    Assert.Equal(2, await fixture.CountMembersAsync(organizationId));
}

[Fact]
public async Task Only_member_organization_is_deleted_with_the_account()
{
    await using var fixture = await OrganizationUserLifecycleFixture.CreateAsync();
    var owner = await fixture.CreateUserAsync("owner@local-agent.test");
    var organizationId = await fixture.CreateOrganizationAsync(owner);

    var result = await fixture.DeleteAccountAsync(owner);

    Assert.True(result.Succeeded);
    Assert.False(await fixture.UserExistsAsync(owner));
    Assert.False(await fixture.OrganizationExistsAsync(organizationId));
}
```

Also assert a failed transfer precondition deletes neither organizations,
memberships, sessions nor user; local cleanup reports the number of sole-member
organizations; and cleanup of a plain local user still returns zero.

`OrganizationUserLifecycleFixture` lives in the new API test file and exposes
the exact seed/query methods used above against one migrated disposable
PostgreSQL database.

- [x] **Step 2: Run focused cleanup tests and observe RED**

```bash
dotnet test Template.sln --no-restore \
  --filter 'FullyQualifiedName~OrganizationUserLifecycle|FullyQualifiedName~Cleanup|FullyQualifiedName~AccountService'
```

Expected: current deletion leaves organization cleanup unclassified and returns
zero.

- [x] **Step 3: Implement analyze-before-mutate lifecycle cleanup**

Inside the current transaction, lock every organization membership for the user
and each affected owner set. First classify all rows. If any multi-member
organization would have no owner, return `OwnershipTransferRequired` without any
write. Otherwise delete only single-member organizations and let user deletion
cascade remaining memberships. Clear session active preferences for memberships
that disappear.

- [x] **Step 4: Wrap account deletion in the generalized unit of work**

`AccountService.DeleteAsync` must validate email first, then call
`IApplicationUnitOfWork.ExecuteAsync`, run lifecycle preparation, and call an
`EfAccountStore.DeleteAsync` implementation that participates in an ambient
transaction instead of opening a nested one. Map the lifecycle result to
`AccountFailure.OrganizationOwnershipTransferRequired`.

- [x] **Step 5: Reuse the same lifecycle port from local cleanup**

Inside the existing cleanup unit of work, prepare organization deletion before
Identity deletion and return:

```csharp
new LocalAutomationCleanup(
    DeletedOrganizations: lifecycle.DeletedOrganizations)
```

A transfer-required local cleanup returns
`AuthFailure.OrganizationOwnershipTransferRequired`, maps to the same
`409 organization_ownership_transfer_required` Problem Details code, and
performs no partial delete.

- [x] **Step 6: Run focused and full .NET tests**

```bash
dotnet test Template.sln --no-restore \
  --filter 'FullyQualifiedName~OrganizationUserLifecycle|FullyQualifiedName~Cleanup|FullyQualifiedName~Account'
dotnet test Template.sln --no-restore
```

Expected: all pass.

- [x] **Step 7: Commit cleanup integration**

```bash
git add apps/api
git commit -m "feat: protect organization ownership during account deletion"
```

---

### Task 7: Organization REST Boundary, Security, and Problem Details

**Files:**

- Create: `apps/api/src/Template.Api/Features/Organizations/OrganizationContracts.cs`
- Create: `apps/api/src/Template.Api/Features/Organizations/OrganizationEndpointModule.cs`
- Create: `apps/api/src/Template.Api/Features/Organizations/OrganizationSecurityEvents.cs`
- Modify: `apps/api/src/Template.Api/Endpoints/EndpointModuleExtensions.cs`
- Modify: `apps/api/src/Template.Api/ApiHost.cs`
- Modify: `apps/api/src/Template.Api/Features/Auth/AuthContracts.cs`
- Modify: `apps/api/src/Template.Api/Features/Auth/AuthEndpointModule.cs`
- Modify: `apps/api/src/Template.Api/Errors/ApiProblemCodes.cs`
- Modify: `apps/api/src/Template.Api/Errors/ApiProblemDetailsDefaults.cs`
- Modify: `apps/api/src/Template.Api/Errors/ApiProblemException.cs`
- Modify: `apps/api/src/Template.Api/Errors/ApiExceptionHandler.cs`
- Create: `apps/api/tests/Template.Api.Tests/Organizations/OrganizationEndpointTests.cs`
- Create: `apps/api/tests/Template.Api.Tests/Organizations/OrganizationSecurityTests.cs`
- Modify: `apps/api/tests/Template.Api.Tests/ProblemDetailsTests.cs`

**Interfaces:**

- Consumes: Task 3 services and Task 5/6 outcomes.
- Produces: named operations `GetOrganizations`, `CreateOrganization`, `GetOrganizationByKey`, `UpdateOrganization`, `DeleteOrganization`, `SetActiveOrganization`, `GetOrganizationMembers`, `AddOrganizationMember`, `UpdateOrganizationMemberRole`.

Map the exact surface:

```text
GET    /api/v1/organizations
POST   /api/v1/organizations
GET    /api/v1/organizations/by-key/{organizationKey}
PATCH  /api/v1/organizations/{organizationId}
DELETE /api/v1/organizations/{organizationId}
PUT    /api/v1/auth/session/active-organization
GET    /api/v1/organizations/{organizationId}/members
POST   /api/v1/organizations/{organizationId}/members
PATCH  /api/v1/organizations/{organizationId}/members/{memberId}
```

There is no member DELETE endpoint.

- [x] **Step 1: Write failing HTTP boundary tests**

For every operation cover anonymous 401, authenticated success, and no-store.
For every mutation cover missing/wrong CSRF, strict non-JSON/malformed/unknown
JSON, and role denial. Representative assertions:

```csharp
Assert.Equal(HttpStatusCode.Created, response.StatusCode);
Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
Assert.Equal("owner", json.RootElement.GetProperty("data").GetProperty("currentRole").GetString());

await AssertProblemAsync(
    foreignResponse,
    HttpStatusCode.NotFound,
    "organization_not_found");
await AssertProblemAsync(
    lastDeleteResponse,
    HttpStatusCode.Conflict,
    "last_organization_required");
```

Assert domain acknowledgement is 409 with `email`, `emailDomain`, and
`allowedEmailDomains` extensions; retry with acknowledgement creates exactly one
member. Assert safe logs omit seeded names/emails/domains.

- [x] **Step 2: Run endpoint tests and observe RED**

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore --filter 'FullyQualifiedName~OrganizationEndpoint|FullyQualifiedName~OrganizationSecurity'
```

Expected: 404/missing types because organization endpoints are not mapped.

- [x] **Step 3: Define strict request/response contracts**

All request records use `[JsonUnmappedMemberHandling(Disallow)]`. Use nullable
properties plus explicit API validation so empty PATCH is rejected. Define
capabilities and closed role strings. DELETE body is required and contains only
`confirmationName`.

Extend `ApiProblemException` with an optional read-only extension dictionary:

```csharp
internal sealed class ApiProblemException(
    int statusCode,
    string code,
    IReadOnlyDictionary<string, object?>? extensions = null) : Exception
{
    internal int StatusCode { get; } = statusCode;
    internal string Code { get; } = code;
    internal IReadOnlyDictionary<string, object?> Extensions { get; } =
        extensions ?? new Dictionary<string, object?>();
}
```

Copy only allow-listed primitive/string-array extensions into Problem Details.

- [x] **Step 4: Map endpoints with established metadata**

Use the inherited browser-session group. Add `RequireApiAntiforgery()` to POST,
PATCH, PUT, DELETE. Use `ApiJsonRequestReader` for required bodies. Validate name,
slug, UUIDs, limit `1..100`, and non-empty PATCH at HTTP boundary. Map application
failures only to the stable codes/statuses in the design.

- [x] **Step 5: Extend current auth session projection**

Add `Guid? ActiveOrganizationId` to authenticated session metadata/response and
map it from `BrowserSession.ActiveOrganizationId`; anonymous response remains
nullable and safe. The PUT operation gets current session ID through
`IBrowserSessionGateway` and passes it explicitly to `OrganizationService`.

- [x] **Step 6: Run endpoint, Problem Details, auth, and full API tests**

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore --filter 'FullyQualifiedName~Organization|FullyQualifiedName~ProblemDetails|FullyQualifiedName~AuthEndpoint'
dotnet test Template.sln --no-restore
```

Expected: all pass; existing auth session clients tolerate the additive field.

- [x] **Step 7: Commit the REST boundary**

```bash
git add apps/api
git commit -m "feat: expose organization browser REST API"
```

---

### Task 8: OpenAPI Contract and Generated TypeScript SDK

**Files:**

- Create: `apps/api/src/Template.Api/OpenApi/OrganizationContractSchemaTransformer.cs`
- Create: `apps/api/src/Template.Api/OpenApi/OrganizationContractOperationTransformer.cs`
- Modify: `apps/api/src/Template.Api/OpenApi/OpenApiServiceCollectionExtensions.cs`
- Modify: `apps/api/src/Template.Api/OpenApi/ApiContractSchemaTransformer.cs`
- Modify: `apps/api/tests/Template.Api.Tests/OpenApiContractTests.cs`
- Modify: `contracts/openapi/v1.json`
- Regenerate: `apps/web/src/lib/api/generated/**`
- Modify: `apps/web/test/contracts/generated-sdk.test.ts`

**Interfaces:**

- Consumes: named Task 7 operations and contracts.
- Produces: exact cookie/CSRF/security/error/pagination schema and generated SDK functions with those operation names.

- [x] **Step 1: Write failing OpenAPI assertions**

Assert all nine operations exist, cookie security is mandatory, mutations require
`X-CSRF-TOKEN`, role strings are enums, strict bodies disallow additional
properties, limit is integer 1–100/default 50, and 404/409 responses reference
Problem Details. Add stable code assertions for every organization code.

```csharp
AssertOperation(document, "/api/v1/organizations", "post", "CreateOrganization");
AssertRequiredHeader(create, "X-CSRF-TOKEN");
AssertStringEnum(roleSchema, "member", "admin", "owner");
AssertPagination(getOrganizations, minimum: 1, maximum: 100, defaultValue: 50);
```

- [x] **Step 2: Run OpenAPI tests and observe RED**

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore --filter FullyQualifiedName~OpenApiContractTests
```

Expected: missing organization schema constraints/codes.

- [x] **Step 3: Implement focused transformers**

Register separate schema and operation transformers. Remove organization-specific
logic from the existing account transformer. Add only stable organization codes
to the global Problem Details enum. Publish trimmed constraints with existing
`x-trimmed-*` conventions when raw JSON Schema length would reject accepted
padding.

- [x] **Step 4: Export the exact contract twice and compare**

```bash
dotnet build apps/api/src/Template.Api/Template.Api.csproj --no-restore \
  -p:OpenApiGenerateDocuments=true
sha256sum contracts/openapi/v1.json > /tmp/iteration5-openapi-a.sha
dotnet build apps/api/src/Template.Api/Template.Api.csproj --no-restore \
  -p:OpenApiGenerateDocuments=true
sha256sum contracts/openapi/v1.json > /tmp/iteration5-openapi-b.sha
diff -u /tmp/iteration5-openapi-a.sha /tmp/iteration5-openapi-b.sha
```

Expected: hashes are identical.

- [x] **Step 5: Regenerate and test the SDK**

```bash
cd apps/web
npm run api:generate
npm run api:check
npm test -- --runInBand test/contracts/generated-sdk.test.ts
```

The generated test must import and assert function existence for all nine
operation names. Never hand-edit generated files.

- [x] **Step 6: Commit contract and SDK**

```bash
git add apps/api/src/Template.Api/OpenApi apps/api/tests/Template.Api.Tests/OpenApiContractTests.cs \
  contracts/openapi/v1.json apps/web/src/lib/api/generated apps/web/test/contracts/generated-sdk.test.ts
git commit -m "feat: publish organization OpenAPI contract"
```

---

### Task 9: Web API Adapters and Organization Route Resolution

**Files:**

- Create: `apps/web/src/features/organizations/organization-routes.ts`
- Create: `apps/web/src/features/organizations/organization-switch-navigation.ts`
- Create: `apps/web/src/lib/api/browser/run-csrf-mutation.ts`
- Create: `apps/web/src/lib/api/organizations/server/load-organizations.ts`
- Create: `apps/web/src/lib/api/organizations/server/load-organization.ts`
- Create: `apps/web/src/lib/api/organizations/server/load-organization-members.ts`
- Create: `apps/web/src/lib/api/organizations/browser/organization-mutations.ts`
- Modify: `apps/web/src/lib/api/account/browser/account-mutations.ts`
- Modify: `apps/web/src/features/application/application-routes.ts`
- Create: `apps/web/test/features/organization-routes.test.ts`
- Create: `apps/web/test/features/organization-switch-navigation.test.ts`
- Create: `apps/web/test/lib/api/organizations-api.test.ts`
- Modify: `apps/web/test/lib/api/account-api.test.ts`

**Interfaces:**

- Consumes: generated SDK Task 8 and existing server/browser clients.
- Produces: typed route builders, REST-only SSR loaders, shared CSRF helper, and all browser organization mutations.

- [x] **Step 1: Read the installed Next.js 16.2.11 documentation before edits**

```bash
sed -n '1,240p' apps/web/node_modules/next/dist/docs/01-app/01-getting-started/05-server-and-client-components.md
sed -n '1,240p' apps/web/node_modules/next/dist/docs/01-app/01-getting-started/06-fetching-data.md
sed -n '1,220p' apps/web/node_modules/next/dist/docs/01-app/02-guides/redirecting.md
sed -n '1,220p' apps/web/node_modules/next/dist/docs/01-app/03-api-reference/03-file-conventions/dynamic-routes.md
sed -n '1,220p' apps/web/node_modules/next/dist/docs/01-app/03-api-reference/03-file-conventions/loading.md
sed -n '1,220p' apps/web/node_modules/next/dist/docs/01-app/03-api-reference/03-file-conventions/error.md
```

Record only installed-version facts used by the implementation in the commit
notes; do not add a documentation dump to the repository.

- [x] **Step 2: Write failing route and adapter tests**

Assert route encoding and switch preservation:

```ts
expect(organizationRoutes.dashboard("acme team")).toBe(
  "/w/acme%20team/dashboard",
);
expect(resolveOrganizationSwitchHref("/w/old/settings/users", "new")).toBe(
  "/w/new/settings/users",
);
expect(resolveOrganizationSwitchHref("/w/old/custom/deep", "new")).toBe(
  "/w/new/dashboard",
);
```

Mock generated operations and assert SSR loaders add only forwarded cookie,
correlation ID, `cache: "no-store"`, and renewal suppression. Assert every
browser mutation calls `getAuthCsrfToken` once and generated SDK once.

- [x] **Step 3: Run focused Jest and observe RED**

```bash
cd apps/web
npm test -- --runInBand \
  test/features/organization-routes.test.ts \
  test/features/organization-switch-navigation.test.ts \
  test/lib/api/organizations-api.test.ts
```

Expected: module-not-found failures.

- [x] **Step 4: Implement typed routes and switch resolver**

Expose `/welcome`, `/workspaces`, workspace root/dashboard, and settings
workspace/users/roles. Preserve only those registered single-key route suffixes;
all other workspace paths fall back to dashboard.

- [x] **Step 5: Extract the shared CSRF mutation runner**

Move the generic request-token/error-normalization flow out of account-specific
code without changing account behavior. The helper signature is:

```ts
export async function runCsrfMutation<T>(
  client: Client,
  operation: (csrfToken: string) => Promise<MutationResponse<T>>,
): Promise<ApiResult<T>>;
```

Reuse it from account and organization mutations.

- [x] **Step 6: Implement generated-only organization loaders/adapters**

SSR detail/list/members loaders use isolated clients and suppress renewal.
Browser adapters cover create, update, delete, set-active, add-member, and
update-role. They branch only on normalized stable failure codes.

Every cookie-bearing organization SSR operation sends exactly:

```ts
headers: {
  "X-Template-Session-Renewal": "suppress",
}
```

- [x] **Step 7: Run focused and full web boundary tests**

```bash
npm test -- --runInBand \
  test/features/organization-routes.test.ts \
  test/features/organization-switch-navigation.test.ts \
  test/lib/api/organizations-api.test.ts \
  test/lib/api/account-api.test.ts
npm run boundaries:check
```

Expected: pass; no raw organization `fetch` or handwritten transport type.

- [x] **Step 8: Commit the web data boundary**

```bash
git add apps/web/src/features apps/web/src/lib/api apps/web/test
git commit -m "feat: add organization web API adapters"
```

---

### Task 10: Onboarding, Workspace List, Routing, and Switcher UI

**Files:**

- Create: `apps/web/src/messages/organizations.en.json`
- Create: `apps/web/src/messages/organizations.ru.json`
- Modify: `apps/web/src/i18n/messages.ts`
- Create: `apps/web/src/components/organizations/organization-onboarding.tsx`
- Create: `apps/web/src/components/organizations/organization-create-dialog.tsx`
- Create: `apps/web/src/components/organizations/organization-card.tsx`
- Create: `apps/web/src/components/organizations/organization-list.tsx`
- Create: `apps/web/src/components/organizations/organization-switcher.tsx`
- Modify: `apps/web/src/components/application/site-header.tsx`
- Replace: `apps/web/src/app/(site)/dashboard/page.tsx`
- Add: routes under `apps/web/src/app/(site)/welcome`, `workspaces`, and `w/[organizationKey]/{page.tsx,dashboard/page.tsx}`
- Create: corresponding `loading.tsx`/`error.tsx` only at local list/dashboard boundaries
- Create: `apps/web/test/components/organization-onboarding.test.tsx`
- Create: `apps/web/test/components/organization-list.test.tsx`
- Create: `apps/web/test/components/organization-switcher.test.tsx`
- Create: `apps/web/test/app/organization-routing.test.tsx`
- Modify: `apps/web/test/components/site-header.test.tsx`
- Modify: `apps/web/test/i18n/messages.test.ts`

**Interfaces:**

- Consumes: Task 9 loaders/mutations/routes.
- Produces: zero-org onboarding, paged workspace list/create, active/fallback dashboard resolver, canonical route guard, and explicit switch UI.

- [x] **Step 1: Write failing UI/route tests**

Mock loaders and Next navigation. Assert:

```tsx
expect(
  screen.getByRole("heading", { name: "Create your first workspace" }),
).toBeVisible();
expect(
  screen.queryByRole("link", { name: /invitation/i }),
).not.toBeInTheDocument();
expect(redirect).toHaveBeenCalledWith("/w/acme/dashboard");
expect(forbidden).toHaveBeenCalled();
```

Also assert existing-org `/welcome` redirects through `/dashboard`, zero-org
`/dashboard` redirects `/welcome`, active accessible org wins, invalid active
falls back to first page item, UUID root canonicalizes, list exposes load-more,
create uses returned canonical key, and switch mutation precedes navigation.

- [x] **Step 2: Run focused Jest and observe RED**

```bash
cd apps/web
npm test -- --runInBand \
  test/components/organization-onboarding.test.tsx \
  test/components/organization-list.test.tsx \
  test/components/organization-switcher.test.tsx \
  test/app/organization-routing.test.tsx
```

Expected: missing components/routes.

- [x] **Step 3: Implement fixed en/ru messages and onboarding/create**

Use the existing locale catalogue registry. Validate the trimmed name on the
client with the same 1–50 UTF-16 and character policy, but display API validation
as authoritative. On create success navigate to the returned
`/w/{canonicalKey}/dashboard` and refresh.

- [x] **Step 4: Implement paged list/cards and safe failure states**

Cards show name/slug and links to dashboard/settings. Delete controls are not
added in this task. Append pages by sending the server cursor verbatim and
de-duplicate organization IDs. Never display raw API detail; show stable localized
copy plus trace ID when present.

- [x] **Step 5: Implement server route resolution**

- `/dashboard`: active detail when accessible, otherwise first list item,
  otherwise `/welcome`.
- `/welcome`: onboarding only when list empty, otherwise `/dashboard`.
- `/w/{key}`: resolve detail; zero list means onboarding; detail 404 with a
  non-empty list calls `forbidden()`; success redirects canonical dashboard.
- `/w/{key}/dashboard`: same access guard, minimal organization name/context only.

Do not mutate active context during a deep-link read.

- [x] **Step 6: Implement the minimal explicit switcher**

Render in `SiteHeader` only for authenticated organization-aware pages. On
selection, call set-active, then route via the switch resolver and refresh. Load
additional organization pages explicitly rather than silently truncating the
switcher.

- [x] **Step 7: Run focused and full web tests**

```bash
npm test -- --runInBand \
  test/components/organization-onboarding.test.tsx \
  test/components/organization-list.test.tsx \
  test/components/organization-switcher.test.tsx \
  test/app/organization-routing.test.tsx \
  test/components/site-header.test.tsx \
  test/i18n/messages.test.ts
npm run typecheck
```

Expected: pass.

- [x] **Step 8: Commit onboarding and routing UI**

```bash
git add apps/web
git commit -m "feat: add organization onboarding and routing"
```

---

### Task 11: Workspace Settings and Member Management UI

**Files:**

- Create: `apps/web/src/components/organizations/organization-settings-nav.tsx`
- Create: `apps/web/src/components/organizations/organization-settings-form.tsx`
- Create: `apps/web/src/components/organizations/organization-delete-dialog.tsx`
- Create: `apps/web/src/components/organizations/organization-member-directory.tsx`
- Create: `apps/web/src/components/organizations/organization-add-member-dialog.tsx`
- Create: `apps/web/src/components/organizations/organization-member-role-control.tsx`
- Add: `apps/web/src/app/(site)/w/[organizationKey]/settings/layout.tsx`
- Add: settings root, workspace, users, and roles routes with local loading/error files
- Modify: `apps/web/src/components/organizations/organization-card.tsx`
- Create: `apps/web/test/components/organization-settings-form.test.tsx`
- Create: `apps/web/test/components/organization-delete-dialog.test.tsx`
- Create: `apps/web/test/components/organization-member-directory.test.tsx`
- Create: `apps/web/test/components/organization-add-member-dialog.test.tsx`
- Create: `apps/web/test/app/organization-settings-pages.test.tsx`

**Interfaces:**

- Consumes: detail/member loaders, capabilities, and browser mutations.
- Produces: role-aware settings, delete, direct-add/domain acknowledgement, role update, and fixed-role explanation pages.

- [x] **Step 1: Write failing settings/member tests**

Assert owner/admin/member presentation separately:

```tsx
expect(screen.getByLabelText("Workspace Name")).toBeDisabled();
expect(screen.queryByRole("button", { name: "Save" })).not.toBeInTheDocument();
expect(
  screen.queryByRole("button", { name: /remove|delete member/i }),
).not.toBeInTheDocument();
expect(screen.getByText("Outside domain policy")).toBeVisible();
```

Assert slug success replaces the canonical URL; delete requires exact
case-sensitive name and is absent for last accessible org; current actor is
separate; admin cannot choose owner; self role control is absent; domain 409
opens a confirmation view and confirm retries once with acknowledgement; a
successful mutation followed by failed refresh retains confirmed state and shows
refresh retry without repeating mutation.

- [x] **Step 2: Run focused tests and observe RED**

```bash
cd apps/web
npm test -- --runInBand \
  test/components/organization-settings-form.test.tsx \
  test/components/organization-delete-dialog.test.tsx \
  test/components/organization-member-directory.test.tsx \
  test/components/organization-add-member-dialog.test.tsx \
  test/app/organization-settings-pages.test.tsx
```

Expected: missing components/routes.

- [x] **Step 3: Implement settings layout/navigation and access guard**

Expose only Workspace, Users, and Roles. Settings root resolves/canonicalizes the
organization and redirects to workspace settings. Do not render Teams,
Invitations, or API Keys links.

- [x] **Step 4: Implement update and delete flows**

Update form accepts name, slug, and newline/comma domains, normalizes only for
client preview, sends the generated request, and replaces the route with the
returned canonical key. Member role sees read-only fields. Delete requires exact
name; server capability and API remain authoritative; success navigates to
`/workspaces`.

- [x] **Step 5: Implement member directory and paging**

Show current actor separately, other members ordered as returned, outside-domain
summary/badges, explicit load-more, and no removal control. Use server-provided
capabilities plus per-row assignable roles for presentation.

- [x] **Step 6: Implement direct add and role update recovery**

Direct add accepts exact UUID user ID and role. On
`member_domain_acknowledgement_required`, show the allow-list/email warning and
retry only after explicit confirmation. Apply confirmed returned member/role
projection immediately. If a subsequent reload fails, show a separate retry that
calls only GET.

- [x] **Step 7: Run full web quality gates**

```bash
npm run format:check
npm run lint
npm run typecheck
npm run boundaries:check
npm test -- --runInBand
rm -rf .next
API_INTERNAL_BASE_URL=http://127.0.0.1:3001 \
PUBLIC_DEFAULT_LOCALE=en npm run build
test -f .next/standalone/server.js
```

Expected: all pass and standalone server exists.

- [x] **Step 8: Commit settings/member UI**

```bash
git add apps/web
git commit -m "feat: add organization settings and member management"
```

---

### Task 12: Deterministic Multi-User Playwright Acceptance

**Files:**

- Create: `apps/web/e2e/organizations.spec.ts`
- Create: `apps/web/e2e/support/generated-organizations-api.ts`
- Modify: `apps/web/e2e/support/generated-auth-api.ts`
- Modify: `apps/api/tests/Template.E2EHost/Program.cs`
- Modify: `apps/api/tests/Template.Api.Tests/Infrastructure/ApiWebApplicationFactory.cs`
- Modify: `apps/web/playwright.config.ts` only if the existing webServer contract needs the organization seed/cleanup behavior.

**Interfaces:**

- Consumes: complete API/UI slice and local automation auth.
- Produces: deterministic browser evidence using generated SDK-only setup/cleanup helpers.

- [x] **Step 1: Add failing Playwright scenarios**

Create one serial-safe file with isolated users and `try/finally` cleanup. Cover:

```ts
test("zero organization onboarding and first workspace", async ({ page }) => {
  await signInLocalAutomationUser(page, {
    name: "E2E Organization Owner",
  });
  try {
    await page.goto("/dashboard");
    await expect(page).toHaveURL("/welcome");
    await expect(
      page.getByRole("heading", { name: "Create your first workspace" }),
    ).toBeVisible();
    await page.getByRole("button", { name: "Create Workspace" }).click();
    await page.getByLabel("Workspace Name").fill("E2E Organization");
    await page.getByRole("button", { name: "Create", exact: true }).click();
    await expect(page).toHaveURL("/w/e2e-organization/dashboard");
  } finally {
    await cleanupLocalAutomationUser(page);
  }
});
```

Assertions must include no invitation/team/api-key links, UUID-to-slug root
canonicalization, domain acknowledgement, no member removal control, and local
cleanup organization count.

The second test creates owner and member in two browser contexts, adds the member
by the generated local-scenario user ID, confirms the domain warning, changes
the member role to admin, then verifies the member context has read-only
settings and no add/role/remove controls. The third test creates names
`E2E Slug` and `E2E-Slug`, asserts canonical slugs `e2e-slug` and
`e2e-slug-2`, switches from `/settings/users`, resolves an UUID root to the
slug dashboard, and verifies the destructive control is absent when only one
accessible organization would remain.

- [x] **Step 2: Run the new E2E file and observe RED**

```bash
cd apps/web
npm run e2e -- organizations.spec.ts
```

Expected: the earliest unimplemented orchestration/helper assertion fails for
the intended reason; do not weaken assertions.

- [x] **Step 3: Implement generated-only E2E helpers**

Use generated SDK operations for API-level setup where UI setup would obscure the
scenario. Never import raw request DTOs or call `fetch`. Keep each browser context
with its own cookie jar and CSRF flow.

- [x] **Step 4: Harden E2E host cleanup and readiness**

Ensure the host migrates `TemplateDbContext`, reset deletes organization rows in
FK-safe order, and local cleanup can remove sole-member orgs. Readiness must query
both `auth.users` and `organizations.organizations` without exposing schema detail.

- [x] **Step 5: Run focused and complete E2E**

```bash
npm run e2e -- organizations.spec.ts
npm run e2e
```

Expected: all deterministic tests pass; opt-in live OAuth tests remain skipped
according to their existing gate.

- [x] **Step 6: Commit full-stack acceptance**

```bash
git add apps/web/e2e apps/web/playwright.config.ts \
  apps/api/tests/Template.E2EHost apps/api/tests/Template.Api.Tests/Infrastructure
git commit -m "test: cover organization full-stack workflows"
```

---

### Task 13: Durable Documentation and Complete Verification

**Completion evidence (2026-07-30):** final observed counts/results are recorded
in `docs/aspnetcore-migration-plan.md` under **Acceptance evidence: итерация 5**;
all Task 13 mandatory gates passed, except that the separately recorded full
development audit remains the known 26-high tooling-only advisory graph while
the required production audit is clean.

**Files:**

- Modify: `docs/api-conventions.md`
- Modify: `docs/web-conventions.md`
- Modify: `docs/authentication-persistence-operations.md`
- Modify: `docs/aspnetcore-migration-plan.md`
- Modify: `docs/superpowers/plans/2026-07-30-organizations-membership-onboarding.md` only to mark completed checkboxes/evidence if execution tracking is retained.

**Interfaces:**

- Consumes: verified implementation and exact observed command output.
- Produces: iteration-5 status, scope, correspondence, operational commands, acceptance evidence, intentional differences, and iteration-6 gate.

- [x] **Step 1: Update durable decisions before claiming completion**

Document exact REST paths, role matrix, non-disclosing access, cursors, active
session FK, transaction rules, account cleanup, SSR renewal suppression, mutation
recovery, context rename, schemas, migration commands, and zero-org routing.

In the migration plan:

- set current iteration to 5;
- mark functional scope complete only after all gates pass;
- include the reference→API→UI→test table;
- state Teams/Invitations/API Keys/product dashboard remain out of scope;
- record the omitted invitation CTA and strengthened target differences;
- add exact test counts and command results, not expected values.

- [x] **Step 2: Run the mandatory .NET and EF gates**

```bash
dotnet restore Template.sln
dotnet build Template.sln --no-restore
dotnet test Template.sln --no-restore
dotnet format Template.sln --no-restore --verify-no-changes
dotnet ef migrations has-pending-model-changes \
  --project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --startup-project apps/api/src/Template.Api/Template.Api.csproj \
  --context TemplateDbContext
dotnet ef migrations script --idempotent \
  --project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --startup-project apps/api/src/Template.Api/Template.Api.csproj \
  --context TemplateDbContext > /tmp/template-iteration5-final.sql
test -s /tmp/template-iteration5-final.sql
dotnet list Template.sln package --vulnerable --include-transitive
```

Record exact project/test totals, warnings/errors, script bytes, and vulnerability
result.

- [x] **Step 3: Run deterministic contract gates**

```bash
dotnet build apps/api/src/Template.Api/Template.Api.csproj --no-restore \
  -p:OpenApiGenerateDocuments=true
sha256sum contracts/openapi/v1.json > /tmp/openapi-final-a.sha
dotnet build apps/api/src/Template.Api/Template.Api.csproj --no-restore \
  -p:OpenApiGenerateDocuments=true
sha256sum contracts/openapi/v1.json > /tmp/openapi-final-b.sha
diff -u /tmp/openapi-final-a.sha /tmp/openapi-final-b.sha
cd apps/web
npm run api:check
```

Record the common hash.

- [x] **Step 4: Run clean web, security, build, and E2E gates**

```bash
cd apps/web
npm ci
npm audit --omit=dev
npm run boundaries:check
npm run format:check
npm run lint
npm run typecheck
npm test -- --runInBand
rm -rf .next
API_INTERNAL_BASE_URL=http://127.0.0.1:3001 \
PUBLIC_DEFAULT_LOCALE=en npm run build
test -f .next/standalone/server.js
npm run e2e
```

Record suite/test counts and any opt-in skips. If full development `npm audit`
continues to show the already-documented tooling-only advisory graph, record the
current exact result separately without claiming it clean; production audit must
be clean.

- [x] **Step 5: Run repository and immutable-reference guards**

```bash
cd /Users/kroniak/Workspaces/github/contixly/templates/netcore-nextjs-shadcn
git diff --check
git diff --exit-code -- template/
git diff --exit-code origin/main...HEAD -- template/
git status --short -- template/
```

Expected: all guards pass and no reference path is changed.

- [x] **Step 6: Self-review and commit documentation/evidence**

```bash
npx --prefix apps/web prettier --check \
  docs/api-conventions.md \
  docs/web-conventions.md \
  docs/authentication-persistence-operations.md \
  docs/aspnetcore-migration-plan.md
git add docs
git commit -m "docs: complete organizations migration iteration"
```

Check staged scope before commit and ensure observed evidence matches the final
HEAD.

---

### Task 14: Ready PR and Automatic Review Loop

**Files:**

- Modify only files required by actionable review findings.
- Append each review round's observed evidence to `docs/aspnetcore-migration-plan.md` when behavior or verification changes.

**Interfaces:**

- Consumes: green Task 13 branch and repository automatic reviewer.
- Produces: pushed ready PR with no actionable review comments.

- [x] **Step 1: Perform final branch review before push**

```bash
git status --short --branch
git log --oneline origin/main..HEAD
git diff --stat origin/main...HEAD
git diff --exit-code origin/main...HEAD -- template/
```

Expected: clean tree, intentional commits, no reference diff.

- [x] **Step 2: Push and create a ready PR**

```bash
git push -u origin codex/iteration-5-organizations-membership
gh pr create --base main --head codex/iteration-5-organizations-membership \
  --title "Implement organizations, membership, and onboarding" \
  --body-file /tmp/iteration5-pr-body.md
```

The PR body must contain scope, correspondence table, security/transaction
summary, exact acceptance results, intentional differences, and out-of-scope
items. Do not pass `--draft`.

- [x] **Step 3: Wait for and inspect automatic review**

Use the connected GitHub integration or `gh` to read review status, review
threads, and check runs. Wait for the configured automatic reviewer rather than
self-posting a review. Classify each comment as actionable or non-actionable with
code/test evidence.

- [x] **Step 4: Fix each actionable review finding test-first**

For a behavior defect, add the smallest failing regression test, observe RED,
implement the fix, run the focused suite and the Task 13 gates affected by the
change. For documentation-only findings, run formatting, link/path, diff, and
reference guards. Do not accept a suggestion that violates the approved scope or
architecture; document the evidence in the PR response.

- [x] **Step 5: Commit, push, and repeat review rounds**

```bash
git diff --name-only --diff-filter=ACMR
git add -- $(git diff --name-only --diff-filter=ACMR)
git commit -m "fix: address organization review findings"
git push
```

After every push, wait for the automatic reviewer again. Continue until all
review threads are resolved and the latest review/check state contains no
actionable comments.

- [x] **Step 6: Record the final clean review state**

Update the migration-plan review evidence only with observed results, rerun
`git diff --check` and both `template/` guards, commit/push that evidence when it
changed, and verify the ready PR remains mergeable with required checks passing.

Historical round-4 observation on 2026-07-31 (superseded by round 5):

- Final branch review and both immutable-reference guards were clean before the
  controller pushed the iteration branch.
- PR #6 was created ready rather than draft. At the round-4 observation, its
  reviewed implementation head was
  `635b29262a344435af7d778f615297262f686e93`.
- Automatic review rounds 1–3 produced actionable findings that were
  classified, repaired test-first, verified, pushed, and resolved. The
  round-3 renewal lifecycle follow-up is included in the reviewed
  implementation head.
- Automatic review round 4 completed in issue comment `5137840074` at
  `2026-07-31T00:44:29Z` with “Didn't find any major issues. Hooray!” for the
  reviewed implementation head.
- GitHub reports all 13 review threads resolved. PR #6 is open, ready,
  mergeable, and not merged.
- GitHub returned no commit status contexts and no PR-triggered workflow runs
  for the reviewed implementation head, so there were no configured checks to
  report as passing or failing.
- This subsequent documentation-only evidence commit may sit on top of the
  reviewed implementation head after the controller pushes it; the controller
  will then run the automatic review again. No amended/future commit hash is
  claimed here.

Round 5 reopened Task 14 after automatic review of documentation head
`66d8f7cbcc552cfedd2afc1eb45c3c9e39103abc` produced three actionable P2 web
findings. The local fixer reproduced and repaired unordered allowed-domain dirty
comparison, stale same-id switcher detail reconciliation, and member-directory
hydration readiness. Focused and full acceptance evidence is recorded in
`docs/aspnetcore-migration-plan.md`. Step 5 remains controller-owned until the
fix commit is pushed and the three threads are resolved; Step 6 requires a new
automatic-review result and therefore is not yet complete.

Round 6 reopened Task 14 after automatic review of implementation head
`8c1ad3730a7f1e3604b40189f6c9a8fec427a8a0` produced four actionable findings.
The local fixer repaired mounted settings permission reconciliation, nullable
member-domain acknowledgement, the 100-item raw allowed-domain HTTP/OpenAPI
bound, and sixth-and-later generated-slug collisions. Step 5 remains
controller-owned until the round-6 fix is pushed and all four threads are
resolved; Step 6 still requires a fresh automatic-review result and no clean
state is claimed here.

Round 7 reopened Task 14 after automatic review of implementation head
`3730de44a5964199fdd7140b8cc406abe439430d` produced one actionable P2 web
finding. The local fixer reproduced and repaired mounted member-directory RSC
first-page reconciliation while retaining confirmed mutation overlays/order,
loaded continuation progress and its last cursor, active generated reads, and
GET-only recovery state. Step 5 remains controller-owned until the round-7 fix
is pushed and its thread is resolved; Step 6 still requires a fresh automatic
review result and no round-7 clean state is claimed here.

Round 8 reopened Task 14 after automatic review of implementation head
`9ad0f656da4558dc197781a2500005e9febd7359` produced two actionable P2
findings. The local fixer serialized
same-normalized-name claims across different shared actors with a
transaction-scoped PostgreSQL advisory namespace and added the client-side
100-distinct-domain field boundary after normalization/de-duplication. Step 5
remains controller-owned until the round-8 fix is pushed and both threads are
resolved; Step 6 still requires a fresh automatic review result and no round-8
clean state is claimed here.

Round 9 closed Task 14 for reviewed implementation head
`9508a0be5b0c546a592775bf553110f751821040`. Codex issue comment
`5139401641` at `2026-07-31T04:52:53Z` reported no major issues, all prior
review threads are resolved, and the controller observed PR #6 open, ready,
mergeable, and without configured PR checks. Exact final implementation
evidence is recorded in `docs/aspnetcore-migration-plan.md`: .NET 600/600, no
EF drift with a 22,767-byte pure `--output` idempotent script, clean NuGet and
production npm vulnerability gates, deterministic OpenAPI SHA-256
`212ed49adaa1a95d42fd407c89a14c3e08dff58cda6324a50ce2a22f6aed8251`,
Jest 51/51 suites and 344/344 tests, Next.js 19/19 plus standalone, E2E 14
passed/5 opt-in skipped, and clean immutable-reference guards.

Steps 5 and 6 are complete for that implementation head. The following
documentation-only closure commit deliberately claims neither its own future
hash nor an automatic-review result; the controller will push and re-review it.
