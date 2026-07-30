# Accounts and External OAuth Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver iteration 4 account lifecycle and five-provider OAuth through ASP.NET Core REST plus the separate Next.js UI.

**Architecture:** Domain and Application own verified-email, connection, session, and deletion rules; Infrastructure implements them with Identity, EF Core/PostgreSQL, OpenIddict Client, persistent cookie tickets, and Data Protection. `Template.Api` is the only OAuth/REST boundary, while `apps/web` uses the generated SDK and same-origin HttpOnly cookie.

**Tech Stack:** .NET SDK 10.0.302, ASP.NET Core/EF Core/Identity/Data Protection 10.0.10, Npgsql 10.0.3, OpenIddict 7.6.0, PostgreSQL 18.4, Next.js 16.2.11, React 19.2.8, TypeScript 6.0.3, Jest 30.4.2, Playwright 1.61.1.

## Global Constraints

- Read `AGENTS.md`, the approved design, migration plan, and installed Next.js docs before the corresponding task.
- `template/` is immutable: read/search/compare only; never format it, migrate it, or run its application.
- Preserve `Domain → Application → Infrastructure → Api`; Domain has no HTTP, EF, Identity, or OpenIddict dependencies.
- ASP.NET Core owns `/api/**`, auth, validation, business orchestration, data, and external integrations.
- Web uses generated REST SDK only: no Prisma, Better Auth, Server Actions, Route Handlers, direct database access, or browser token storage.
- Browser auth remains secure HttpOnly same-origin cookie plus `X-CSRF-TOKEN`; callback state replaces CSRF headers only on provider callbacks.
- Production password lifecycle, provider token persistence/API calls, organizations, invitations, API keys, Bearer, Aspire, YARP, KMS/Vault, and OpenSpec are out of scope.
- Implement every behavior RED → focused GREEN → broader GREEN; do not write production behavior before its failing test.
- Never commit OAuth secrets, PFX/private keys, passwords, state, codes, tokens, or `appsettings.Local.json`.
- Required final .NET commands are `dotnet restore Template.sln`, `dotnet build Template.sln --no-restore`, and `dotnet test Template.sln --no-restore`.

---

## File Structure

### Domain and Application

- `apps/api/src/Template.Domain/Accounts/ExternalProvider.cs` — closed five-provider identifiers.
- `apps/api/src/Template.Domain/Accounts/VerifiedEmail.cs` — bounded original/normalized verified email value.
- `apps/api/src/Template.Domain/Accounts/ExternalConnectionPolicy.cs` — link/disconnect decisions.
- `apps/api/src/Template.Application/Accounts/AccountModels.cs` — account, email, connection, page, external identity, and result records.
- `apps/api/src/Template.Application/Accounts/Ports/IExternalAccountStore.cs` — reconciliation persistence port.
- `apps/api/src/Template.Application/Accounts/Ports/IAccountStore.cs` — profile/connections/delete port.
- `apps/api/src/Template.Application/Accounts/Ports/IAccountSessionStore.cs` — session page/revoke port.
- `apps/api/src/Template.Application/Accounts/ExternalIdentityService.cs` — transactional sign-in/connect reconciliation.
- `apps/api/src/Template.Application/Accounts/AccountService.cs` — profile, connections, disconnect, delete.
- `apps/api/src/Template.Application/Accounts/AccountSessionService.cs` — list and revoke rules.
- `apps/api/src/Template.Application/Authentication/Ports/IBrowserSessionGateway.cs` — add authentication method and current-session renewal.

### Infrastructure

- `apps/api/src/Template.Infrastructure/Identity/ApplicationUserLogin.cs` — Identity login plus verified-email metadata.
- `apps/api/src/Template.Infrastructure/Persistence/UserEmailEntity.cs` — verified primary/secondary email rows.
- `apps/api/src/Template.Infrastructure/Accounts/EfExternalAccountStore.cs` — reconciliation store.
- `apps/api/src/Template.Infrastructure/Accounts/EfAccountStore.cs` — account/connection/delete store.
- `apps/api/src/Template.Infrastructure/Accounts/EfAccountSessionStore.cs` — session queries and revocation.
- `apps/api/src/Template.Infrastructure/Authentication/ExternalAuthenticationOptions.cs` — public origin/provider/certificate options.
- `apps/api/src/Template.Infrastructure/Authentication/ExternalProviderCatalog.cs` — configured/known provider catalogue.
- `apps/api/src/Template.Infrastructure/Authentication/ExternalIdentityNormalizer.cs` — five provider claim/user-info mappings.
- `apps/api/src/Template.Infrastructure/Authentication/OpenIddictClientServiceCollectionExtensions.cs` — client registrations/state store.
- `apps/api/src/Template.Infrastructure/Authentication/OpenIddictStateCleanupService.cs` — bounded expired/redeemed cleanup.
- `apps/api/src/Template.Infrastructure/Persistence/AuthDbContext.cs` — custom Identity login, emails, OpenIddict model.
- `apps/api/src/Template.Infrastructure/Persistence/Migrations/*_AccountsExternalOAuth.cs` — generated additive migration.
- `apps/api/src/Template.Infrastructure/Persistence/Migrations/AuthDbContextModelSnapshot.cs` — generated snapshot.

### API

- `apps/api/src/Template.Api/Features/Auth/ExternalAuthContracts.cs` — challenge/provider DTOs.
- `apps/api/src/Template.Api/Features/Auth/ExternalAuthEndpointModule.cs` — challenge and callback protocol endpoints.
- `apps/api/src/Template.Api/Features/Account/AccountContracts.cs` — account REST DTOs.
- `apps/api/src/Template.Api/Features/Account/AccountEndpointModule.cs` — profile/connections/session/delete REST.
- `apps/api/src/Template.Api/Authentication/ExternalOAuthChallengeService.cs` — OpenIddict challenge-to-URL bridge.
- `apps/api/src/Template.Api/Authentication/SafeReturnUrl.cs` — one canonical return-path policy.
- `apps/api/src/Template.Api/Features/Account/AccountSecurityEvents.cs` — PII-safe audit events.
- `apps/api/src/Template.Api/appsettings.Local.example.json` — secret-free local shape.
- `.gitignore`, `Directory.Packages.props`, project files, `ApiHost.cs`, auth rate-limit/options, endpoint registration, Problem codes, and appsettings — composition changes.

### Tests, contract, and web

- `apps/api/tests/Template.Application.Tests/Accounts/*Tests.cs` — domain/application RED/GREEN.
- `apps/api/tests/Template.Api.Tests/Accounts/*Tests.cs` — PostgreSQL/API/security tests.
- `apps/api/tests/Template.Api.Tests/Infrastructure/FakeOAuthServer.cs` — loopback authorization/token/user-info server.
- `contracts/openapi/v1.json` and `apps/web/src/lib/api/generated/**` — generated artifacts.
- `apps/web/src/app/(simple)/auth/error/page.tsx` — safe OAuth error page.
- `apps/web/src/app/(site)/user/**` — account layout, redirect, four pages, loading/error boundaries.
- `apps/web/src/components/authentication/external-provider-buttons.tsx` — challenge navigation.
- `apps/web/src/components/account/**` — profile, connections, sessions, danger UI.
- `apps/web/src/lib/api/account/**` — generated-client browser/server adapters.
- `apps/web/src/features/account/account-routes.ts` — typed routes.
- `apps/web/src/messages/account.{en,ru}.json` and auth message updates — localized copy.
- `apps/web/test/**` and `apps/web/e2e/account-settings.spec.ts`, `account-security.spec.ts`, `external-provider-smoke.spec.ts` — component and browser acceptance.

