# API Keys and Public V1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build secure personal and organization API-key management plus the scoped machine-readable `/api/v1` surface from the approved iteration-7 design.

**Architecture:** Add an isolated API-key Domain/Application slice, PostgreSQL-backed management/authentication stores, and a custom ASP.NET Core authentication scheme. Existing resource GET routes become explicitly browser-or-key while management remains cookie/CSRF-only; Next.js uses only the generated REST SDK for two shared management pages.

**Tech Stack:** .NET 10, ASP.NET Core Minimal APIs, EF Core 10, Npgsql/PostgreSQL, xUnit v3, Next.js 16.2.11, React 19.2.8, TypeScript 6, next-intl, shadcn/Radix, Jest 30, Playwright 1.61, OpenAPI 3.1, `@hey-api/openapi-ts`.

## Global Constraints

- `template/` is immutable: read/search/compare only; never edit, format, move, or run migrations in it.
- Work only on branch `codex/iteration-7-api-keys`, created from fresh `origin/main`.
- Follow dependency direction `Domain ← Application ← Infrastructure/Api`; Domain has no infrastructure or HTTP dependency.
- ASP.NET Core owns `/api/**`, authorization, validation, business orchestration, PostgreSQL and external integration boundaries.
- `apps/web` uses generated REST SDK only; no Prisma, Better Auth, Server Actions, raw `fetch`, direct database access, or bearer storage.
- Browser management uses secure HttpOnly same-origin cookie plus CSRF; machine access uses only `x-api-key`.
- When `x-api-key` is present on a mixed route, invalid key authentication must not fall back to a valid browser cookie.
- Generate 256-bit credentials with `RandomNumberGenerator`; persist only SHA-256 plus a 16-character safe start; reveal raw credentials only from create/rotate.
- API-key lists use opaque cursor pagination, default 50 and accepted range 1..100; no management search/status filtering in iteration 7.
- Public collections retain the target `{ data }`, Problem Details and opaque-page contracts rather than reference arrays/error envelopes.
- Write a failing test before production behavior, run the focused test RED, implement minimally, then run focused GREEN.
- Do not add documents search, machine writes, Bearer/JWT, Redis, arbitrary scopes, YARP, Aspire, production deployment, or an OpenSpec change.
- Before completing, run all .NET, EF, OpenAPI, generated-client, web static/unit/build, Playwright, vulnerability, whitespace and immutable-reference gates from the approved design.

---

## File Structure

### New backend files

- `apps/api/src/Template.Domain/ApiKeys/ApiKeyId.cs` — typed UUID key identity.
- `apps/api/src/Template.Domain/ApiKeys/ApiKeyOwnerKind.cs` — user/organization owner enum.
- `apps/api/src/Template.Domain/ApiKeys/ApiKeyPolicy.cs` — names, scopes, presets, expiration and rate-limit rules.
- `apps/api/src/Template.Application/ApiKeys/ApiKeyModels.cs` — commands, results, safe DTO models and machine principal.
- `apps/api/src/Template.Application/ApiKeys/ApiKeyCursor.cs` — typed opaque management cursor.
- `apps/api/src/Template.Application/ApiKeys/ApiKeyManagementService.cs` — management orchestration.
- `apps/api/src/Template.Application/ApiKeys/ApiKeyAuthenticationService.cs` — credential verification/usage orchestration.
- `apps/api/src/Template.Application/ApiKeys/MachineApiService.cs` — machine organization/team query orchestration.
- `apps/api/src/Template.Application/ApiKeys/Ports/IApiKeyCredentialService.cs` — generate/hash/canonical-format port.
- `apps/api/src/Template.Application/ApiKeys/Ports/IApiKeyStore.cs` — management and quota persistence port.
- `apps/api/src/Template.Application/ApiKeys/Ports/IMachineApiStore.cs` — machine-safe read port.
- `apps/api/src/Template.Infrastructure/ApiKeys/ApiKeyEntity.cs` — EF row.
- `apps/api/src/Template.Infrastructure/ApiKeys/ApiKeyEntityConfiguration.cs` — table, constraints, FKs and indexes.
- `apps/api/src/Template.Infrastructure/ApiKeys/CryptographicApiKeyCredentialService.cs` — RNG/base64url/SHA-256 implementation.
- `apps/api/src/Template.Infrastructure/ApiKeys/EfApiKeyStore.cs` — transactional management and quota.
- `apps/api/src/Template.Infrastructure/ApiKeys/EfMachineApiStore.cs` — user/org principal read queries.
- `apps/api/src/Template.Infrastructure/Persistence/Migrations/20260802000000_ApiKeysPublicV1.cs` and `.Designer.cs` — additive migration.
- `apps/api/src/Template.Api/Authentication/ApiKeyAuthenticationDefaults.cs` — scheme/claim constants.
- `apps/api/src/Template.Api/Authentication/ApiKeyAuthenticationHandler.cs` — header authentication/challenge.
- `apps/api/src/Template.Api/Authentication/ApiKeyAuthorization.cs` — required-scope metadata/handler/principal reader.
- `apps/api/src/Template.Api/Features/ApiKeys/ApiKeyContracts.cs` — strict management and `/me` contracts.
- `apps/api/src/Template.Api/Features/ApiKeys/ApiKeyEndpointBoundary.cs` — validation/result mapping.
- `apps/api/src/Template.Api/Features/ApiKeys/ApiKeyEndpointModule.cs` — personal/org management and `/me` routes.
- `apps/api/src/Template.Api/Features/ApiKeys/ApiKeySecurityEvents.cs` — bounded structured audit.
- `apps/api/src/Template.Api/OpenApi/ApiKeyContractOperationTransformer.cs` — security, header and exact error contract.
- `apps/api/src/Template.Api/OpenApi/ApiKeyContractSchemaTransformer.cs` — closed enums/limits/reveal-once annotations.

### New backend tests

- `apps/api/tests/Template.Application.Tests/ApiKeys/ApiKeyPolicyTests.cs`
- `apps/api/tests/Template.Application.Tests/ApiKeys/ApiKeyCursorTests.cs`
- `apps/api/tests/Template.Application.Tests/ApiKeys/ApiKeyManagementServiceTests.cs`
- `apps/api/tests/Template.Application.Tests/ApiKeys/ApiKeyAuthenticationServiceTests.cs`
- `apps/api/tests/Template.Application.Tests/ApiKeys/MachineApiServiceTests.cs`
- `apps/api/tests/Template.Api.Tests/ApiKeys/ApiKeyPersistenceModelTests.cs`
- `apps/api/tests/Template.Api.Tests/ApiKeys/ApiKeyCredentialTests.cs`
- `apps/api/tests/Template.Api.Tests/ApiKeys/ApiKeyStoreTests.cs`
- `apps/api/tests/Template.Api.Tests/ApiKeys/ApiKeyConcurrencyTests.cs`
- `apps/api/tests/Template.Api.Tests/ApiKeys/ApiKeyEndpointTests.cs`
- `apps/api/tests/Template.Api.Tests/ApiKeys/ApiKeyAuthenticationTests.cs`
- `apps/api/tests/Template.Api.Tests/ApiKeys/MachineOrganizationEndpointTests.cs`
- `apps/api/tests/Template.Api.Tests/ApiKeys/MachineTeamEndpointTests.cs`
- `apps/api/tests/Template.Api.Tests/ApiKeys/ApiKeySecurityTests.cs`