### Durable documentation

- `docs/api-conventions.md`, `docs/web-conventions.md`, `docs/authentication-persistence-operations.md` — final contracts/operations.
- `docs/aspnetcore-migration-plan.md` — iteration state and exact acceptance evidence.

---

### Task 1: Domain Values and Connection Policy

**Files:**

- Create: `apps/api/src/Template.Domain/Accounts/ExternalProvider.cs`
- Create: `apps/api/src/Template.Domain/Accounts/VerifiedEmail.cs`
- Create: `apps/api/src/Template.Domain/Accounts/ExternalConnectionPolicy.cs`
- Create: `apps/api/tests/Template.Application.Tests/Accounts/ExternalConnectionPolicyTests.cs`

**Interfaces:**

- Produces:

```csharp
public readonly record struct ExternalProvider(string Value)
{
    public static ExternalProvider Google { get; }
    public static ExternalProvider GitHub { get; }
    public static ExternalProvider GitLab { get; }
    public static ExternalProvider Vk { get; }
    public static ExternalProvider Yandex { get; }
    public static bool TryParse(string value, out ExternalProvider provider);
}

public sealed record VerifiedEmail(string Value, string NormalizedValue)
{
    public static VerifiedEmail Create(string value);
}

public static class ExternalConnectionPolicy
{
    public static EmailOwnershipDecision DecideEmailOwnership(
        UserId? currentUser, UserId? emailOwner);
    public static bool CanDisconnect(
        ExternalProvider? currentAuthenticationProvider,
        ExternalProvider candidate,
        int productionConnectionCount);
}
```

- [ ] **Step 1: Write the failing table tests**

```csharp
[Theory]
[InlineData("google")]
[InlineData("github")]
[InlineData("gitlab")]
[InlineData("vk")]
[InlineData("yandex")]
public void ProviderIdsAreClosedAndCanonical(string value) =>
    Assert.True(ExternalProvider.TryParse(value, out _));

[Fact]
public void DifferentFreeEmailCanBeAttachedAsSecondary() =>
    Assert.Equal(
        EmailOwnershipDecision.AttachSecondary,
        ExternalConnectionPolicy.DecideEmailOwnership(new UserId(Guid.NewGuid()), null));

[Fact]
public void CurrentOrLastProductionConnectionCannotBeDisconnected()
{
    Assert.False(ExternalConnectionPolicy.CanDisconnect(
        ExternalProvider.Google, ExternalProvider.Google, 2));
    Assert.False(ExternalConnectionPolicy.CanDisconnect(
        null, ExternalProvider.Google, 1));
    Assert.True(ExternalConnectionPolicy.CanDisconnect(
        null, ExternalProvider.Google, 2));
}
```

- [ ] **Step 2: Run RED**

Run: `dotnet test apps/api/tests/Template.Application.Tests/Template.Application.Tests.csproj --filter FullyQualifiedName~ExternalConnectionPolicyTests`

Expected: FAIL because `Template.Domain.Accounts` types do not exist.

- [ ] **Step 3: Implement the minimal immutable values and policy**

Normalize email with `Trim().ToUpperInvariant()`, reject empty/control-containing or longer-than-254 values, reject provider ids outside the five lowercase constants, and return explicit `ReuseCurrent`, `AttachSecondary`, or `ConflictWithOtherUser`.

- [ ] **Step 4: Run focused GREEN and architecture tests**

Run: `dotnet test apps/api/tests/Template.Application.Tests/Template.Application.Tests.csproj --filter "FullyQualifiedName~ExternalConnectionPolicyTests|FullyQualifiedName~Architecture"`

Expected: all selected tests PASS.

- [ ] **Step 5: Commit**

```bash
git add apps/api/src/Template.Domain/Accounts apps/api/tests/Template.Application.Tests/Accounts
git commit -m "feat: define external account domain rules"
```

### Task 2: Application Reconciliation Use Case

**Files:**

- Create: `apps/api/src/Template.Application/Accounts/AccountModels.cs`
- Create: `apps/api/src/Template.Application/Accounts/Ports/IExternalAccountStore.cs`
- Create: `apps/api/src/Template.Application/Accounts/ExternalIdentityService.cs`
- Create: `apps/api/tests/Template.Application.Tests/Accounts/ExternalIdentityServiceTests.cs`

**Interfaces:**

- Consumes: Task 1 values and existing `IAuthenticationUnitOfWork`.
- Produces:

```csharp
public enum ExternalAuthIntent { SignIn, Connect }
public sealed record ExternalIdentity(
    ExternalProvider Provider, string Subject, VerifiedEmail Email,
    string? DisplayName, Uri? ImageUrl);
public sealed record ExternalLoginSnapshot(
    UserId UserId, ExternalProvider Provider, string Subject,
    VerifiedEmail Email, DateTimeOffset ConnectedAt, DateTimeOffset? LastUsedAt);
public sealed record ExternalAuthentication(
    AuthUser User, ExternalProvider Provider, bool CreatedUser, bool AddedConnection);
public enum AccountFailure
{
    SessionRequired, EmailRequired, EmailUnverified,
    IdentityConflict, EmailConflict,
    ConnectionRequired, SessionNotFound, CurrentSessionCannotBeRevoked,
    ConfirmationMismatch, ConcurrencyConflict
}
public sealed record AccountOperationResult<T>(T? Value, AccountFailure? Failure)
    where T : class;
public sealed class AccountConcurrencyException : Exception;

public interface IExternalAccountStore
{
    Task<ExternalLoginSnapshot?> FindLoginAsync(ExternalProvider provider, string subject, CancellationToken ct);
    Task<AuthUser?> FindUserByEmailAsync(string normalizedEmail, CancellationToken ct);
    Task<AuthUser> CreateUserAsync(ExternalIdentity identity, CancellationToken ct);
    Task EnsureVerifiedEmailAsync(UserId userId, VerifiedEmail email, bool primary, CancellationToken ct);
    Task AddLoginAsync(UserId userId, ExternalIdentity identity, DateTimeOffset connectedAt, bool usedForSignIn, CancellationToken ct);
    Task UpdateLoginEmailAsync(UserId userId, ExternalIdentity identity, DateTimeOffset usedAt, CancellationToken ct);
    Task UpdateLinkedProfileAsync(UserId userId, string? displayName, Uri? imageUrl, CancellationToken ct);
}

public Task<AccountOperationResult<ExternalAuthentication>> ReconcileAsync(
    ExternalIdentity identity, ExternalAuthIntent intent,
    AuthenticatedSession? current, CancellationToken cancellationToken);
```

- [ ] **Step 1: Write failing fake-store tests**

Cover new anonymous user, primary/secondary implicit link, authenticated same-email connect, free secondary connect, other-owner conflict, existing subject, changed free email, changed email owned by another user, missing current connect session, one unique-conflict retry, and profile update only on new link.

```csharp
[Fact]
public async Task AnonymousVerifiedSecondaryEmailImplicitlyLinksItsOwner()
{
    var result = await Subject.ReconcileAsync(identity, ExternalAuthIntent.SignIn, null, Ct);
    Assert.Equal(owner.Id, result.Value!.User.Id);
    Assert.True(result.Value.AddedConnection);
    Assert.False(result.Value.CreatedUser);
}
```

- [ ] **Step 2: Run RED**

Run: `dotnet test apps/api/tests/Template.Application.Tests/Template.Application.Tests.csproj --filter FullyQualifiedName~ExternalIdentityServiceTests`

Expected: FAIL on missing models/service/port.

- [ ] **Step 3: Implement transaction orchestration**

Use one `IAuthenticationUnitOfWork.ExecuteAsync`, stable subject before email lookup, one bounded retry only for `AccountConcurrencyException`, and `AccountFailure` values matching the approved stable outcomes. Never issue a browser session inside the transaction.

- [ ] **Step 4: Run focused and full Application GREEN**

Run: `dotnet test apps/api/tests/Template.Application.Tests/Template.Application.Tests.csproj`

Expected: all Application tests PASS.

- [ ] **Step 5: Commit**

```bash
git add apps/api/src/Template.Application/Accounts apps/api/tests/Template.Application.Tests/Accounts
git commit -m "feat: reconcile external identities"
```

### Task 3: Application Account and Session Lifecycle

**Files:**

- Create: `apps/api/src/Template.Application/Accounts/Ports/IAccountStore.cs`
- Create: `apps/api/src/Template.Application/Accounts/Ports/IAccountSessionStore.cs`
- Create: `apps/api/src/Template.Application/Accounts/AccountService.cs`
- Create: `apps/api/src/Template.Application/Accounts/AccountSessionService.cs`
- Create: `apps/api/tests/Template.Application.Tests/Accounts/AccountServiceTests.cs`
- Create: `apps/api/tests/Template.Application.Tests/Accounts/AccountSessionServiceTests.cs`

**Interfaces:**

```csharp
public sealed record AccountSnapshot(
    AuthUser User, VerifiedEmail PrimaryEmail,
    IReadOnlyList<AccountEmail> Emails, DateTimeOffset CreatedAt);
public sealed record AccountConnection(
    ExternalProvider Provider, bool Configured, VerifiedEmail? Email,
    DateTimeOffset? ConnectedAt, DateTimeOffset? LastUsedAt);
public sealed record AccountSession(
    SessionId Id, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt,
    DateTimeOffset ExpiresAt, string AuthenticationMethod,
    string? IpAddress, string? UserAgent);
public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor);
public sealed record DisconnectSnapshot(
    UserId UserId, ExternalProvider Provider, VerifiedEmail Email,
    bool EmailIsPrimary, int ProductionConnectionCount);
public readonly record struct SessionCursor(DateTimeOffset LastSeenAt, SessionId Id)
{
    public static string Encode(SessionCursor value);
    public static bool TryDecode(string value, out SessionCursor cursor);
}

public interface IAccountStore
{
    Task<AccountSnapshot?> GetAsync(UserId userId, CancellationToken ct);
    Task<AccountSnapshot> UpdateDisplayNameAsync(UserId userId, string displayName, CancellationToken ct);
    Task<IReadOnlyList<AccountConnection>> ListConnectionsAsync(UserId userId, CancellationToken ct);
    Task<DisconnectSnapshot?> GetDisconnectSnapshotAsync(UserId userId, ExternalProvider provider, CancellationToken ct);
    Task DisconnectAsync(DisconnectSnapshot snapshot, CancellationToken ct);
    Task DeleteAsync(UserId userId, CancellationToken ct);
}

public interface IAccountSessionStore
{
    Task<CursorPage<AccountSession>> ListAsync(UserId userId, SessionCursor? cursor, int limit, CancellationToken ct);
    Task<bool> RevokeAsync(UserId userId, SessionId sessionId, CancellationToken ct);
    Task<int> RevokeOthersAsync(UserId userId, SessionId current, CancellationToken ct);
}
```

- [ ] **Step 1: Write failing lifecycle tests**

Assert name trim/2–50, configured-plus-existing connection projection, current/last disconnect rejection, orphan secondary-email removal, opaque cursor validation, foreign/missing session equivalence, current session rejection, revoke-others preservation, and normalized primary-email delete confirmation.

```csharp
[Fact]
public async Task CurrentSessionCannotBeRevoked()
{
    var result = await service.RevokeAsync(userId, currentSessionId, currentSessionId, Ct);
    Assert.Equal(AccountFailure.CurrentSessionCannotBeRevoked, result.Failure);
    Assert.Empty(store.RevokedSessionIds);
}
```

- [ ] **Step 2: Run RED**

Run: `dotnet test apps/api/tests/Template.Application.Tests/Template.Application.Tests.csproj --filter "FullyQualifiedName~AccountServiceTests|FullyQualifiedName~AccountSessionServiceTests"`

Expected: FAIL because services/ports are absent.

- [ ] **Step 3: Implement minimal services and cursor codec**

Use `SessionCursor.Encode(new SessionCursor(lastSeenAt, id))` and `TryDecode`; keep provider catalogue input explicit; make delete transaction complete before returning success.

- [ ] **Step 4: Run Application GREEN**

Run: `dotnet test apps/api/tests/Template.Application.Tests/Template.Application.Tests.csproj`

Expected: all tests PASS.

- [ ] **Step 5: Commit**

```bash
git add apps/api/src/Template.Application/Accounts apps/api/tests/Template.Application.Tests/Accounts
git commit -m "feat: add account lifecycle use cases"
```

### Task 4: EF Account Model, Migration, and Stores

**Files:**

- Modify: `Directory.Packages.props`
- Modify: `apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj`
- Create: `apps/api/src/Template.Infrastructure/Identity/ApplicationUserLogin.cs`
- Create: `apps/api/src/Template.Infrastructure/Persistence/UserEmailEntity.cs`
- Create: `apps/api/src/Template.Infrastructure/Accounts/EfExternalAccountStore.cs`
- Create: `apps/api/src/Template.Infrastructure/Accounts/EfAccountStore.cs`
- Create: `apps/api/src/Template.Infrastructure/Accounts/EfAccountSessionStore.cs`
- Modify: `apps/api/src/Template.Infrastructure/Persistence/AuthDbContext.cs`
- Modify: `apps/api/src/Template.Infrastructure/Persistence/InfrastructureServiceCollectionExtensions.cs`
- Create: `apps/api/src/Template.Infrastructure/Persistence/Migrations/*_AccountsExternalOAuth.cs`
- Modify: `apps/api/src/Template.Infrastructure/Persistence/Migrations/AuthDbContextModelSnapshot.cs`
- Create: `apps/api/tests/Template.Api.Tests/Accounts/AccountPersistenceTests.cs`

**Interfaces:**

- Implements the three Task 2/3 store ports.
- Adds `DbSet<UserEmailEntity> UserEmails` and custom `ApplicationUserLogin`.

- [ ] **Step 1: Add failing PostgreSQL model tests**

```csharp
[Fact]
public async Task MigrationBackfillsOnePrimaryEmailPerExistingUser()
{
    await db.Database.MigrateAsync("20260724142511_InitialAuthPersistence");
    var userId = Guid.CreateVersion7();
    db.Users.Add(new ApplicationUser
    {
        Id = userId,
        UserName = "owner@example.test",
        NormalizedUserName = "OWNER@EXAMPLE.TEST",
        Email = "owner@example.test",
        NormalizedEmail = "OWNER@EXAMPLE.TEST",
        EmailConfirmed = true,
        DisplayName = "Owner",
        CreatedAt = now,
        UpdatedAt = now
    });
    await db.SaveChangesAsync();

    await db.Database.MigrateAsync();

    var email = await db.UserEmails.SingleAsync();
    Assert.Equal(userId, email.UserId);
    Assert.True(email.IsPrimary);
    Assert.Equal("OWNER@EXAMPLE.TEST", email.NormalizedEmail);
}
```