### New web files

- `apps/web/src/features/api-keys/api-key-routes.ts` — personal/org UI routes.
- `apps/web/src/features/api-keys/api-key-options.ts` — closed UI options/preset metadata.
- `apps/web/src/lib/api/api-keys/server/load-api-keys.ts` — cookie-forwarded first page.
- `apps/web/src/lib/api/api-keys/browser/api-key-mutations.ts` — generated SDK + CSRF mutations.
- `apps/web/src/components/api-keys/api-key-management.tsx` — client state/reconciliation owner.
- `apps/web/src/components/api-keys/api-key-table.tsx` — paged safe rows.
- `apps/web/src/components/api-keys/api-key-create-dialog.tsx`
- `apps/web/src/components/api-keys/api-key-edit-dialog.tsx`
- `apps/web/src/components/api-keys/api-key-rotate-dialog.tsx`
- `apps/web/src/components/api-keys/api-key-revoke-dialog.tsx`
- `apps/web/src/components/api-keys/api-key-education.tsx`
- `apps/web/src/components/api-keys/api-key-secret-view.tsx`
- `apps/web/src/app/(site)/user/api-keys/page.tsx`, `loading.tsx`, `error.tsx`
- `apps/web/src/app/(site)/w/[organizationKey]/settings/api-keys/page.tsx`, `loading.tsx`, `error.tsx`
- matching `@organizationSwitcher` slot pages for both UI routes.
- `apps/web/src/messages/api-keys.en.json` and `.ru.json`.
- shadcn primitives `checkbox.tsx`, `dropdown-menu.tsx`, `switch.tsx`, `table.tsx`.

### New web tests/E2E

- `apps/web/test/features/api-keys/api-key-options.test.ts`
- `apps/web/test/components/api-keys/api-key-management.test.tsx`
- `apps/web/test/components/api-keys/api-key-create-dialog.test.tsx`
- `apps/web/test/components/api-keys/api-key-edit-dialog.test.tsx`
- `apps/web/test/components/api-keys/api-key-rotate-dialog.test.tsx`
- `apps/web/test/app/api-key-pages.test.tsx`
- `apps/web/test/contracts/api-key-boundaries.test.ts`
- `apps/web/e2e/support/generated-api-keys-api.ts`
- `apps/web/e2e/support/api-key-e2e-harness.ts`
- `apps/web/e2e/api-keys.spec.ts`

---

### Task 1: Domain Policy and Application Contracts

**Files:**

- Create all Domain/Application model, cursor, port and service files listed above.
- Modify: `apps/api/src/Template.Domain/Organizations/OrganizationPermissionPolicy.cs`
- Test: the five new `Template.Application.Tests/ApiKeys/*.cs` files.

**Interfaces:**

- Consumes: existing `UserId`, `OrganizationId`, `TeamId`, organization/team cursor conventions and `TimeProvider`.
- Produces:

```csharp
public readonly record struct ApiKeyId(Guid Value);
public enum ApiKeyOwnerKind { User, Organization }
public static class ApiKeyScopes
{
    public const string BasicRead = "basic:read";
    public const string OrganizationRead = "organization:read";
    public const string MemberRead = "member:read";
    public const string TeamRead = "team:read";
    public const string TeamMemberRead = "teamMember:read";
}

public sealed record ApiKeyOwner(
    ApiKeyOwnerKind Kind,
    UserId? UserId,
    OrganizationId? OrganizationId);

public sealed record ApiKeyCredentialMaterial(
    string Credential,
    byte[] Hash,
    string Start);

public interface IApiKeyCredentialService
{
    ApiKeyCredentialMaterial Generate(ApiKeyOwnerKind ownerKind);
    bool TryHashCanonical(string credential, out byte[] hash);
}

public interface IApiKeyStore
{
    Task<ApiKeyOperationResult<ApiKeyStorePage>> ListAsync(
        ApiKeyListQuery query, CancellationToken cancellationToken);
    Task<ApiKeyOperationResult<ApiKeySummary>> CreateAsync(
        CreateApiKeyStoreCommand command, CancellationToken cancellationToken);
    Task<ApiKeyOperationResult<ApiKeySummary>> UpdateAsync(
        UpdateApiKeyCommand command, CancellationToken cancellationToken);
    Task<ApiKeyOperationResult<ApiKeyRevocation>> RevokeAsync(
        RevokeApiKeyCommand command, CancellationToken cancellationToken);
    Task<ApiKeyOperationResult<ApiKeySummary>> RotateAsync(
        RotateApiKeyStoreCommand command, CancellationToken cancellationToken);
    Task<ApiKeyAuthenticationResult> AuthenticateAndConsumeAsync(
        byte[] hash, DateTimeOffset now, CancellationToken cancellationToken);
}
```

- [ ] **Step 1: Write failing policy tests**

Create `ApiKeyPolicyTests` asserting exact presets/scopes, trimmed 1..32 name rules,
expiry options `never|7d|30d|90d|365d`, rate windows `1m|1h|1d`, max range
`1..1_000_000`, and owner/admin/member `CanManageApiKeys` values.

```csharp
[Fact]
public void OrganizationReadAllExpandsToFourReadScopes() =>
    Assert.Equal(
        ["organization:read", "member:read", "team:read", "teamMember:read"],
        ApiKeyPolicy.ExpandPresets(["organization-read-all"]));
```

- [ ] **Step 2: Run policy tests RED**

Run:

```bash
dotnet test apps/api/tests/Template.Application.Tests/Template.Application.Tests.csproj \
  --no-restore --filter FullyQualifiedName~ApiKeyPolicyTests
```

Expected: compile failure because the API-key Domain types do not exist.

- [ ] **Step 3: Implement Domain types and capability**

Implement `ApiKeyId`, owner enum and `ApiKeyPolicy` with canonical sorted,
de-duplicated scope output. Extend `OrganizationCapabilities` with the final
constructor property `bool CanManageApiKeys`; return `true` for admin/owner and
`false` for member in `OrganizationPermissionPolicy.GetCapabilities`.

- [ ] **Step 4: Run policy and organization regression tests GREEN**

Run:

```bash
dotnet test apps/api/tests/Template.Application.Tests/Template.Application.Tests.csproj \
  --no-restore --filter 'FullyQualifiedName~ApiKeyPolicyTests|FullyQualifiedName~OrganizationDomainTests'
```

Expected: all selected tests pass.

- [ ] **Step 5: Write cursor and service tests RED**

Create exact tests for:

- cursor round-trip `(createdAt, ApiKeyId)`;
- wrong type/version, noncanonical base64url, checksum corruption and extra bytes;
- personal owner derived from actor;
- organization commands retain actor and organization owner separately;
- create returns credential once but store receives only hash/start;
- update rejects no-op, rotate returns the replacement credential, revoke maps terminal failure;
- authentication rejects noncanonical input before store and maps valid/rate-limited outcomes;
- machine scope sets for `/me`, organization, members, teams and team members.

Use fakes implementing the exact ports above; never place a sample raw key in an
exception assertion.

- [ ] **Step 6: Run service tests RED**

Run:

```bash
dotnet test apps/api/tests/Template.Application.Tests/Template.Application.Tests.csproj \
  --no-restore --filter FullyQualifiedName~ApiKeys
```

Expected: compile failures for the Application contracts/services.

- [ ] **Step 7: Implement models, ports, cursor and services**