Also assert one partial-primary index, cascades, connection timestamps, no token columns, concurrent link classification, and delete cascade.

- [ ] **Step 2: Run RED**

Run: `dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj --filter FullyQualifiedName~AccountPersistenceTests`

Expected: FAIL because entities/migration/stores are missing.

- [ ] **Step 3: Add packages and implement mappings/stores**

Add central/package references for `OpenIddict.EntityFrameworkCore` 7.6.0 and `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` 10.0.10. Change `AuthDbContext` to the custom `IdentityUserContext` generic overload and implement `IDataProtectionKeyContext`; map `auth.user_emails`, extended `auth.user_logins`, `auth.data_protection_keys`, and OpenIddict EF entities; call `options.UseOpenIddict()` in the Npgsql configuration; preserve Identity keys; and map known `PostgresException.SqlState == PostgresErrorCodes.UniqueViolation` to `AccountConcurrencyException`.

- [ ] **Step 4: Generate and inspect the additive migration**

Run:

```bash
dotnet ef migrations add AccountsExternalOAuth \
  --project apps/api/src/Template.Infrastructure \
  --startup-project apps/api/src/Template.Api \
  --context AuthDbContext
```

Inspect SQL for backfill before `NOT NULL`/FK enforcement, global normalized-email uniqueness, partial primary index, provider uniqueness, cascades, and no destructive changes to iteration-3 user/session data.

- [ ] **Step 5: Run persistence GREEN**

Run: `dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj --filter "FullyQualifiedName~AccountPersistenceTests|FullyQualifiedName~AuthPersistenceTests|FullyQualifiedName~IdentityGatewayTests"`

Expected: all selected tests PASS.

- [ ] **Step 6: Commit**

```bash
git add Directory.Packages.props apps/api/src/Template.Infrastructure apps/api/tests/Template.Api.Tests/Accounts
git commit -m "feat: persist verified emails and provider connections"
```

### Task 5: Authentication Method and Session Revocation Infrastructure

**Files:**

- Modify: `apps/api/src/Template.Application/Authentication/AuthModels.cs`
- Modify: `apps/api/src/Template.Application/Authentication/Ports/IBrowserSessionGateway.cs`
- Modify: `apps/api/src/Template.Infrastructure/Authentication/BrowserSessionClaimTypes.cs`
- Modify: `apps/api/src/Template.Infrastructure/Authentication/BrowserSessionGateway.cs`
- Modify: `apps/api/src/Template.Infrastructure/Authentication/PostgresTicketStore.cs`
- Create: `apps/api/tests/Template.Api.Tests/Accounts/AccountSessionPersistenceTests.cs`
- Modify: existing browser session tests as required by the signature.

**Interfaces:**

```csharp
Task<BrowserSession> SignInAsync(
    AuthUser user, string authenticationMethod, CancellationToken cancellationToken);
Task<BrowserSession> RenewCurrentAsync(CancellationToken cancellationToken);
```

`BrowserSession` gains `AuthenticationMethod`; allowed values are `local` and the five provider ids.

- [ ] **Step 1: Write failing ticket/session tests**

Assert local fallback for old tickets, provider claim round-trip, connect renewal preserves current method/session, account page never exposes ticket/hash, ownership-qualified delete, current revoke rejection, and revoke-others count.

```csharp
[Fact]
public async Task RenewCurrentPreservesProviderAndSessionId()
{
    var before = await gateway.GetCurrentAsync(Ct);
    var renewed = await gateway.RenewCurrentAsync(Ct);
    Assert.Equal(before!.Session.Id, renewed.Id);
    Assert.Equal("github", renewed.AuthenticationMethod);
}
```

- [ ] **Step 2: Run RED**

Run: `dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj --filter "FullyQualifiedName~AccountSessionPersistenceTests|FullyQualifiedName~BrowserSession"`

Expected: new tests FAIL on missing method metadata/operations.

- [ ] **Step 3: Implement claim and gateway changes**

Add exactly one bounded authentication-method claim when issuing; validate it while reading and project unknown/legacy as `local`. Reuse existing ticket protection and `ExecuteDeleteAsync`; do not deserialize tickets for list responses.

- [ ] **Step 4: Run GREEN**

Run: `dotnet test Template.sln --no-restore --filter "FullyQualifiedName~BrowserSession|FullyQualifiedName~AccountSession"`

Expected: selected tests PASS.

- [ ] **Step 5: Commit**

```bash
git add apps/api/src/Template.Application/Authentication apps/api/src/Template.Infrastructure/Authentication apps/api/tests
git commit -m "feat: manage authenticated account sessions"
```

### Task 6: Persistent Data Protection and Local Secret Overlay

**Files:**

- Create: `apps/api/src/Template.Infrastructure/Authentication/DataProtectionOptions.cs`
- Modify: `apps/api/src/Template.Infrastructure/Persistence/AuthDbContext.cs`
- Modify: `apps/api/src/Template.Infrastructure/Persistence/InfrastructureServiceCollectionExtensions.cs`
- Modify: `apps/api/src/Template.Api/ApiHost.cs`
- Modify: `apps/api/src/Template.Api/appsettings.json`
- Create: `apps/api/src/Template.Api/appsettings.Local.example.json`
- Modify: `.gitignore`
- Create: `apps/api/tests/Template.Api.Tests/Accounts/DataProtectionPersistenceTests.cs`

**Interfaces:**

```csharp
public sealed class DataProtectionOptions
{
    public const string SectionName = "DataProtection";
    public string ApplicationName { get; init; } = "Template";
    public string? CertificatePath { get; init; }
    public string? CertificatePassword { get; init; }
}
```

- [ ] **Step 1: Write failing key-ring tests**

Assert two API hosts sharing PostgreSQL can read the same protected payload, test certificate makes stored XML not contain plaintext key material, Production startup fails without certificate, Test/Development can start, and `appsettings.Local.json` is loaded only in Development.

```csharp
[Fact]
public async Task SharedDatabaseKeyRingUnprotectsAcrossHosts()
{
    var payload = hostOne.Services.GetRequiredService<IDataProtectionProvider>()
        .CreateProtector("cross-host").Protect("expected");
    var actual = hostTwo.Services.GetRequiredService<IDataProtectionProvider>()
        .CreateProtector("cross-host").Unprotect(payload);
    Assert.Equal("expected", actual);
}
```

- [ ] **Step 2: Run RED**

Run: `dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj --filter FullyQualifiedName~DataProtectionPersistenceTests`

Expected: FAIL because keys are ephemeral/default.

- [ ] **Step 3: Implement the key ring and overlay**

Before service registration, call `builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)` only when `builder.Environment.IsDevelopment()`. Make `AuthDbContext` implement `IDataProtectionKeyContext`, then configure `PersistKeysToDbContext<AuthDbContext>()`, `SetApplicationName("Template")`, and Production-only `ProtectKeysWithCertificate(new X509Certificate2(path, password))` with fail-closed option validation.

- [ ] **Step 4: Add local files safely**