Use these public service signatures:

```csharp
public sealed class ApiKeyManagementService(
    IApiKeyStore store,
    IApiKeyCredentialService credentials,
    TimeProvider timeProvider)
{
    public Task<ApiKeyOperationResult<ApiKeyPage>> ListAsync(
        ApiKeyListRequest request, CancellationToken cancellationToken);
    public Task<ApiKeyOperationResult<ApiKeySecret>> CreateAsync(
        CreateApiKeyCommand command, CancellationToken cancellationToken);
    public Task<ApiKeyOperationResult<ApiKeySummary>> UpdateAsync(
        UpdateApiKeyCommand command, CancellationToken cancellationToken);
    public Task<ApiKeyOperationResult<ApiKeyRevocation>> RevokeAsync(
        RevokeApiKeyCommand command, CancellationToken cancellationToken);
    public Task<ApiKeyOperationResult<ApiKeySecret>> RotateAsync(
        RotateApiKeyCommand command, CancellationToken cancellationToken);
}

public sealed class ApiKeyAuthenticationService(
    IApiKeyCredentialService credentials,
    IApiKeyStore store,
    TimeProvider timeProvider)
{
    public Task<ApiKeyAuthenticationResult> AuthenticateAsync(
        string credential, CancellationToken cancellationToken);
}
```

Define `ApiKeyFailure` as the closed set `InvalidName`, `InvalidPreset`,
`InvalidExpiration`, `InvalidRateLimit`, `InvalidCursor`, `PermissionDenied`,
`NotFound`, `Unchanged`, `ConcurrencyConflict`. Define authentication outcomes
`Succeeded`, `Invalid`, `RateLimited` with a nullable bounded retry duration.

- [ ] **Step 8: Run all API-key Application tests GREEN**

Run the Task 1 filter again. Expected: all selected tests pass.

- [ ] **Step 9: Run architecture boundaries**

Run:

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore --filter FullyQualifiedName~ArchitectureBoundaryTests
```

Expected: Domain/Application dependency tests pass.

- [ ] **Step 10: Commit Task 1**

```bash
git add apps/api/src/Template.Domain apps/api/src/Template.Application \
  apps/api/tests/Template.Application.Tests apps/api/tests/Template.Api.Tests/ArchitectureBoundaryTests.cs
git commit -m "feat: define api key domain and application contracts"
```

---

### Task 2: EF Model, Migration, and Credential Cryptography

**Files:**

- Create: `apps/api/src/Template.Infrastructure/ApiKeys/ApiKeyEntity.cs`
- Create: `apps/api/src/Template.Infrastructure/ApiKeys/ApiKeyEntityConfiguration.cs`
- Create: `apps/api/src/Template.Infrastructure/ApiKeys/CryptographicApiKeyCredentialService.cs`
- Create: `apps/api/src/Template.Infrastructure/Persistence/Migrations/20260802000000_ApiKeysPublicV1.cs`
- Create: `apps/api/src/Template.Infrastructure/Persistence/Migrations/20260802000000_ApiKeysPublicV1.Designer.cs`
- Modify: `apps/api/src/Template.Infrastructure/Persistence/TemplateDbContext.cs`
- Modify: `apps/api/src/Template.Infrastructure/Persistence/InfrastructureServiceCollectionExtensions.cs`
- Modify: `apps/api/src/Template.Infrastructure/Persistence/Migrations/TemplateDbContextModelSnapshot.cs`
- Test: `apps/api/tests/Template.Api.Tests/ApiKeys/ApiKeyPersistenceModelTests.cs`
- Test: `apps/api/tests/Template.Api.Tests/ApiKeys/ApiKeyCredentialTests.cs`

**Interfaces:**

- Consumes: Task 1 `IApiKeyCredentialService` and `ApiKeyCredentialMaterial`.
- Produces: registered `CryptographicApiKeyCredentialService`, `DbSet<ApiKeyEntity> ApiKeys`, and exact relational schema.

- [ ] **Step 1: Write EF model tests RED**

Assert table/schema, exactly-one-owner check, 32-byte hash, 16-character start,
allowed scope array, positive counters, both cascade FKs, unique hash and the two
partial owner-list indexes. Assert `TemplateDbContext` exposes `ApiKeys`.

- [ ] **Step 2: Write credential tests RED**

Assert 1000 generated values are unique, user keys start `user_`, organization
keys start `org_`, decoded random portion is 32 bytes, hash is 32 bytes, start is
16 characters, canonical credentials re-hash identically, and whitespace,
padding, wrong prefix, wrong decoded size or overlong input fails closed.

- [ ] **Step 3: Run focused tests RED**

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore --filter 'FullyQualifiedName~ApiKeyPersistenceModelTests|FullyQualifiedName~ApiKeyCredentialTests'
```

Expected: compile failures for missing Infrastructure types.

- [ ] **Step 4: Implement entity/configuration/credential service**

Use `RandomNumberGenerator.GetBytes(32)`, `WebEncoders.Base64UrlEncode`,
`SHA256.HashData(Encoding.UTF8.GetBytes(credential))`, and ordinal prefix checks.
The entity contains no `Key`/`Secret` string property.

- [ ] **Step 5: Wire DbContext and DI**

Add:

```csharp
public DbSet<ApiKeyEntity> ApiKeys => Set<ApiKeyEntity>();
```

Apply configuration after existing collaboration configurations and register:

```csharp
services.AddSingleton<IApiKeyCredentialService,
    CryptographicApiKeyCredentialService>();
```

- [ ] **Step 6: Generate and normalize the EF migration**

Run from repository root:

```bash
dotnet ef migrations add ApiKeysPublicV1 \
  --project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --startup-project apps/api/src/Template.Api/Template.Api.csproj \
  --context TemplateDbContext \
  --output-dir Persistence/Migrations
```

Rename the generated pair to `20260802000000_ApiKeysPublicV1.cs` and
`20260802000000_ApiKeysPublicV1.Designer.cs`, and set the designer migration ID
to exactly `20260802000000_ApiKeysPublicV1`. Inspect `Up`/`Down`; `Up` must be
additive and must not alter/delete any existing table.

- [ ] **Step 7: Run model/credential tests GREEN**

Run the Task 2 focused filter. Expected: all selected tests pass.

- [ ] **Step 8: Verify migration completeness**

```bash
dotnet ef migrations has-pending-model-changes \
  --project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --startup-project apps/api/src/Template.Api/Template.Api.csproj \
  --context TemplateDbContext
dotnet ef migrations script --idempotent \
  --project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --startup-project apps/api/src/Template.Api/Template.Api.csproj \
  --context TemplateDbContext > /tmp/template-iteration7-api-keys.sql
test -s /tmp/template-iteration7-api-keys.sql
```

Expected: no pending model changes; script is nonempty and contains
`auth.api_keys`, both owner FKs, exact-one-owner check, hash unique index and
partial list indexes.

- [ ] **Step 9: Commit Task 2**

```bash
git add apps/api/src/Template.Infrastructure apps/api/tests/Template.Api.Tests/ApiKeys
git commit -m "feat: persist hashed api key credentials"
```

---

### Task 3: Transactional API-Key Store

**Files:**