Add the exact ignored path `apps/api/src/Template.Api/appsettings.Local.json`; commit only the `.example.json` hierarchy for public origin, five provider ids/secrets, and Data Protection fields. Verify `git check-ignore -v apps/api/src/Template.Api/appsettings.Local.json`.

- [ ] **Step 5: Run GREEN and secret checks**

Run:

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj --filter FullyQualifiedName~DataProtectionPersistenceTests
git grep -n -E '(client_secret|BEGIN PRIVATE KEY)' -- ':!template/**' ':!docs/**'
```

Expected: tests PASS; grep finds no credential/private-key value.

- [ ] **Step 6: Commit**

```bash
git add .gitignore Directory.Packages.props apps/api/src/Template.Api apps/api/src/Template.Infrastructure apps/api/tests/Template.Api.Tests/Accounts
git commit -m "feat: persist and protect authentication keys"
```

### Task 7: OpenIddict Client and Five Provider Normalization

**Files:**

- Modify: `Directory.Packages.props`
- Modify: `apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj`
- Create: `apps/api/src/Template.Infrastructure/Authentication/ExternalAuthenticationOptions.cs`
- Create: `apps/api/src/Template.Infrastructure/Authentication/ExternalProviderCatalog.cs`
- Create: `apps/api/src/Template.Infrastructure/Authentication/ExternalIdentityNormalizer.cs`
- Create: `apps/api/src/Template.Infrastructure/Authentication/OpenIddictClientServiceCollectionExtensions.cs`
- Create: `apps/api/src/Template.Infrastructure/Authentication/OpenIddictStateCleanupService.cs`
- Modify: `apps/api/src/Template.Infrastructure/Persistence/AuthDbContext.cs`
- Modify: `apps/api/src/Template.Infrastructure/Persistence/InfrastructureServiceCollectionExtensions.cs`
- Create: `apps/api/tests/Template.Api.Tests/Accounts/ExternalProviderConfigurationTests.cs`
- Create: `apps/api/tests/Template.Api.Tests/Accounts/ExternalIdentityNormalizerTests.cs`
- Create: `apps/api/tests/Template.Api.Tests/Accounts/OpenIddictStateTests.cs`

**Interfaces:**

```csharp
public interface IExternalProviderCatalog
{
    IReadOnlyList<ExternalProviderDescriptor> Known { get; }
    bool IsConfigured(ExternalProvider provider);
    string GetAuthenticationScheme(ExternalProvider provider);
}
public sealed record ExternalProviderDescriptor(
    ExternalProvider Provider, string DisplayName, bool Configured);
public sealed record ExternalIdentityResult(
    ExternalIdentity? Identity, AccountFailure? Failure)
{
    public bool Succeeded => Failure is null;
}

public interface IExternalIdentityNormalizer
{
    Task<ExternalIdentityResult> NormalizeAsync(
        ExternalProvider provider, ClaimsPrincipal principal,
        IReadOnlyDictionary<string, string> ephemeralTokens,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 1: Write provider/config RED tests**

Cover complete/incomplete blocks, lowercase catalogue, exact callback paths, Google/GitLab verified claims, GitHub primary+verified selection, VK scoped email, Yandex `default_email`, missing/unverified rejection, HTTPS-only avatar, stable numeric/string subjects, and no token persistence.

```csharp
[Fact]
public async Task GoogleRejectsUnverifiedEmail()
{
    var result = await normalizer.NormalizeAsync(
        ExternalProvider.Google,
        Principal(("sub", "123"), ("email", "owner@example.test"),
            ("email_verified", "false")),
        EmptyTokens,
        Ct);
    Assert.Equal(AccountFailure.EmailUnverified, result.Failure);
}
```

- [ ] **Step 2: Run RED**

Run: `dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj --filter "FullyQualifiedName~ExternalProvider|FullyQualifiedName~OpenIddictState"`

Expected: FAIL because provider infrastructure is absent.

- [ ] **Step 3: Add exact OpenIddict packages and registrations**

Add central/package references:

```xml
<PackageVersion Include="OpenIddict.Client.AspNetCore" Version="7.6.0" />
<PackageVersion Include="OpenIddict.Client.SystemNetHttp" Version="7.6.0" />
<PackageVersion Include="OpenIddict.Client.WebIntegration" Version="7.6.0" />
```

Reuse the Task 4 `OpenIddict.EntityFrameworkCore` reference. Register Google/GitHub/GitLab/VK Web Integration and a Yandex custom registration, authorization-code flow with PKCE where supported, EF state-token storage, ASP.NET Core callback pass-through, and Data Protection token format. Set redirect URIs from the validated public origin and the five exact approved paths.

- [ ] **Step 4: Implement normalization and bounded cleanup**

Request only approved scopes. Use tokens only to call user-info during normalization and clear references afterward. Run cleanup hourly, deleting at most 500 expired records or terminal redeemed records older than 24 hours per pass. Never register OpenIddict Server, token issuance, offline access, or provider-token columns.

- [ ] **Step 5: Run GREEN**

Run: `dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj --filter "FullyQualifiedName~ExternalProvider|FullyQualifiedName~OpenIddictState"`

Expected: all selected tests PASS, including replay rejection.

- [ ] **Step 6: Commit**

```bash
git add Directory.Packages.props apps/api/src/Template.Infrastructure apps/api/tests/Template.Api.Tests/Accounts
git commit -m "feat: configure external oauth providers"
```

### Task 8: OAuth Challenge and Callback HTTP Boundary

**Files:**

- Create: `apps/api/src/Template.Api/Features/Auth/ExternalAuthContracts.cs`
- Create: `apps/api/src/Template.Api/Authentication/SafeReturnUrl.cs`
- Create: `apps/api/src/Template.Api/Authentication/ExternalOAuthChallengeService.cs`
- Create: `apps/api/src/Template.Api/Features/Auth/ExternalAuthEndpointModule.cs`
- Modify: `apps/api/src/Template.Api/Features/Auth/AuthContracts.cs`
- Modify: `apps/api/src/Template.Api/Features/Auth/AuthEndpointModule.cs`
- Modify: `apps/api/src/Template.Api/Authentication/AuthSecurityServiceCollectionExtensions.cs`
- Modify: `apps/api/src/Template.Api/Endpoints/EndpointModuleExtensions.cs`
- Modify: `apps/api/src/Template.Api/Errors/ApiProblemCodes.cs`
- Create: `apps/api/tests/Template.Api.Tests/Infrastructure/FakeOAuthServer.cs`
- Create: `apps/api/tests/Template.Api.Tests/Accounts/ExternalAuthEndpointTests.cs`

**Interfaces:**

```csharp
internal sealed record ExternalAuthChallengeRequest(
    ExternalAuthIntent Intent, string? ReturnUrl);
internal sealed record ExternalAuthChallengeResponse(string AuthorizationUrl);

internal static bool SafeReturnUrl.TryNormalize(
    string? candidate, string fallback, out string normalized);
```

- [ ] **Step 1: Write endpoint RED tests**

Assert configured capabilities, CSRF required for both intents, sign-in requires anonymous, connect requires session, unsafe returns rejected, challenge response is 200/no-store with provider URL, exact callback methods/paths, state replay rejected, connect user/session mismatch rejected, conflict codes safely redirected, raw provider text absent, and successful sign-in/connect cookie behavior.

```csharp
[Fact]
public async Task CallbackStateIsRedeemedOnlyOnce()
{
    var callback = await oauth.CompleteAuthorizationAsync(client, "google");
    using var first = await client.GetAsync(callback);
    using var replay = await client.GetAsync(callback);
    Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
    Assert.Equal("/auth/error?code=external_auth_failed",
        replay.Headers.Location!.OriginalString);
}
```

- [ ] **Step 2: Run RED**

Run: `dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj --filter FullyQualifiedName~ExternalAuthEndpointTests`

Expected: 404/missing-operation failures.

- [ ] **Step 3: Implement challenge-to-URL bridge**

Invoke the provider forwarding scheme with `AuthenticationProperties.Items` containing intent, return path, and connect user/session ids. Capture the OpenIddict 302 `Location` before the response starts, remove the header/status, and return it in the JSON envelope. Assert exactly one HTTPS/provider authorization URL; fail closed otherwise.

- [ ] **Step 4: Implement callbacks**

Authenticate the OpenIddict client result, normalize ephemeral identity, revalidate connect context, call `ExternalIdentityService`, commit, then sign in with provider method or renew connect session preserving its method. Redirect only to stored safe path or `/auth/error?code=<allow-listed-code>`.

- [ ] **Step 5: Add rate limits and PII-safe audit assertions**

Add a challenge fixed window of 20 requests/minute/IP and callback fixed window of 60 requests/5 minutes/IP plus callback concurrency limit 10/queue 0; bind all values to positive validated options. Log provider/outcome/correlation and post-auth user id only; test that email, subject, state, code, token, cookie, and raw error never appear.

- [ ] **Step 6: Run endpoint and regression GREEN**

Run: `dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj --filter "FullyQualifiedName~ExternalAuthEndpointTests|FullyQualifiedName~AuthEndpointTests|FullyQualifiedName~ObservabilityTests"`

Expected: all selected tests PASS.

- [ ] **Step 7: Commit**

```bash
git add apps/api/src/Template.Api apps/api/tests/Template.Api.Tests
git commit -m "feat: expose external oauth flow"
```

### Task 9: Account REST Boundary

**Files:**

- Create: `apps/api/src/Template.Api/Features/Account/AccountContracts.cs`
- Create: `apps/api/src/Template.Api/Features/Account/AccountEndpointModule.cs`
- Create: `apps/api/src/Template.Api/Features/Account/AccountSecurityEvents.cs`
- Modify: `apps/api/src/Template.Api/Endpoints/EndpointModuleExtensions.cs`
- Modify: `apps/api/src/Template.Api/Errors/ApiProblemCodes.cs`
- Create: `apps/api/tests/Template.Api.Tests/Accounts/AccountEndpointTests.cs`
- Create: `apps/api/tests/Template.Api.Tests/Accounts/AccountSecurityTests.cs`
- Modify: `apps/api/tests/Template.Api.Tests/Infrastructure/ApiWebApplicationFactory.cs`

**Interfaces:**

```csharp
internal sealed record UpdateProfileRequest(string DisplayName);
internal sealed record DeleteAccountRequest(string ConfirmationEmail);
internal sealed record AccountResponse(
    Guid Id, string DisplayName, string PrimaryEmail, string? ImageUrl,
    DateTimeOffset CreatedAt, IReadOnlyList<AccountEmailResponse> VerifiedEmails);
internal sealed record AccountSessionsResponse(
    IReadOnlyList<AccountSessionResponse> Items, string? NextCursor);
```

- [ ] **Step 1: Write all account endpoint RED tests**

For every route assert authorization, CSRF on mutations, no-store, strict JSON/unmapped-member rejection, exact validation, safe projections, configured-plus-existing connections, disconnect conflicts, pagination limits/cursor, foreign session 404, current session 409, revoke-others preservation, delete mismatch, cascade, cookie expiry, and stable Problem Details.

```csharp
[Fact]
public async Task ForeignSessionUsesTheSameNotFoundProblemAsMissingSession()
{
    using var foreign = await DeleteSessionAsync(ownerClient, foreignSessionId);
    using var missing = await DeleteSessionAsync(ownerClient, Guid.CreateVersion7());
    Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    Assert.Equal(
        await ReadProblemCodeAsync(missing),
        await ReadProblemCodeAsync(foreign));
}
```

- [ ] **Step 2: Run RED**

Run: `dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj --filter "FullyQualifiedName~AccountEndpointTests|FullyQualifiedName~AccountSecurityTests"`

Expected: missing endpoint failures.

- [ ] **Step 3: Map the account module**

Map the eight approved versioned routes under the existing default `BrowserSession` group, use `ApiJsonRequestReader`, `RequireApiAntiforgery()` on mutations, limit 1–100/default 20, and return only `{ data }` envelopes. Redact IPv4 to `/24` (last octet zero), IPv6 to `/64` (lower 64 bits zero), and project an unparseable address as null. Add the exact design codes: `invalid_cursor`, `invalid_return_url`, `external_provider_not_configured`, `external_email_required`, `external_email_unverified`, `already_authenticated`, `external_auth_failed`, `external_identity_conflict`, `external_email_conflict`, `external_connection_required`, `external_connection_not_found`, `account_session_not_found`, `current_session_cannot_be_revoked`, and `oauth_flow_context_changed`.

- [ ] **Step 4: Add safe audit events and reset support**

Extend factory cleanup order for OpenIddict state, connections, emails, sessions, users, and Data Protection isolation without deleting shared key rows during individual tests.

- [ ] **Step 5: Run API GREEN**

Run: `dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj`

Expected: complete API test project PASS.

- [ ] **Step 6: Commit**

```bash
git add apps/api/src/Template.Api apps/api/tests/Template.Api.Tests
git commit -m "feat: expose account lifecycle rest api"
```

### Task 10: OpenAPI Contract and Generated SDK

**Files:**

- Modify: `apps/api/tests/Template.Api.Tests/OpenApiContractTests.cs`
- Modify: `apps/api/src/Template.Api/OpenApi/ApiContractSchemaTransformer.cs`
- Modify: `contracts/openapi/v1.json`
- Regenerate: `apps/web/src/lib/api/generated/**`
- Modify: `apps/web/test/contracts/generated-sdk.test.ts`
- Modify: `apps/web/test/typecheck/auth-contract.typecheck.ts`

**Interfaces:**

- Versioned challenge/account endpoints appear in OpenAPI.
- Unversioned provider callbacks remain absent from generated UI SDK.

- [ ] **Step 1: Write contract RED assertions**

Assert operation ids, cookie security, challenge/account schemas, enums, limits, required properties, strict request bodies, Problem Details responses, and callback absence.

```csharp
[Fact]
public async Task ProtocolCallbacksAreNotPublishedInVersionedOpenApi()
{
    var json = await client.GetStringAsync("/api/openapi/v1.json");
    Assert.DoesNotContain("/api/auth/callback/", json, StringComparison.Ordinal);
    Assert.Contains("/api/v1/account/sessions", json, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run RED**

Run: `dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj --filter FullyQualifiedName~OpenApiContractTests`

Expected: contract assertions FAIL until metadata is complete.

- [ ] **Step 3: Complete metadata and export**

Run:

```bash
dotnet build apps/api/src/Template.Api/Template.Api.csproj \
  --no-restore -p:OpenApiGenerateDocuments=true
cd apps/web && npm run api:generate
```

- [ ] **Step 4: Run contract GREEN**

Run:

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj --filter FullyQualifiedName~OpenApiContractTests
cd apps/web && npm run api:check && npm test -- --runInBand test/contracts/generated-sdk.test.ts
```

Expected: deterministic contract/SDK checks PASS.

- [ ] **Step 5: Commit**

```bash
git add contracts/openapi apps/api/src/Template.Api/OpenApi apps/api/tests/Template.Api.Tests/OpenApiContractTests.cs apps/web/src/lib/api/generated apps/web/test
git commit -m "feat: publish account api contract"
```

### Task 11: Next.js API Adapters, Routes, and Authentication UI

**Files:**

- Create: `apps/web/src/lib/api/auth/browser/start-external-auth.ts`
- Modify: `apps/web/src/lib/api/auth/load-auth-capabilities.ts`
- Create: `apps/web/src/components/authentication/external-provider-buttons.tsx`
- Modify: `apps/web/src/components/authentication/login-runtime.tsx`
- Create: `apps/web/src/app/(simple)/auth/error/page.tsx`
- Modify: `apps/web/src/features/authentication/authentication-routes.ts`
- Modify: `apps/web/src/features/application/application-routes.ts`
- Modify: `apps/web/src/messages/auth.en.json`
- Modify: `apps/web/src/messages/auth.ru.json`
- Create: `apps/web/test/components/external-provider-buttons.test.tsx`
- Modify: `apps/web/test/components/login-runtime.test.tsx`
- Create: `apps/web/test/app/auth-error-page.test.tsx`
- Modify: adapter/route/i18n tests.

**Interfaces:**

```ts
export async function startExternalAuth(input: {
  provider: ExternalProvider;
  intent: ExternalAuthIntent;
  returnUrl: string;
}): Promise<ApiResult<ExternalAuthChallengeResponse>>;
```

- [ ] **Step 1: Read installed Next.js docs**

Run:

```bash
sed -n '1,240p' apps/web/node_modules/next/dist/docs/01-app/02-guides/authentication.md
sed -n '1,220p' apps/web/node_modules/next/dist/docs/01-app/01-getting-started/05-server-and-client-components.md
sed -n '1,220p' apps/web/node_modules/next/dist/docs/01-app/01-getting-started/10-error-handling.md
sed -n '1,220p' apps/web/node_modules/next/dist/docs/01-app/02-guides/redirecting.md
```

- [ ] **Step 2: Write adapter/component RED tests**

Assert generated CSRF/challenge calls, `window.location.assign`, configured provider buttons, double-click disabling, local panel coexistence, known error-code localization, unknown safe fallback, and no arbitrary query echo.

```tsx
it("navigates only to the API-issued authorization URL", async () => {
  startExternalAuthMock.mockResolvedValue({
    ok: true,
    data: {
      authorizationUrl:
        "https://accounts.google.com/o/oauth2/v2/auth?state=safe",
    },
  });
  render(
    <ExternalProviderButtons providers={[google]} returnUrl="/dashboard" />,
  );
  await user.click(
    screen.getByRole("button", { name: "Continue with Google" }),
  );
  expect(window.location.assign).toHaveBeenCalledWith(
    "https://accounts.google.com/o/oauth2/v2/auth?state=safe",
  );
});
```

- [ ] **Step 3: Run RED**

Run: `cd apps/web && npm test -- --runInBand test/components/external-provider-buttons.test.tsx test/components/login-runtime.test.tsx test/app/auth-error-page.test.tsx`

Expected: missing module/component failures.

- [ ] **Step 4: Implement with generated SDK only**

Follow existing browser client/result normalization; do not add `fetch`, Server Actions, Route Handlers, token parsing, or callback logic. Use buttons from `capabilities.providers`.

- [ ] **Step 5: Run GREEN and boundaries**

Run: `cd apps/web && npm test -- --runInBand && npm run boundaries:check`

Expected: Jest and source-boundary checks PASS.

- [ ] **Step 6: Commit**

```bash
git add apps/web/src apps/web/test
git commit -m "feat: add external provider login ui"
```

### Task 12: Account API Adapters and Protected Layout

**Files:**

- Create: `apps/web/src/lib/api/account/server/load-account.ts`
- Create: `apps/web/src/lib/api/account/server/load-connections.ts`
- Create: `apps/web/src/lib/api/account/server/load-sessions.ts`
- Create: `apps/web/src/lib/api/account/browser/account-mutations.ts`
- Modify: `apps/web/src/lib/api/result.ts`
- Create: `apps/web/src/features/account/account-routes.ts`
- Create: `apps/web/src/app/(site)/user/layout.tsx`
- Create: `apps/web/src/app/(site)/user/page.tsx`
- Create: `apps/web/src/components/account/account-nav.tsx`
- Create: `apps/web/test/lib/api/account-api.test.ts`
- Create: `apps/web/test/features/account-routes.test.ts`
- Create: `apps/web/test/components/account-nav.test.tsx`

**Interfaces:**

```ts
export const accountRoutes = {
  root: "/user",
  profile: "/user/profile",
  connections: "/user/connections",
  security: "/user/security",
  danger: "/user/danger",
} as const;
```

- [ ] **Step 1: Write adapter/layout RED tests**

Assert SSR cookie/correlation forwarding, browser CSRF on every mutation, no-store generated operations, `/user` redirect, exactly four nav entries, active route semantics, and unauthorized redirect to `/auth/login?redirect=%2Fuser%2Fprofile`.

```tsx
it("renders only iteration-four account destinations", () => {
  render(<AccountNav pathname="/user/profile" />);
  expect(screen.getAllByRole("link").map((link) => link.textContent)).toEqual([
    "Profile",
    "Connections",
    "Security",
    "Danger",
  ]);
});
```

- [ ] **Step 2: Run RED**

Run: `cd apps/web && npm test -- --runInBand test/lib/api/account-api.test.ts test/features/account-routes.test.ts test/components/account-nav.test.tsx`

Expected: missing adapters/routes/components.

- [ ] **Step 3: Implement adapters/layout**

Reuse `createServerApiClient`, `readForwardedApiHeaders`, `getAuthCsrf`, `normalizeApiFailure`, typed `Route`, and existing protected-page server auth gate. No optimistic auth state and no raw network code.

- [ ] **Step 4: Run GREEN**

Run: `cd apps/web && npm test -- --runInBand && npm run boundaries:check`

Expected: tests PASS.

- [ ] **Step 5: Commit**

```bash
git add apps/web/src apps/web/test
git commit -m "feat: add protected account settings shell"
```

### Task 13: Profile, Connections, Security, and Danger Pages

**Files:**

- Create: `apps/web/src/components/ui/input.tsx`
- Create: `apps/web/src/components/ui/label.tsx`
- Create: `apps/web/src/components/ui/dialog.tsx`
- Create: `apps/web/src/components/ui/separator.tsx`
- Create: `apps/web/src/app/(site)/user/{profile,connections,security,danger}/page.tsx`
- Create: page-specific loading/error files under the four directories.
- Create: `apps/web/src/components/account/profile-form.tsx`
- Create: `apps/web/src/components/account/connections-list.tsx`
- Create: `apps/web/src/components/account/session-list.tsx`
- Create: `apps/web/src/components/account/delete-account-dialog.tsx`
- Create: `apps/web/src/messages/account.en.json`
- Create: `apps/web/src/messages/account.ru.json`
- Modify: `apps/web/src/i18n/messages.ts`
- Create: `apps/web/test/components/{profile-form,connections-list,session-list,delete-account-dialog}.test.tsx`
- Create: `apps/web/test/app/account-pages.test.tsx`
- Modify: `apps/web/test/i18n/messages.test.ts`

**Interfaces:**

- Consumes Task 12 adapters/routes and generated DTOs.

- [ ] **Step 1: Write page/component RED tests**

Assert read-only primary/secondary email/id/date, 2–50 trimmed name, configured/connected/current/disabled connection states, cursor “load more”, current session non-revocable, revoke-others, exact confirmation input, successful home navigation, failure display, loading/error boundaries, and en/ru bundle shape.

```tsx
it("does not offer revoke for the current session", () => {
  render(
    <SessionList initialPage={{ items: [currentSession], nextCursor: null }} />,
  );
  expect(screen.getByText("Current session")).toBeVisible();
  expect(screen.queryByRole("button", { name: "Revoke session" })).toBeNull();
});
```

- [ ] **Step 2: Run RED**

Run: `cd apps/web && npm test -- --runInBand test/components/profile-form.test.tsx test/components/connections-list.test.tsx test/components/session-list.test.tsx test/components/delete-account-dialog.test.tsx test/app/account-pages.test.tsx`

Expected: missing UI failures.

- [ ] **Step 3: Add required shadcn primitives**

Run from `apps/web`: `npx --no-install shadcn add input label dialog separator`.
Review generated imports/style and keep only these four primitives.

- [ ] **Step 4: Implement the four vertical pages**

Server components load projections; focused client components perform generated-SDK mutations. Derive browser/OS label only for display, render server-provided disconnect reason, and never trust UI-disabled state for authorization.

- [ ] **Step 5: Run web GREEN**

Run:

```bash
cd apps/web
npm run format:check
npm run lint
npm run typecheck
npm test -- --runInBand
npm run boundaries:check
```

Expected: all commands PASS.

- [ ] **Step 6: Commit**

```bash
git add apps/web/src apps/web/test apps/web/components.json apps/web/package-lock.json
git commit -m "feat: add account settings pages"
```

### Task 14: Full-Stack and Live Authorization-Screen E2E

**Files:**

- Modify: `apps/api/tests/Template.E2EHost/Program.cs`
- Modify: `apps/web/e2e/support/generated-auth-api.ts`
- Create: `apps/web/e2e/account-settings.spec.ts`
- Create: `apps/web/e2e/account-security.spec.ts`
- Create: `apps/web/e2e/external-provider-smoke.spec.ts`
- Modify: `apps/web/playwright.config.ts`

**Interfaces:**

- E2E host accepts the named `ExternalAuthentication__PublicOrigin` and per-provider `ClientId`/`ClientSecret` environment variables but never reads `template/.env`.

- [ ] **Step 1: Write E2E scenarios before host support**

Port reference behaviors: root redirect/profile update, provider state, delete mismatch/success, two browser contexts, revoke one, revoke all others preserving current, safe session projection. Add five sequential opt-in tests that assert configured button and navigation to the provider authorization/login host without submitting credentials.

```ts
test("revoke all others preserves the current browser", async ({
  browser,
  page,
}) => {
  const other = await browser.newContext();
  await createTwoSessions(page, other);
  await page.goto("/user/security");
  await page.getByRole("button", { name: "Revoke all other sessions" }).click();
  await expect(page).toHaveURL(/\/user\/security$/);
  expect((await getGeneratedAuthSession(other.request)).authenticated).toBe(
    false,
  );
  await other.close();
});
```

- [ ] **Step 2: Run RED**

Run: `cd apps/web && npm run e2e -- --grep "account|external provider"`

Expected: new routes/provider controls fail.

- [ ] **Step 3: Extend deterministic E2E setup**

Pass fake-provider settings explicitly for normal E2E; expose generated SDK helpers for account calls; keep real-provider smoke disabled unless all required environment variables for that provider are present. Do not copy secrets in code or print provider URLs containing state.

- [ ] **Step 4: Run GREEN**

Run:

```bash
cd apps/web
npm run e2e
```

Expected: deterministic suite PASS. With local ignored credentials loaded into the process, each opted-in provider smoke opens its official authorization/login screen and exits without callback.

- [ ] **Step 5: Commit**

```bash
git add apps/api/tests/Template.E2EHost apps/web/e2e apps/web/playwright.config.ts
git commit -m "test: cover account and oauth browser flows"
```

### Task 15: Durable Documentation, Complete Verification, and Acceptance Evidence

**Files:**

- Modify: `docs/api-conventions.md`
- Modify: `docs/web-conventions.md`
- Modify: `docs/authentication-persistence-operations.md`
- Modify: `docs/aspnetcore-migration-plan.md`

**Interfaces:**

- Records the implemented contract, not planned behavior.

- [ ] **Step 1: Update durable docs**

Document REST/callback routes, auth/CSRF, provider email mappings, secondary-email lifecycle, session cursor, transactions, Data Protection certificate/local overlay, token non-storage, disconnect limitation, rollback, and production gate. Mark iteration 4 scope/state precisely; leave future iterations unchanged.

- [ ] **Step 2: Run required .NET verification**

```bash
dotnet restore Template.sln
dotnet build Template.sln --no-restore
dotnet test Template.sln --no-restore
```

Expected: restore succeeds; build has 0 errors; all tests PASS.

- [ ] **Step 3: Verify migration and OpenAPI determinism**

```bash
dotnet ef migrations has-pending-model-changes \
  --project apps/api/src/Template.Infrastructure \
  --startup-project apps/api/src/Template.Api \
  --context AuthDbContext
dotnet build apps/api/src/Template.Api/Template.Api.csproj \
  --no-restore -p:OpenApiGenerateDocuments=true
git diff --exit-code -- contracts/openapi/v1.json
cd apps/web && npm run api:check
```

Expected: no pending model changes; contract and generated SDK unchanged.

- [ ] **Step 4: Run complete web verification**

```bash
cd apps/web
npm ci
npm audit --omit=dev
npm run boundaries:check
npm run format:check
npm run lint
npm run typecheck
npm test -- --runInBand
node -e "require('node:fs').rmSync('.next', { recursive: true, force: true })"
env -u API_INTERNAL_BASE_URL -u API_PROXY_TARGET PUBLIC_DEFAULT_LOCALE=en npm run build
test -f .next/standalone/server.js
npm run e2e
```

Expected: all commands PASS and standalone artifact exists.

- [ ] **Step 5: Verify immutable reference and branch hygiene**

```bash
cd ../..
git diff --check
git diff --exit-code -- template/
git diff --exit-code origin/main...HEAD -- template/
git status --short
```

Expected: no whitespace/reference diff; only intended final documentation changes remain before commit.

- [ ] **Step 6: Record exact acceptance evidence**

Insert actual test counts, command outcomes, provider smoke result/skip reasons, and known differences in `docs/aspnetcore-migration-plan.md`. Do not claim live callback completion unless it was actually performed.

- [ ] **Step 7: Commit**

```bash
git add docs/api-conventions.md docs/web-conventions.md docs/authentication-persistence-operations.md docs/aspnetcore-migration-plan.md
git commit -m "docs: complete accounts oauth iteration"
```

- [ ] **Step 8: Perform final verification-before-completion**

Invoke `superpowers:verification-before-completion`, rerun any command it identifies as stale, confirm clean status and `template/` invariance, then report implemented scenarios, results, known differences, out-of-scope work, and the next-iteration gate.