- Create: `apps/api/src/Template.Infrastructure/ApiKeys/EfApiKeyStore.cs`
- Modify: `apps/api/src/Template.Application/ApiKeys/ApiKeyManagementService.cs`
- Modify: `apps/api/src/Template.Infrastructure/Persistence/InfrastructureServiceCollectionExtensions.cs`
- Modify: `apps/api/tests/Template.Api.Tests/Accounts/AccountPersistenceTests.cs`
- Modify: `apps/api/tests/Template.Api.Tests/Organizations/OrganizationUserLifecycleTests.cs`
- Test: `apps/api/tests/Template.Api.Tests/ApiKeys/ApiKeyStoreTests.cs`
- Test: `apps/api/tests/Template.Api.Tests/ApiKeys/ApiKeyConcurrencyTests.cs`
- Test: `apps/api/tests/Template.Application.Tests/ApiKeys/ApiKeyManagementServiceTests.cs`

The account/organization test modifications only add cascade/lifecycle coverage;
database foreign keys remain the production cleanup mechanism.

**Interfaces:**

- Consumes: Task 1 `IApiKeyStore` commands/results and Task 2 entity.
- Produces: scoped `IApiKeyStore` implementing management and persisted quota.

- [ ] **Step 1: Write management store tests RED**

Against the real PostgreSQL fixture, assert:

- personal and organization create persist hash/start but not raw credential;
- descending `(createdAt,id)` pagination has no duplicates/skips;
- revoked rows are excluded;
- update is owner-qualified and rechecks current organization role;
- member cannot create/list/update/revoke/rotate organization keys;
- rotation changes hash/start, preserves ID/config/last request and resets window;
- revoke makes authentication invalid;
- user/org cascade behavior is exact;
- deleting the org-key creator does not remove the organization key.

- [ ] **Step 2: Run store tests RED**

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore --filter FullyQualifiedName~ApiKeyStoreTests
```

Expected: DI or `NotImplementedException` failures because `EfApiKeyStore` is absent.

- [ ] **Step 3: Implement management transactions**

Use explicit transactions and owner-qualified row locks. Organization operations
lock/read actor membership and require owner/admin inside every fresh attempt.
Inside the store, retry only SQLSTATE `40001`/`40P01`, to a maximum of three
complete transaction attempts. Map a unique hash collision to
`ConcurrencyConflict`; in `ApiKeyManagementService` create/rotate, generate a
fresh credential material and retry that operation, bounded to three materials,
so Application can return the matching reveal-once credential. Never retry
permission or validation failures, and never generate credential material inside
the store where the raw replacement could be lost.

- [ ] **Step 4: Run store tests GREEN**

Run the Task 3 store filter. Expected: all selected tests pass.

- [ ] **Step 5: Write quota/concurrency tests RED**

Use barriers and injected time to prove:

- exactly `rateLimitMax` concurrent authentications succeed in one live window;
- the next result is `RateLimited` with positive `RetryAfter`;
- the first request after window expiry resets to count 1;
- a valid key with later scope denial still consumed once;
- rotate-vs-use and revoke-vs-use serialize to one pre-commit or post-commit outcome;
- disabled, expired, revoked and unknown hash are indistinguishable `Invalid`.

- [ ] **Step 6: Run concurrency tests RED**

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore --filter FullyQualifiedName~ApiKeyConcurrencyTests
```

Expected: failures for missing quota implementation.

- [ ] **Step 7: Implement `AuthenticateAndConsumeAsync`**

Lock by unique hash; sample the supplied `now` once; evaluate terminal state;
reset stale windows; compute ceiling seconds for retry; increment and persist
count/last-request before returning a safe `ApiKeyPrincipal`. Do not materialize
name/hash into the principal.

- [ ] **Step 8: Run concurrency and lifecycle regressions GREEN**

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore --filter 'FullyQualifiedName~ApiKey|FullyQualifiedName~OrganizationUserLifecycleTests|FullyQualifiedName~AccountPersistenceTests'
```

Expected: all selected tests pass.

- [ ] **Step 9: Commit Task 3**

```bash
git add apps/api/src/Template.Infrastructure/ApiKeys \
  apps/api/src/Template.Infrastructure/Persistence/InfrastructureServiceCollectionExtensions.cs \
  apps/api/tests/Template.Api.Tests/ApiKeys \
  apps/api/tests/Template.Api.Tests/Organizations \
  apps/api/tests/Template.Api.Tests/Accounts
git commit -m "feat: add transactional api key store"
```

---

### Task 4: Browser-Session Management REST

**Files:**

- Create all `apps/api/src/Template.Api/Features/ApiKeys/*` files except machine-only additions supplied in Task 5.
- Modify: `apps/api/src/Template.Api/ApiHost.cs`
- Modify: `apps/api/src/Template.Api/Endpoints/EndpointModuleExtensions.cs`
- Modify: `apps/api/src/Template.Api/Errors/ApiProblemCodes.cs`
- Modify: `apps/api/src/Template.Api/Errors/ApiProblemDetailsDefaults.cs`
- Modify: `apps/api/src/Template.Api/Authentication/AuthResponseCacheMiddleware.cs`
- Modify: `apps/api/src/Template.Api/Features/Organizations/OrganizationContracts.cs`
- Modify: `apps/api/src/Template.Api/Features/Organizations/OrganizationEndpointModule.cs`
- Test: `apps/api/tests/Template.Api.Tests/ApiKeys/ApiKeyEndpointTests.cs`
- Test: `apps/api/tests/Template.Api.Tests/ApiKeys/ApiKeySecurityTests.cs`
- Test: `apps/api/tests/Template.Api.Tests/Organizations/OrganizationEndpointTests.cs`
- Test: `apps/api/tests/Template.Api.Tests/Organizations/OrganizationSecurityTests.cs`

**Interfaces:**

- Consumes: `ApiKeyManagementService`, browser session gateway, CSRF filter and Task 3 store.
- Produces: the ten personal/organization management operations and safe DTOs.

- [ ] **Step 1: Write endpoint contract tests RED**

Create tests for all personal and organization verbs, exact routes/statuses,
strict bodies, UUID validation, default/no-store headers, anonymous `401`, unsafe
CSRF `400`, owner/admin success, member `403`, missing/foreign `404`, semantic
no-op `409`, reveal-only create/rotate, and no secret/hash in list/update/revoke.

- [ ] **Step 2: Run endpoint tests RED**

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore --filter FullyQualifiedName~ApiKeyEndpointTests
```

Expected: routes return 404.

- [ ] **Step 3: Implement strict contracts and boundary**

Define exact request records:

```csharp
internal sealed record CreateApiKeyRequest(
    string? Name,
    IReadOnlyList<string>? PresetIds,
    string? ExpiresIn,
    bool? RateLimitEnabled,
    int? RateLimitMax,
    string? RateLimitWindow);

internal sealed record UpdateApiKeyRequest(
    string? Name,
    IReadOnlyList<string>? PresetIds,
    string? ExpiresIn,
    bool? Enabled,
    bool? RateLimitEnabled,
    int? RateLimitMax,
    string? RateLimitWindow);
```

Apply `JsonUnmappedMemberHandling.Disallow`, manual JSON reading and the exact
Task 1 policies. Define safe `ApiKeyResponse`, page, secret, revocation DTOs.

- [ ] **Step 4: Implement personal and organization endpoints**

Map personal routes under `/account/api-keys`; map organization routes under
`/organizations/{organizationId}/api-keys`. Require normal browser policy from
the versioned group and `.RequireApiAntiforgery()` on POST/PATCH/DELETE. Rotate
and DELETE call the existing empty-body validator.

- [ ] **Step 5: Add stable failure mapping/audit/cache behavior**

Add codes `api_key_not_found`, `api_key_permission_denied`,
`api_key_update_unchanged`, `api_key_missing`, `api_key_invalid`,
`api_key_rate_limited`, `organization_access_denied`. Use safe bounded events
from the design and extend no-store middleware for `/api/v1/account/api-keys`,
organization key-management paths and `/api/v1/me`.

- [ ] **Step 6: Register services/module and run endpoint tests GREEN**

Register `ApiKeyManagementService`, `ApiKeyAuthenticationService`, later
`MachineApiService`, and `ApiKeyEndpointModule`. Run Task 4 endpoint filter;
expected all pass.

- [ ] **Step 7: Run security/organization regressions**

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore --filter 'FullyQualifiedName~ApiKeySecurityTests|FullyQualifiedName~OrganizationEndpointTests|FullyQualifiedName~OrganizationSecurityTests'
```

Expected: safe logs, no-store, existing organization JSON and capability tests pass after adding `canManageApiKeys`.

- [ ] **Step 8: Commit Task 4**

```bash
git add apps/api/src/Template.Api apps/api/tests/Template.Api.Tests/ApiKeys \
  apps/api/tests/Template.Api.Tests/Organizations
git commit -m "feat: expose session-authenticated api key management"
```

---

### Task 5: API-Key Authentication Scheme and `/me`

**Files:**

- Create: `apps/api/src/Template.Api/Authentication/ApiKeyAuthenticationDefaults.cs`
- Create: `apps/api/src/Template.Api/Authentication/ApiKeyAuthenticationHandler.cs`
- Create: `apps/api/src/Template.Api/Authentication/ApiKeyAuthorization.cs`
- Modify: `apps/api/src/Template.Api/Authentication/AuthenticationServiceCollectionExtensions.cs`
- Modify: `apps/api/src/Template.Api/Authentication/ApiAuthenticationDefaults.cs`
- Modify: `apps/api/src/Template.Api/Authentication/ApiPolicies.cs`
- Modify: `apps/api/src/Template.Api/Features/ApiKeys/ApiKeyEndpointModule.cs` for `GET /api/v1/me`.
- Test: `apps/api/tests/Template.Api.Tests/ApiKeys/ApiKeyAuthenticationTests.cs`
- Test: `apps/api/tests/Template.Application.Tests/ApiKeys/ApiKeyAuthenticationServiceTests.cs`

**Interfaces:**

- Consumes: `ApiKeyAuthenticationService.AuthenticateAsync` and API-key Problem Details codes.
- Produces: schemes `Template.ApiKey`, `Template.Consumer.Selector`; policies `Api.MachineKey`, `Api.BrowserOrMachine`; scope metadata extension `RequireApiKeyScopes(params string[])`.

- [ ] **Step 1: Write authentication tests RED**

Assert missing, blank, multiple, malformed, unknown, disabled, expired, revoked,
rate-limited and valid headers; exact Problem Details; `Retry-After`; no cookie
issuance; claims contain only safe key/owner/scopes; `/me` rejects cookie-only.

- [ ] **Step 2: Write selector precedence tests RED**

Map a test mixed endpoint and assert:

- cookie without header authenticates browser;
- API key without cookie authenticates machine;
- valid cookie plus invalid key returns `api_key_invalid`;
- valid cookie plus valid key uses machine principal;
- scope forbid returns `api_key_permission_denied`, not browser `forbidden`.

- [ ] **Step 3: Run authentication tests RED**

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore --filter FullyQualifiedName~ApiKeyAuthenticationTests
```

Expected: scheme/policy registration failures.

- [ ] **Step 4: Implement schemes and claims**

Use one policy scheme selector:

```csharp
options.ForwardDefaultSelector = context =>
    context.Request.Headers.ContainsKey("x-api-key")
        ? ApiKeyAuthenticationDefaults.SchemeName
        : ApiAuthenticationDefaults.SchemeName;
```

Keep the existing default liveness selector unchanged. The API-key handler reads
exactly one header value, calls Application once, builds a principal with
authentication type `Template.ApiKey`, and writes failure-specific Problem
Details from challenge/forbid without echoing credentials.

- [ ] **Step 5: Implement scope authorization**

`ApiKeyScopeRequirement` succeeds automatically for the primary browser scheme;
for `Template.ApiKey` it requires every endpoint scope claim. Register one
singleton authorization handler and endpoint convention extension.

- [ ] **Step 6: Implement `/api/v1/me`**

Map through a machine-only route group. Require `basic:read`. Return owner kind,
nullable user/organization ID, key ID/start/config ID and canonical scopes in
`ApiResponse<ApiKeyMeResponse>`.

- [ ] **Step 7: Run authentication and auth regression tests GREEN**

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore --filter 'FullyQualifiedName~ApiKeyAuthenticationTests|FullyQualifiedName~AuthEndpointTests|FullyQualifiedName~BrowserSessionCookieRotationTests|FullyQualifiedName~HealthEndpointTests'
```

Expected: all selected tests pass; liveness still never touches PostgreSQL.

- [ ] **Step 8: Commit Task 5**

```bash
git add apps/api/src/Template.Api/Authentication \
  apps/api/src/Template.Api/Features/ApiKeys \
  apps/api/tests/Template.Api.Tests/ApiKeys
git commit -m "feat: authenticate scoped machine api keys"
```

---

### Task 6: Mixed Organization Machine Reads

**Files:**

- Create: `apps/api/src/Template.Infrastructure/ApiKeys/EfMachineApiStore.cs` organization query portion.
- Modify: `apps/api/src/Template.Api/Endpoints/EndpointRouteContext.cs`
- Modify: `apps/api/src/Template.Api/Endpoints/EndpointModuleExtensions.cs`
- Modify: `apps/api/src/Template.Api/Features/Organizations/OrganizationContracts.cs`
- Modify: `apps/api/src/Template.Api/Features/Organizations/OrganizationEndpointModule.cs`
- Modify: `apps/api/src/Template.Application/ApiKeys/MachineApiService.cs`
- Modify: `apps/api/src/Template.Application/ApiKeys/Ports/IMachineApiStore.cs`
- Modify: `apps/api/src/Template.Infrastructure/Persistence/InfrastructureServiceCollectionExtensions.cs`
- Test: `apps/api/tests/Template.Application.Tests/ApiKeys/MachineApiServiceTests.cs`
- Test: `apps/api/tests/Template.Api.Tests/ApiKeys/MachineOrganizationEndpointTests.cs`
- Test: `apps/api/tests/Template.Api.Tests/Organizations/OrganizationEndpointTests.cs`
- Test: `apps/api/tests/Template.Api.Tests/Organizations/OrganizationStoreTests.cs`

**Interfaces:**

- Consumes: Task 5 machine principal reader and policies.
- Produces: machine organization list/detail/member reads and additive `accessPrincipal`.

- [ ] **Step 1: Write machine organization tests RED**

Assert personal key list contains only current memberships; organization key
list contains exactly its owner organization; detail/members enforce scopes;
personal foreign membership and organization-key foreign ID return
`organization_access_denied`; valid cookie behavior and current page shape remain
unchanged; invalid key never falls back to cookie.

- [ ] **Step 2: Run focused tests RED**

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore --filter FullyQualifiedName~MachineOrganizationEndpointTests
```

Expected: browser policy denies keys and UUID detail route is absent.

- [ ] **Step 3: Add explicit route groups**

Extend `EndpointRouteContext` to carry:

```csharp
RouteGroupBuilder VersionedApi;          // browser only
RouteGroupBuilder VersionedMixedApi;     // browser or API key
RouteGroupBuilder VersionedMachineApi;   // API key only
```

All three share `/api/v1`; each has exactly one named policy. Move only the
overlapping GET mappings from browser group to mixed group. Mutations stay in
browser group.

- [ ] **Step 4: Implement machine organization store/service**

Use explicit projections and current membership checks. Organization-principal
queries never construct a fake `UserId`. Decode existing organization/member
cursors in Application and return the same bounded target pages.

- [ ] **Step 5: Branch endpoint handlers by trusted principal**

Use `ApiKeyPrincipalReader.TryRead(http.User, out principal)`. Browser branch
retains existing actor/session/audit code. Machine branch calls
`MachineApiService`, writes machine audit and maps:

- personal: `accessPrincipal = "user"`, real role/capabilities;
- organization: `accessPrincipal = "organization"`,
  `currentRole = "organization"`, all browser mutation capabilities false.

Add machine-only `GET /organizations/{organizationId}` with a safe detail DTO
that omits allowed-domain configuration.

- [ ] **Step 6: Run machine and browser organization tests GREEN**

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore --filter 'FullyQualifiedName~MachineOrganizationEndpointTests|FullyQualifiedName~OrganizationEndpointTests|FullyQualifiedName~OrganizationStoreTests'
```

Expected: all selected tests pass.

- [ ] **Step 7: Commit Task 6**

```bash
git add apps/api/src/Template.Application/ApiKeys \
  apps/api/src/Template.Infrastructure/ApiKeys \
  apps/api/src/Template.Api/Endpoints \
  apps/api/src/Template.Api/Features/Organizations \
  apps/api/tests/Template.Application.Tests/ApiKeys \
  apps/api/tests/Template.Api.Tests/ApiKeys \
  apps/api/tests/Template.Api.Tests/Organizations
git commit -m "feat: authorize machine organization reads"
```

---

### Task 7: Mixed Team Machine Reads and Scope Redaction

**Files:**

- Modify: `apps/api/src/Template.Infrastructure/ApiKeys/EfMachineApiStore.cs`
- Modify: `apps/api/src/Template.Application/ApiKeys/MachineApiService.cs`
- Modify: `apps/api/src/Template.Api/Features/Collaboration/TeamContracts.cs`
- Modify: `apps/api/src/Template.Api/Features/Collaboration/TeamEndpointModule.cs`
- Test: `apps/api/tests/Template.Api.Tests/ApiKeys/MachineTeamEndpointTests.cs`
- Test: `apps/api/tests/Template.Api.Tests/Collaboration/TeamEndpointTests.cs`
- Test: `apps/api/tests/Template.Api.Tests/Collaboration/TeamStoreTests.cs`
- Test: `apps/api/tests/Template.Api.Tests/Collaboration/TeamConcurrencyTests.cs`

**Interfaces:**

- Consumes: Task 6 machine principal/read store.
- Produces: scoped team/team-member reads and additive `membersIncluded`.

- [ ] **Step 1: Write machine team tests RED**

Assert:

- `team:read` lists teams but returns `membersIncluded=false` and empty embedded members;
- adding `teamMember:read` returns the first embedded page and `membersIncluded=true`;
- dedicated member route requires all three scopes;
- personal key membership is current;
- organization key cannot cross organizations;
- a team ID from another organization returns non-disclosing not-found;
- browser session responses remain `membersIncluded=true`.

- [ ] **Step 2: Run focused tests RED**

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore --filter FullyQualifiedName~MachineTeamEndpointTests
```

Expected: API-key calls denied or nested members leaked by the existing shape.

- [ ] **Step 3: Implement machine team store/service methods**

Qualify every query by organization. Project safe team fields and aggregate
member count. Only load embedded member rows when principal scopes contain
`teamMember:read`; the dedicated route always checks team ownership before rows.

- [ ] **Step 4: Move team GET routes to the mixed group**

Move only list teams and list team members. Keep team create/update/delete,
member add/remove and candidates browser-only. Add required API-key scope
metadata without changing browser authorization.

- [ ] **Step 5: Add `membersIncluded` mappings**

Append `bool MembersIncluded` to `TeamResponse`. Set `true` for every existing
browser mutation/list response. Machine list sets it from the actual projection.
Update exact OpenAPI/Jest consumers later through generation, never hand-edit
generated files here.

- [ ] **Step 6: Run team machine/browser regression GREEN**

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore --filter 'FullyQualifiedName~MachineTeamEndpointTests|FullyQualifiedName~TeamEndpointTests|FullyQualifiedName~TeamStoreTests|FullyQualifiedName~TeamConcurrencyTests'
```

Expected: all selected tests pass with no cross-scope nested member disclosure.

- [ ] **Step 7: Commit Task 7**

```bash
git add apps/api/src/Template.Application/ApiKeys \
  apps/api/src/Template.Infrastructure/ApiKeys \
  apps/api/src/Template.Api/Features/Collaboration \
  apps/api/tests/Template.Api.Tests/ApiKeys \
  apps/api/tests/Template.Api.Tests/Collaboration
git commit -m "feat: authorize machine team reads"
```

---

### Task 8: OpenAPI, Generated SDK, and Consumer Contract

**Files:**

- Create: `apps/api/src/Template.Api/OpenApi/ApiKeyContractOperationTransformer.cs`
- Create: `apps/api/src/Template.Api/OpenApi/ApiKeyContractSchemaTransformer.cs`
- Modify: `apps/api/src/Template.Api/OpenApi/OpenApiServiceCollectionExtensions.cs`
- Modify: `apps/api/src/Template.Api/OpenApi/OpenApiDefaults.cs`
- Modify: `apps/api/src/Template.Api/Authentication/ApiKeyAuthorization.cs`
- Modify: `apps/api/tests/Template.Api.Tests/OpenApiContractTests.cs`
- Modify: `contracts/openapi/v1.json`
- Regenerate: `apps/web/src/lib/api/generated/**`.
- Create: `docs/api-key-consumer-guide.md`.

**Interfaces:**

- Consumes: Tasks 4–7 final operations/DTOs.
- Produces: exact `apiKeyAuth`, security alternatives, generated management SDK and consumer guide.

- [ ] **Step 1: Write OpenAPI assertions RED**

Assert:

- `apiKeyAuth = { type: apiKey, in: header, name: x-api-key }`;
- `/me` and UUID detail advertise only `apiKeyAuth`;
- mixed GETs advertise `cookieAuth OR apiKeyAuth`;
- management advertises only cookie plus CSRF mutation header;
- every path/verb/status/error union is exact;
- enums/limits/pagination/reveal-once descriptions are exact;
- list/update/revoke schemas contain no `key`/hash; create/rotate contain `key`;
- `Retry-After` is documented on machine 429;
- `accessPrincipal` and `membersIncluded` are required additive fields.

- [ ] **Step 2: Run OpenAPI tests RED**

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore --filter FullyQualifiedName~OpenApiContractTests
```

Expected: missing scheme/operations/schema annotations.

- [ ] **Step 3: Implement bounded transformers**

Register operation/schema transformers after existing organization/collaboration
transformers. Match operations by endpoint metadata/name, not fragile path text
alone. Preserve existing cookie/local-only contracts.

- [ ] **Step 4: Run OpenAPI tests GREEN**

Run the Task 8 filter. Expected: all contract assertions pass.

- [ ] **Step 5: Export deterministically twice**

```bash
rm -f contracts/openapi/v1.json
dotnet build apps/api/src/Template.Api/Template.Api.csproj --no-restore \
  -p:OpenApiGenerateDocuments=true
cp contracts/openapi/v1.json /tmp/iteration7-openapi-first.json
rm -f contracts/openapi/v1.json
dotnet build apps/api/src/Template.Api/Template.Api.csproj --no-restore \
  -p:OpenApiGenerateDocuments=true
cmp /tmp/iteration7-openapi-first.json contracts/openapi/v1.json
```

Expected: byte-identical documents.

- [ ] **Step 6: Regenerate and verify the TypeScript SDK**

```bash
cd apps/web
npm run api:generate
npm run api:check
```

Expected: generated files current and deterministic.

- [ ] **Step 7: Write the consumer guide**

Document exact header syntax without a real secret, scope matrix, personal/org
principal semantics, cursor use, target envelopes, 401/403/429 handling,
`Retry-After`, reveal-once storage, rotate/revoke and explicit read-only scope.

- [ ] **Step 8: Commit Task 8**

```bash
git add apps/api/src/Template.Api/OpenApi \
  apps/api/tests/Template.Api.Tests/OpenApiContractTests.cs \
  contracts/openapi/v1.json apps/web/src/lib/api/generated \
  docs/api-key-consumer-guide.md
git commit -m "docs: publish api key consumer contract"
```

---

### Task 9: Personal API-Key Management UI

**Files:**

- Create all shared UI/API/options files and personal route files listed in File Structure.
- Modify: `apps/web/src/features/account/account-routes.ts`
- Modify: `apps/web/src/components/account/account-nav.tsx`
- Modify: `apps/web/src/i18n/messages.ts`
- Add shadcn primitives through the installed CLI.
- Test: all API-key files under `apps/web/test/features`, `apps/web/test/components`,
  `apps/web/test/app`, and `apps/web/test/contracts` listed in File Structure.

**Interfaces:**

- Consumes: Task 8 generated functions/types for personal management.
- Produces: functional `/user/api-keys` and reusable components for Task 10.

- [ ] **Step 1: Read installed Next/shadcn guidance**

Re-read the installed Next docs named in the design. Run:

```bash
cd apps/web
npx shadcn@latest info --json
npx shadcn@latest docs checkbox dropdown-menu switch table
```

Keep RSC data loading server-only and interactive state in the narrow client
management boundary.

- [ ] **Step 2: Write option/API-boundary tests RED**

Assert exact preset metadata/defaults, expiry/rate enums, generated SDK imports,
CSRF wrapper use, no raw fetch, no Server Action and no handwritten request DTO.

- [ ] **Step 3: Run option/boundary tests RED**

```bash
cd apps/web
npm test -- --runInBand --testPathPatterns='api-key-options|api-key-boundaries'
```

Expected: missing feature/API modules.

- [ ] **Step 4: Add UI primitives and API adapters**

```bash
cd apps/web
npx shadcn@latest add checkbox dropdown-menu switch table --yes
```

Implement server loader with forwarded cookie/correlation/renewal suppression.
Implement browser create/update/revoke/rotate with `runCsrfMutation`; implement
safe continuation GET directly through the generated client.

- [ ] **Step 5: Implement options/messages/routes/nav**

Personal defaults are `basic-read`, `30d`, rate enabled, max 1000, window `1h`.
Register `apiKeys` messages in English/Russian and add the account nav entry
before Danger.

- [ ] **Step 6: Write component tests RED**

Assert empty/list/status rows, create validation, one-time secret, copy/close,
edit/no-op, enable-disable, rotate reveal, revoke removal, load-more dedupe, stale
GET rejection, confirmed mutation precedence and failed-refresh retry without
mutation replay.

- [ ] **Step 7: Run component tests RED**

```bash
npm test -- --runInBand --testPathPatterns='api-key-management|api-key-create|api-key-edit|api-key-rotate'
```

Expected: missing components.

- [ ] **Step 8: Implement shared client components**

Keep raw `key` only in `ApiKeySecretView` state passed from the confirmed
create/rotate result. Clear on close/unmount. Use generated safe DTOs for rows,
the existing interaction-ready attribute, localized Problem Details and a
read-generation counter for stale GET suppression.

- [ ] **Step 9: Implement the personal route and slots**

Server page loads exactly the first page and renders the shared management
component. Add loading/error boundaries and the matching organization-switcher
slot page so parallel routing remains complete.

- [ ] **Step 10: Run personal UI focused GREEN**

```bash
npm test -- --runInBand --testPathPatterns='api-key|account-nav|messages'
npm run boundaries:check
npm run typecheck
```

Expected: focused tests, boundary scan and typecheck pass.

- [ ] **Step 11: Commit Task 9**

```bash
git add apps/web/src apps/web/test apps/web/package.json apps/web/package-lock.json
git commit -m "feat: add personal api key management ui"
```

---

### Task 10: Organization API-Key Management UI

**Files:**

- Create organization API-key route/loading/error/slot files.
- Modify: `apps/web/src/features/organizations/organization-routes.ts`
- Modify: `apps/web/src/components/organizations/organization-settings-nav.tsx`
- Modify: `apps/web/src/app/(site)/w/[organizationKey]/settings/layout.tsx`
- Modify shared components only for owner type/capabilities.
- Test: organization page/nav/capability and shared management tests.

**Interfaces:**

- Consumes: Task 9 shared management UI and Task 8 generated organization management SDK.
- Produces: owner/admin `/w/{key}/settings/api-keys`; member-hidden navigation and direct-call denial.

- [ ] **Step 1: Write organization UI tests RED**

Assert canonical organization route, owner/admin nav item, member-hidden nav,
organization default `organization-read-all`, educational personal-key link,
organization IDs supplied only from trusted page data, and all mutations use
organization generated operations.

- [ ] **Step 2: Run organization UI tests RED**

```bash
cd apps/web
npm test -- --runInBand --testPathPatterns='api-key-pages|organization-settings-nav|api-key-management'
```

Expected: missing route/nav support.

- [ ] **Step 3: Extend routes/navigation**

Add `settingsApiKeys(organizationKey)`. Pass `canManageApiKeys` from the trusted
organization detail projection to `OrganizationSettingsNav`. Do not infer it
from client route or role text.

- [ ] **Step 4: Implement organization server page**

Resolve existing organization by key, canonicalize to slug, require the API
loader result, and pass owner kind/id/key plus capabilities to the shared
component. Direct member access renders the existing safe error path from API
403; it does not fetch key rows.

- [ ] **Step 5: Run organization UI GREEN and full Jest**

```bash
npm test -- --runInBand --testPathPatterns='api-key|organization-settings-nav'
npm test -- --runInBand
npm run boundaries:check
npm run typecheck
```

Expected: focused and full Jest pass; boundaries/typecheck clean.

- [ ] **Step 6: Commit Task 10**

```bash
git add apps/web/src apps/web/test
git commit -m "feat: add organization api key management ui"
```

---

### Task 11: Black-Box API-Key Playwright Coverage

**Files:**

- Create: `apps/web/e2e/support/generated-api-keys-api.ts`
- Create: `apps/web/e2e/support/api-key-e2e-harness.ts`
- Create: `apps/web/e2e/api-keys.spec.ts`
- Modify: `apps/web/playwright.config.ts` only if timeout/project registration requires an exact API-key project tag; do not change default browser coverage.

**Interfaces:**

- Consumes: final generated SDK, local automation, organization/team UI and API-key management UI.
- Produces: black-box parity/security acceptance for iteration 7.

- [ ] **Step 1: Write failing personal/auth boundary scenarios**

Add tests for unauthenticated management page, missing/blank/invalid/cookie-only
`/me`, create/reveal/use/update/disable-enable/rotate/revoke personal key, and old
secret invalidation.

- [ ] **Step 2: Run personal E2E RED**

```bash
cd apps/web
npx playwright test e2e/api-keys.spec.ts --project=chromium --grep 'personal|auth boundary'
```

Expected: tests fail before helpers/scenarios are complete.

- [ ] **Step 3: Implement generated E2E helper**

Use the generated SDK with explicit `x-api-key` header and JSON parsing through
the generated client. UI helper locates accessible labels, captures the reveal
input once and never prints it in assertion messages/traces.

- [ ] **Step 4: Run personal E2E GREEN**

Run the Task 11 personal command. Expected: all selected scenarios pass.

- [ ] **Step 5: Write organization/scope scenarios RED**

Add owner/admin management, member denial, personal/org separation,
insufficient-scope denial, personal membership read-all, membership-loss
denial, organization owner isolation, creator role/removal survival, teams and
team-members scope redaction, and cursor continuation.

- [ ] **Step 6: Run organization/scope E2E RED then implement helper gaps**

```bash
npx playwright test e2e/api-keys.spec.ts --project=chromium --grep 'organization|scope|pagination'
```

Expected first run: deterministic failures identifying missing helper/UI flow.
Implement only harness/UI synchronization needed for those scenarios; do not
change production authorization to accommodate the test.

- [ ] **Step 7: Run focused and full E2E GREEN**

```bash
npx playwright test e2e/api-keys.spec.ts --project=chromium
npm run e2e
```

Expected: API-key file passes; full suite passes with only documented opt-in live-provider skips.

- [ ] **Step 8: Commit Task 11**

```bash
git add apps/web/e2e apps/web/playwright.config.ts
git commit -m "test: cover api key management and machine access"
```

---

### Task 12: Durable Documentation and Full Acceptance

**Files:**

- Modify: `docs/aspnetcore-migration-plan.md`
- Modify: `docs/api-conventions.md`
- Modify: `docs/web-conventions.md`
- Modify: `docs/authentication-persistence-operations.md`
- Modify: `docs/api-key-consumer-guide.md` if implementation evidence requires exact clarification.
- Modify: this plan checkboxes as tasks complete.

**Interfaces:**

- Consumes: exact implemented contract, migration ID, command output and PR/review state.
- Produces: iteration register, security/operations guidance, acceptance evidence and final verified branch.

- [ ] **Step 1: Update durable contract/security docs**

Record schema, key format/hash/reveal policy, auth selector precedence, schemes,
scope matrix, mixed routes, management routes, pagination, transactions, rate
limits, errors, audit redaction, UI routes and intentional reference differences.
Do not claim a future review result.

- [ ] **Step 2: Update migration plan scope/register**

Set current iteration to 7 while work is in progress; after all gates, mark the
functional scope complete with the exact implementation head and acceptance
table. Keep iterations 8+ unstarted and list documents search, machine writes,
Redis/Bearer/deploy items as out of scope.

- [ ] **Step 3: Run mandatory .NET gates**

```bash
dotnet restore Template.sln
dotnet build Template.sln --no-restore
dotnet test Template.sln --no-restore
dotnet format Template.sln --no-restore --verify-no-changes
```

Record exact project totals, failures/skips, warnings and elapsed results.

- [ ] **Step 4: Run EF and NuGet gates**

```bash
dotnet ef migrations has-pending-model-changes \
  --project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --startup-project apps/api/src/Template.Api/Template.Api.csproj \
  --context TemplateDbContext
dotnet ef migrations script --idempotent \
  --project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --startup-project apps/api/src/Template.Api/Template.Api.csproj \
  --context TemplateDbContext > /tmp/template-iteration7-final.sql
dotnet list Template.sln package --vulnerable --include-transitive
```

Expected: no pending model changes, nonempty inspected script, no vulnerable NuGet packages.

- [ ] **Step 5: Run deterministic contract/web gates**

```bash
rm -f contracts/openapi/v1.json
dotnet build apps/api/src/Template.Api/Template.Api.csproj --no-restore \
  -p:OpenApiGenerateDocuments=true
cp contracts/openapi/v1.json /tmp/iteration7-final-openapi.json
rm -f contracts/openapi/v1.json
dotnet build apps/api/src/Template.Api/Template.Api.csproj --no-restore \
  -p:OpenApiGenerateDocuments=true
cmp /tmp/iteration7-final-openapi.json contracts/openapi/v1.json
cd apps/web
npm run api:check
npm run boundaries:check
npm run format:check
npm run lint
npm run typecheck
npm test -- --runInBand
rm -rf .next
npm run build
test -f .next/standalone/server.js
npm run audit:prod
npm audit --json > /tmp/template-iteration7-npm-audit.json || true
npm run e2e
```

Expected: deterministic/current SDK, clean static/unit/build gates, zero
production npm vulnerabilities, full E2E pass with only documented live-provider
skips. Record development audit findings without hiding a nonzero result.

- [ ] **Step 6: Run repository/reference/OpenSpec guards**

```bash
cd ../../
git diff --check
git diff --check origin/main...HEAD
test -z "$(git status --short -- template/)"
test -z "$(git diff -- template/)"
test -z "$(git diff origin/main...HEAD -- template/)"
test -z "$(find openspec/changes -mindepth 1 -maxdepth 1 -type d 2>/dev/null)"
git status --short
```

Expected: no whitespace/reference/OpenSpec drift; only intentional tracked evidence changes remain.

- [ ] **Step 7: Commit final documentation/evidence**

```bash
git add docs contracts/openapi/v1.json
git commit -m "docs: complete api key migration evidence"
```

- [ ] **Step 8: Run verification-before-completion**

Re-run any command invalidated by the evidence commit, inspect `git diff
origin/main...HEAD`, confirm no untracked source, and record the exact final local
head. Do not mark review clean until the automatic reviewer has reviewed that
exact pushed head.

---

## Subagent Execution Order

Execute Tasks 1–8 sequentially because each publishes interfaces/contracts used
by the next task. After Task 8, Tasks 9 and the non-UI setup portion of Task 11
may be prepared in parallel only if they do not edit the same generated files;
Task 10 follows Task 9 shared components. Task 11 final scenarios follow both UI
tasks. Task 12 is last.

For every task, the controller must apply the subagent-driven two-stage gate:

1. spec compliance review against this plan and the approved design;
2. code-quality/security review after compliance is accepted;
3. focused verification and commit inspection before dispatching the next task.

The controller, not a worker, owns final push, ready PR creation, automatic-review
polling, thread resolution and repeated fix/push/re-review until the exact current
head has no actionable automatic-review comments.
