# Persistence, Identity, and Basic Authentication Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver migration iteration 3 as a working PostgreSQL/Identity/browser-session vertical slice with reference-compatible local email/password automation, REST-only Next.js login/logout/session UI, and no production social login until iteration 4.

**Architecture:** ASP.NET Core remains the only `/api/**` owner. Application use cases depend on neutral identity/session ports; Infrastructure implements them with Identity Core, EF Core/Npgsql, PostgreSQL-backed `ITicketStore`, and explicit transactions; Api owns validation, authorization, cookies, CSRF, rate limits, Problem Details, and OpenAPI. Next.js consumes only the generated SDK, forwards only the same-origin cookie and correlation ID during SSR, and never reads the session cookie in browser JavaScript.

**Tech Stack:** .NET SDK `10.0.302`, ASP.NET Core runtime/packages `10.0.10`, EF Core/design tool `10.0.10`, Npgsql EF provider `10.0.3`, Testcontainers for .NET `4.13.0`, PostgreSQL image `postgres:18.4`, xUnit v3 `3.2.2`, Next.js `16.2.11`, React `19.2.8`, TypeScript `6.0.3`, `@hey-api/openapi-ts` `0.99.0`, Jest `30.4.2`, Playwright `1.61.1`.

## Global Constraints

- Treat `template/` as immutable reference input: read and compare it, but never edit, format, move, delete, or run migrations inside it.
- Preserve dependency direction `Domain → Application → Infrastructure → Api`; Domain has no EF/Identity/HTTP dependencies and Application depends only on Domain.
- Keep all browser authentication in secure HttpOnly same-origin cookies; never put bearer tokens or session tokens in browser storage.
- Support reference-compatible email/password only when both the environment is `Development` or `Test` and `LocalAutomationAuth:Enabled=true`; Production always reports `local_auth_disabled`.
- Do not expose production password registration/login. Social providers, account lifecycle, API keys, `x-api-key`, real Bearer issuance/validation, organizations, Aspire, YARP, Redis, and production Data Protection key storage remain outside iteration 3.
- Register the primary `Template.Session` cookie handler and its non-default, write-only `Template.Session.Issuer` companion only for secure browser-session key rotation; `Api.BrowserSession` accepts only the primary handler. Keep future machine/Bearer extension points conceptual; do not register fake schemes or advertise them in OpenAPI.
- Use `ConnectionStrings:Postgres`; do not commit database passwords. Runtime code never auto-applies migrations.
- Run EF CLI design-time commands with `Template.Infrastructure` as both `--project` and `--startup-project`: it owns the design-time factory and the private EF Design package. `Template.Api` remains the only HTTP host.
- Pin all persistence acceptance to PostgreSQL `18.4`; do not use EF InMemory or SQLite.
- Use `{ "data": ... }` for success and RFC Problem Details with stable `code`/`traceId` for failures; all auth responses set `Cache-Control: no-store`.
- Reject unknown JSON members. Name is trimmed and `2–50`, email is trimmed/lowercased and at most `254`, explicit password is `12–128`, generated password contains at least `256` random bits.
- Cookie: `__Host-template.session`, HttpOnly, Secure Always, SameSite Lax, Path `/`, no Domain, persistent seven-day sliding expiration.
- Antiforgery cookie: `__Host-template.antiforgery`, HttpOnly, Secure Always, SameSite Strict, Path `/`, no Domain; unsafe operations require `X-CSRF-TOKEN`.
- Rate limits have queue length `0`: scenario create `20/minute/IP`, credential sign-in `10/5 minutes/IP`.
- Keep local operations in committed OpenAPI with tag `local-only` and `x-local-only: true`; keep API-key and Bearer schemes absent.
- Do not add pagination or filtering: iteration 3 exposes no collection endpoint.
- Keep Next.js free of Prisma, Better Auth, Server Actions, Route Handlers, direct database access, raw application `fetch`, handwritten transport DTOs, and browser API-origin environment variables.
- For every behavior, add the focused failing test first, observe the intended failure, implement the smallest behavior, rerun focused tests, then commit.
- Do not create an active OpenSpec change/spec.
- Before completion run the full command matrix in Task 15 and prove both `git diff --exit-code -- template/` and `git diff --exit-code origin/main...HEAD -- template/`.

---

## Security amendment: browser-session lookup-key rotation

Task 6 implementation evidence exposed an ASP.NET Core `10.0.10` behavior that
the original single-handler recipe could not safely support: after a same-request
cookie sign-out, `CookieAuthenticationHandler` retains its private session-store
key and a following sign-in uses `ITicketStore.RenewAsync` rather than allocating
a new opaque key. Reusing that key would let a pre-replacement cookie authenticate
the new session, including across an account switch.

The locked correction is deliberately based only on public ASP.NET Core APIs:

- `Template.Session` remains the only default authenticate/challenge/forbid/sign-out
  scheme. Its cookie manager reads request cookies normally.
- `Template.Session.Issuer` is a non-default standard `AddCookie` scheme used only
  by `BrowserSessionGateway` to issue a replacement. Its cookie manager never
  returns a request cookie, forcing `ITicketStore.StoreAsync` to generate a fresh
  256-bit lookup key.
- Both schemes share the same `__Host-template.session` cookie policy,
  `PostgresTicketStore`, and an explicit application-owned `TicketDataFormat` Data
  Protection purpose. The primary scheme can therefore authenticate a cookie that
  the issuer writes.
- A replacement starts once per `HttpContext`, signs out the primary scheme first
  (revoking the old database row and suppressing a pending sliding refresh),
  suppresses only that intentional delete-cookie header, then signs in via the
  issuer scheme. A second gateway sign-in in the same request fails closed.
- `RenewAsync` never recreates a deleted row. Retrieval validates the protected
  ticket's known scheme plus exactly one matching persisted user/session claim;
  expired, malformed, incompatible, or mismatched tickets are lazily and
  conditionally deleted.

Do not collapse the two schemes into a single-handler sign-out/sign-in sequence,
manually serialize the outer cookie ticket, use reflection, or reintroduce a
fallback that stores a new session under a removed key. Task 7 extends this
already-established composition with seven-day/sliding options and the browser
policy; it must not replace it. Regression coverage proves same-user and
cross-user replacement revoke the old cookie, ordinary logout still revokes, a
half-life sliding refresh cannot append a stale cookie, and coordinated
revoke/renew paths remain idempotent.

---

## Locked File Structure

### Domain and Application

- Create `apps/api/src/Template.Domain/Authentication/UserId.cs` — framework-neutral UUIDv7 user identifier.
- Create `apps/api/src/Template.Domain/Authentication/SessionId.cs` — framework-neutral UUIDv7 session identifier.
- Create `apps/api/src/Template.Domain/Authentication/SessionWindow.cs` — UTC creation/update/expiry invariant.
- Create `apps/api/src/Template.Application/Authentication/AuthModels.cs` — safe user/session projections and operation results.
- Create `apps/api/src/Template.Application/Authentication/LocalAutomationCredentialPolicy.cs` — namespace and normalization rules.
- Create `apps/api/src/Template.Application/Authentication/LocalAutomationAuthService.cs` — scenario, credential sign-in, and cleanup orchestration.
- Create `apps/api/src/Template.Application/Authentication/BrowserAuthenticationService.cs` — current-session and logout use cases.
- Create `apps/api/src/Template.Application/Authentication/Ports/*.cs` — identity, browser-session, credential-generator, and transaction ports.
- Create `apps/api/tests/Template.Application.Tests/` — pure Domain/Application tests and deterministic fakes.

### Infrastructure and PostgreSQL

- Modify `Directory.Packages.props`, `Template.sln`, and create `.config/dotnet-tools.json` for exact EF/Testcontainers dependencies and `dotnet-ef`.
- Modify `apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj` for Identity EF/Npgsql and ASP.NET shared-framework access.
- Create `apps/api/src/Template.Infrastructure/Identity/ApplicationUser.cs` and `Identity/IdentityGateway.cs`.
- Create `apps/api/src/Template.Infrastructure/Persistence/AuthDbContext.cs`, `AuthDbContextFactory.cs`, `AuthSessionEntity.cs`, `EfAuthenticationUnitOfWork.cs`, and `InfrastructureServiceCollectionExtensions.cs`.
- Generate `apps/api/src/Template.Infrastructure/Persistence/Migrations/*_InitialAuthPersistence.cs`, its designer, and `AuthDbContextModelSnapshot.cs`.
- Create `apps/api/src/Template.Infrastructure/Authentication/CryptographicLocalAutomationCredentialGenerator.cs`, `BrowserSessionAuthenticationDefaults.cs`, `BrowserSessionCookieManagers.cs`, `BrowserSessionGateway.cs`, and `PostgresTicketStore.cs`.
- Create `apps/api/src/Template.Infrastructure/Health/AuthDatabaseHealthCheck.cs`.
- Create `apps/api/src/Template.Infrastructure/Properties/AssemblyInfo.cs` for API-test internals visibility only.

### API boundary

- Modify `apps/api/src/Template.Api/Authentication/ApiAuthenticationDefaults.cs`, `ApiPolicies.cs`, and `AuthenticationServiceCollectionExtensions.cs`.
- Create `apps/api/src/Template.Api/Authentication/AntiforgeryEndpointFilter.cs`, `AuthEndpointMetadata.cs`, `AuthSecurityServiceCollectionExtensions.cs`, `LocalAutomationAuthAvailability.cs`, `LocalAutomationAuthOptions.cs`, and `LocalAutomationAvailabilityMiddleware.cs`.
- Modify `apps/api/src/Template.Api/Errors/*` and create `ApiProblemException.cs` plus `ApiValidationException.cs`.
- Create `apps/api/src/Template.Api/Features/Auth/AuthContracts.cs`, `AuthEndpointModule.cs`, `AuthSecurityEvents.cs`, and `ApiJsonRequestReader.cs`.
- Modify endpoint registration, OpenAPI transformers, `Program.cs`, `appsettings.json`, and `appsettings.Development.json`.
- Rewrite `apps/api/tests/Template.Api.Tests/Infrastructure/ApiWebApplicationFactory.cs`; create PostgreSQL/container/API auth helpers and auth/persistence/contract tests; remove the header-only test authentication handler.

### Contract and web

- Regenerate `contracts/openapi/v1.json` and the complete `apps/web/src/lib/api/generated/` tree.
- Modify `apps/web/src/lib/api/result.ts`, the server request-header allowlist, source-boundary checker, and generated-contract tests.
- Create `apps/web/src/features/authentication/` for route constants and redirect sanitization.
- Create `apps/web/src/lib/api/auth/` for generated-SDK session/capability/CSRF/mutation adapters.
- Move the current home page beneath `apps/web/src/app/(site)/`; make the root layout providers-only; add site and simple route-group layouts.
- Create `/auth/login` and `/dashboard` pages/components, `auth.en.json`, and `auth.ru.json`.
- Create `apps/api/tests/Template.E2EHost/` as the test-only API+Testcontainers network host.
- Create `apps/web/e2e/support/generated-auth-api.ts` and replace/extend Playwright coverage with the complete multi-session scenario.

### Durable documentation

- Modify `docs/api-conventions.md`, `docs/web-conventions.md`, and `docs/aspnetcore-migration-plan.md`.
- Create `docs/authentication-persistence-operations.md`.
- Keep the approved design at `docs/superpowers/specs/2026-07-24-persistence-identity-authentication-design.md` updated when implementation evidence exposes a real contradiction, including the locked Task 6 browser-session rotation correction above.

## Task 1: Add framework-neutral identifiers and session invariants

**Files:**

- Create: `apps/api/tests/Template.Application.Tests/Template.Application.Tests.csproj`
- Create: `apps/api/tests/Template.Application.Tests/SessionWindowTests.cs`
- Create: `apps/api/src/Template.Domain/Authentication/UserId.cs`
- Create: `apps/api/src/Template.Domain/Authentication/SessionId.cs`
- Create: `apps/api/src/Template.Domain/Authentication/SessionWindow.cs`
- Modify: `Template.sln`

**Interfaces:**

- Consumes: BCL `Guid.CreateVersion7()` and `DateTimeOffset`.
- Produces: `UserId.New()`, `SessionId.New()`, and `SessionWindow.Start(DateTimeOffset, TimeSpan)` for all later Application/Infrastructure code.

- [ ] **Step 1: Scaffold the test project and write the failing invariant tests**

```xml
<!-- apps/api/tests/Template.Application.Tests/Template.Application.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Template.Application\Template.Application.csproj" />
    <ProjectReference Include="..\..\src\Template.Domain\Template.Domain.csproj" />
  </ItemGroup>
</Project>
```

```csharp
// apps/api/tests/Template.Application.Tests/SessionWindowTests.cs
using Template.Domain.Authentication;

namespace Template.Application.Tests;

public sealed class SessionWindowTests
{
    [Fact]
    public void StartNormalizesUtcAndCreatesExpectedExpiry()
    {
        var local = new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.FromHours(3));

        var window = SessionWindow.Start(local, TimeSpan.FromDays(7));

        Assert.Equal(TimeSpan.Zero, window.CreatedAt.Offset);
        Assert.Equal(window.CreatedAt, window.UpdatedAt);
        Assert.Equal(window.CreatedAt.AddDays(7), window.ExpiresAt);
    }

    [Fact]
    public void StartRejectsNonPositiveLifetime()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SessionWindow.Start(DateTimeOffset.UtcNow, TimeSpan.Zero));
    }

    [Fact]
    public void NewIdentifiersAreVersionSevenAndDistinct()
    {
        var user = UserId.New();
        var session = SessionId.New();

        Assert.Equal(7, user.Value.Version);
        Assert.Equal(7, session.Value.Version);
        Assert.NotEqual(user.Value, session.Value);
    }
}
```

Add the project without manually editing solution GUIDs:

```bash
dotnet sln Template.sln add \
  apps/api/tests/Template.Application.Tests/Template.Application.Tests.csproj \
  --solution-folder tests
```

- [ ] **Step 2: Run the focused test and verify the intended red state**

Run:

```bash
dotnet test apps/api/tests/Template.Application.Tests/Template.Application.Tests.csproj \
  --filter FullyQualifiedName~SessionWindowTests
```

Expected: build fails because `Template.Domain.Authentication` and the three types do not exist.

- [ ] **Step 3: Implement the minimal Domain types**

```csharp
// apps/api/src/Template.Domain/Authentication/UserId.cs
namespace Template.Domain.Authentication;

public readonly record struct UserId(Guid Value)
{
    public static UserId New() => new(Guid.CreateVersion7());
}
```

```csharp
// apps/api/src/Template.Domain/Authentication/SessionId.cs
namespace Template.Domain.Authentication;

public readonly record struct SessionId(Guid Value)
{
    public static SessionId New() => new(Guid.CreateVersion7());
}
```

```csharp
// apps/api/src/Template.Domain/Authentication/SessionWindow.cs
namespace Template.Domain.Authentication;

public sealed record SessionWindow(
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset ExpiresAt)
{
    public static SessionWindow Start(DateTimeOffset now, TimeSpan lifetime)
    {
        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lifetime),
                "Session lifetime must be positive.");
        }

        var utcNow = now.ToUniversalTime();
        return new SessionWindow(utcNow, utcNow, utcNow.Add(lifetime));
    }

    public bool IsExpired(DateTimeOffset now) =>
        ExpiresAt <= now.ToUniversalTime();
}
```

- [ ] **Step 4: Run the focused tests**

Run:

```bash
dotnet test apps/api/tests/Template.Application.Tests/Template.Application.Tests.csproj \
  --filter FullyQualifiedName~SessionWindowTests
```

Expected: `3` tests pass.

- [ ] **Step 5: Commit**

```bash
git add Template.sln \
  apps/api/src/Template.Domain/Authentication \
  apps/api/tests/Template.Application.Tests
git commit -m "Add authentication domain invariants"
```

## Task 2: Lock local credential namespace and normalization rules

**Files:**

- Create: `apps/api/tests/Template.Application.Tests/LocalAutomationCredentialPolicyTests.cs`
- Create: `apps/api/src/Template.Application/Authentication/LocalAutomationCredentialPolicy.cs`

**Interfaces:**

- Consumes: no Infrastructure or HTTP types.
- Produces: `LocalAutomationCredentialPolicy.NormalizeName`, `NormalizeEmail`, and `IsLocalEmail` used by `LocalAutomationAuthService` and Identity gateway tests.

- [ ] **Step 1: Write the failing policy tests**

```csharp
// apps/api/tests/Template.Application.Tests/LocalAutomationCredentialPolicyTests.cs
using Template.Application.Authentication;

namespace Template.Application.Tests;

public sealed class LocalAutomationCredentialPolicyTests
{
    [Theory]
    [InlineData(" LOCAL-AGENT+Case@LOCAL-AGENT.TEST ", "local-agent+case@local-agent.test")]
    [InlineData("local-agent+abc@local-agent.test", "local-agent+abc@local-agent.test")]
    public void NormalizeEmailTrimsAndLowercases(string input, string expected)
    {
        Assert.Equal(expected, LocalAutomationCredentialPolicy.NormalizeEmail(input));
    }

    [Theory]
    [InlineData("local-agent+abc@local-agent.test", true)]
    [InlineData("local-agent+@local-agent.test", false)]
    [InlineData("local-agent+a@b@local-agent.test", false)]
    [InlineData("person@example.com", false)]
    [InlineData("local-agent+abc@example.com", false)]
    public void IsLocalEmailRequiresTheReservedNamespace(string input, bool expected)
    {
        Assert.Equal(expected, LocalAutomationCredentialPolicy.IsLocalEmail(input));
    }

    [Fact]
    public void NormalizeNameTrimsVisibleName()
    {
        Assert.Equal(
            "Local Automation User",
            LocalAutomationCredentialPolicy.NormalizeName("  Local Automation User  "));
    }
}
```

- [ ] **Step 2: Run the focused test and verify it fails**

Run:

```bash
dotnet test apps/api/tests/Template.Application.Tests/Template.Application.Tests.csproj \
  --filter FullyQualifiedName~LocalAutomationCredentialPolicyTests
```

Expected: build fails because `LocalAutomationCredentialPolicy` does not exist.

- [ ] **Step 3: Implement the policy**

```csharp
// apps/api/src/Template.Application/Authentication/LocalAutomationCredentialPolicy.cs
namespace Template.Application.Authentication;

public static class LocalAutomationCredentialPolicy
{
    public const string EmailPrefix = "local-agent+";
    public const string EmailDomain = "local-agent.test";
    public const string CleanupPath = "/api/local-auth/scenario";
    public const int GeneratedCollisionAttempts = 3;

    public static string NormalizeName(string value) => value.Trim();

    public static string NormalizeEmail(string value) =>
        value.Trim().ToLowerInvariant();

    public static bool IsLocalEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = NormalizeEmail(value);
        var suffix = $"@{EmailDomain}";
        if (!normalized.StartsWith(EmailPrefix, StringComparison.Ordinal) ||
            !normalized.EndsWith(suffix, StringComparison.Ordinal))
        {
            return false;
        }

        var discriminator = normalized[
            EmailPrefix.Length..^suffix.Length];
        return discriminator.Length > 0 &&
               !discriminator.Contains('@');
    }
}
```

- [ ] **Step 4: Run the focused tests**

Run:

```bash
dotnet test apps/api/tests/Template.Application.Tests/Template.Application.Tests.csproj \
  --filter FullyQualifiedName~LocalAutomationCredentialPolicyTests
```

Expected: `8` theory/fact cases pass.

- [ ] **Step 5: Commit**

```bash
git add \
  apps/api/src/Template.Application/Authentication/LocalAutomationCredentialPolicy.cs \
  apps/api/tests/Template.Application.Tests/LocalAutomationCredentialPolicyTests.cs
git commit -m "Add local automation credential policy"
```

## Task 3: Implement Application auth use cases over ports

**Files:**

- Create: `apps/api/src/Template.Application/Authentication/AuthModels.cs`
- Create: `apps/api/src/Template.Application/Authentication/Ports/IAuthenticationUnitOfWork.cs`
- Create: `apps/api/src/Template.Application/Authentication/Ports/IBrowserSessionGateway.cs`
- Create: `apps/api/src/Template.Application/Authentication/Ports/ILocalAutomationCredentialGenerator.cs`
- Create: `apps/api/src/Template.Application/Authentication/Ports/ILocalIdentityGateway.cs`
- Create: `apps/api/src/Template.Application/Authentication/LocalAutomationAuthService.cs`
- Create: `apps/api/src/Template.Application/Authentication/BrowserAuthenticationService.cs`
- Create: `apps/api/tests/Template.Application.Tests/LocalAutomationAuthServiceTests.cs`
- Create: `apps/api/tests/Template.Application.Tests/BrowserAuthenticationServiceTests.cs`

**Interfaces:**

- Consumes: `UserId`, `SessionId`, `SessionWindow`, and `LocalAutomationCredentialPolicy`.
- Produces: the exact ports and use-case methods consumed by Infrastructure and Api:
  - `LocalAutomationAuthService.CreateScenarioAsync(CreateLocalScenarioInput, CancellationToken)`
  - `LocalAutomationAuthService.SignInAsync(LocalCredentialInput, CancellationToken)`
  - `LocalAutomationAuthService.CleanupAsync(CancellationToken)`
  - `BrowserAuthenticationService.GetSessionAsync(CancellationToken)`
  - `BrowserAuthenticationService.LogoutAsync(CancellationToken)`.

- [ ] **Step 1: Write failing orchestration tests with deterministic fakes**

The tests must cover generated-email retry, explicit duplicate without retry,
generic credential failure, local-user cleanup authorization, and transaction
boundaries. Use the following test shape and keep the nested fakes in the same
file so no production fake leaks into runtime:

```csharp
// apps/api/tests/Template.Application.Tests/LocalAutomationAuthServiceTests.cs
using Template.Application.Authentication;
using Template.Application.Authentication.Ports;
using Template.Domain.Authentication;

namespace Template.Application.Tests;

public sealed class LocalAutomationAuthServiceTests
{
    [Fact]
    public async Task GeneratedDuplicateRetriesAndReturnsSecondCredentials()
    {
        var identities = new FakeIdentityGateway { DuplicateCreatesRemaining = 1 };
        var sessions = new FakeBrowserSessionGateway();
        var generator = new QueueCredentialGenerator(
            new("First User", "local-agent+first@local-agent.test", "local-first-password"),
            new("Second User", "local-agent+second@local-agent.test", "local-second-password"));
        var transactions = new CountingUnitOfWork();
        var service = new LocalAutomationAuthService(
            identities,
            sessions,
            generator,
            transactions);

        var result = await service.CreateScenarioAsync(
            new CreateLocalScenarioInput(null, null, null),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal("local-agent+second@local-agent.test", result.Value!.Credentials.Email);
        Assert.Equal(2, transactions.Executions);
        Assert.Equal(2, identities.CreateAttempts);
        Assert.Equal(LocalAutomationCredentialPolicy.CleanupPath, result.Value.CleanupUrl);
    }

    [Fact]
    public async Task ExplicitDuplicateReturnsConflictWithoutRetry()
    {
        var identities = new FakeIdentityGateway { DuplicateCreatesRemaining = 1 };
        var transactions = new CountingUnitOfWork();
        var service = new LocalAutomationAuthService(
            identities,
            new FakeBrowserSessionGateway(),
            new QueueCredentialGenerator(
                new("Generated", "local-agent+generated@local-agent.test", "local-generated-password")),
            transactions);

        var result = await service.CreateScenarioAsync(
            new CreateLocalScenarioInput(
                "Explicit",
                "local-agent+explicit@local-agent.test",
                "local-explicit-password"),
            TestContext.Current.CancellationToken);

        Assert.Equal(AuthFailure.UserExists, result.Failure);
        Assert.Equal(1, transactions.Executions);
        Assert.Equal(1, identities.CreateAttempts);
    }

    [Fact]
    public async Task SignInOutsideNamespaceIsGenericInvalidCredentials()
    {
        var identities = new FakeIdentityGateway();
        var service = new LocalAutomationAuthService(
            identities,
            new FakeBrowserSessionGateway(),
            new QueueCredentialGenerator(
                new("Generated", "local-agent+generated@local-agent.test", "local-generated-password")),
            new CountingUnitOfWork());

        var result = await service.SignInAsync(
            new LocalCredentialInput("person@example.com", "not-used-password"),
            TestContext.Current.CancellationToken);

        Assert.Equal(AuthFailure.InvalidCredentials, result.Failure);
        Assert.Equal(0, identities.PasswordChecks);
    }

    [Fact]
    public async Task CleanupRejectsAuthenticatedNonLocalUser()
    {
        var sessions = new FakeBrowserSessionGateway
        {
            Current = TestIdentity.Session(isLocalAutomation: false)
        };
        var identities = new FakeIdentityGateway();
        var service = new LocalAutomationAuthService(
            identities,
            sessions,
            new QueueCredentialGenerator(
                new("Generated", "local-agent+generated@local-agent.test", "local-generated-password")),
            new CountingUnitOfWork());

        var result = await service.CleanupAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AuthFailure.LocalUserRequired, result.Failure);
        Assert.Equal(0, identities.DeleteAttempts);
    }

    private sealed class QueueCredentialGenerator(
        params LocalAutomationCredentials[] credentials)
        : ILocalAutomationCredentialGenerator
    {
        private readonly Queue<LocalAutomationCredentials> _credentials = new(credentials);

        public LocalAutomationCredentials Generate() => _credentials.Dequeue();
    }

    private sealed class CountingUnitOfWork : IAuthenticationUnitOfWork
    {
        public int Executions { get; private set; }

        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken)
        {
            Executions++;
            return await action(cancellationToken);
        }
    }

    private sealed class FakeIdentityGateway : ILocalIdentityGateway
    {
        public int DuplicateCreatesRemaining { get; init; }
        public int CreateAttempts { get; private set; }
        public int PasswordChecks { get; private set; }
        public int DeleteAttempts { get; private set; }

        public Task<AuthUser> CreateLocalAsync(
            LocalAutomationCredentials credentials,
            CancellationToken cancellationToken)
        {
            CreateAttempts++;
            if (CreateAttempts <= DuplicateCreatesRemaining)
            {
                throw new DuplicateLocalIdentityException();
            }

            return Task.FromResult(TestIdentity.User(
                email: credentials.Email,
                name: credentials.Name,
                isLocalAutomation: true));
        }

        public Task<AuthUser?> CheckLocalPasswordAsync(
            string email,
            string password,
            CancellationToken cancellationToken)
        {
            PasswordChecks++;
            return Task.FromResult<AuthUser?>(
                email == "local-agent+valid@local-agent.test"
                    ? TestIdentity.User(email: email, isLocalAutomation: true)
                    : null);
        }

        public Task DeleteAsync(UserId userId, CancellationToken cancellationToken)
        {
            DeleteAttempts++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeBrowserSessionGateway : IBrowserSessionGateway
    {
        public AuthenticatedSession? Current { get; init; }
        public int SignOutCalls { get; private set; }

        public Task<AuthenticatedSession?> GetCurrentAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Current);

        public Task<BrowserSession> SignInAsync(
            AuthUser user,
            CancellationToken cancellationToken) =>
            Task.FromResult(TestIdentity.Session(user).Session);

        public Task SignOutAsync(CancellationToken cancellationToken)
        {
            SignOutCalls++;
            return Task.CompletedTask;
        }
    }

    private static class TestIdentity
    {
        public static AuthUser User(
            string email = "local-agent+user@local-agent.test",
            string name = "Local User",
            bool isLocalAutomation = true) =>
            new(
                UserId.New(),
                name,
                email,
                EmailVerified: false,
                Image: null,
                IsLocalAutomation: isLocalAutomation);

        public static AuthenticatedSession Session(bool isLocalAutomation) =>
            Session(User(isLocalAutomation: isLocalAutomation));

        public static AuthenticatedSession Session(AuthUser user)
        {
            var window = SessionWindow.Start(
                new DateTimeOffset(2026, 7, 24, 0, 0, 0, TimeSpan.Zero),
                TimeSpan.FromDays(7));
            return new AuthenticatedSession(
                user,
                new BrowserSession(
                    SessionId.New(),
                    window.CreatedAt,
                    window.UpdatedAt,
                    window.ExpiresAt));
        }
    }
}
```

```csharp
// apps/api/tests/Template.Application.Tests/BrowserAuthenticationServiceTests.cs
using Template.Application.Authentication;
using Template.Application.Authentication.Ports;

namespace Template.Application.Tests;

public sealed class BrowserAuthenticationServiceTests
{
    [Fact]
    public async Task AnonymousSessionUsesExplicitAnonymousProjection()
    {
        var service = new BrowserAuthenticationService(
            new AnonymousSessionGateway(),
            new InlineUnitOfWork());

        var state = await service.GetSessionAsync(TestContext.Current.CancellationToken);

        Assert.False(state.Authenticated);
        Assert.Null(state.User);
        Assert.Null(state.Session);
    }

    [Fact]
    public async Task AnonymousLogoutReturnsSessionRequired()
    {
        var service = new BrowserAuthenticationService(
            new AnonymousSessionGateway(),
            new InlineUnitOfWork());

        var result = await service.LogoutAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AuthFailure.SessionRequired, result.Failure);
    }

    private sealed class AnonymousSessionGateway : IBrowserSessionGateway
    {
        public Task<AuthenticatedSession?> GetCurrentAsync(CancellationToken cancellationToken) =>
            Task.FromResult<AuthenticatedSession?>(null);

        public Task<BrowserSession> SignInAsync(
            AuthUser user,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Sign-in is not part of this test.");

        public Task SignOutAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class InlineUnitOfWork : IAuthenticationUnitOfWork
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) =>
            action(cancellationToken);
    }
}
```

- [ ] **Step 2: Run the two focused classes and verify they fail**

Run:

```bash
dotnet test apps/api/tests/Template.Application.Tests/Template.Application.Tests.csproj \
  --filter "FullyQualifiedName~LocalAutomationAuthServiceTests|FullyQualifiedName~BrowserAuthenticationServiceTests"
```

Expected: build fails because the models, ports, and services do not exist.

- [ ] **Step 3: Add the exact Application models and ports**

```csharp
// apps/api/src/Template.Application/Authentication/AuthModels.cs
using Template.Domain.Authentication;

namespace Template.Application.Authentication;

public sealed record AuthUser(
    UserId Id,
    string Name,
    string Email,
    bool EmailVerified,
    string? Image,
    bool IsLocalAutomation);

public sealed record BrowserSession(
    SessionId Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset ExpiresAt);

public sealed record AuthenticatedSession(AuthUser User, BrowserSession Session);

public sealed record SessionState(
    bool Authenticated,
    AuthUser? User,
    BrowserSession? Session)
{
    public static SessionState Anonymous { get; } = new(false, null, null);

    public static SessionState From(AuthenticatedSession value) =>
        new(true, value.User, value.Session);
}

public sealed record LocalAutomationCredentials(
    string Name,
    string Email,
    string Password);

public sealed record CreateLocalScenarioInput(
    string? Name,
    string? Email,
    string? Password);

public sealed record LocalCredentialInput(string Email, string Password);

public sealed record LocalAutomationScenario(
    AuthUser User,
    BrowserSession Session,
    LocalAutomationCredentials Credentials,
    string CleanupUrl);

public sealed record LocalAutomationCleanup(int DeletedOrganizations);

public enum AuthFailure
{
    InvalidLocalEmail,
    UserExists,
    InvalidCredentials,
    SessionRequired,
    LocalUserRequired
}

public sealed record AuthOperationResult<T>(T? Value, AuthFailure? Failure)
    where T : class
{
    public bool Succeeded => Failure is null;

    public static AuthOperationResult<T> Success(T value) => new(value, null);

    public static AuthOperationResult<T> Failed(AuthFailure failure) =>
        new(null, failure);
}

public sealed class DuplicateLocalIdentityException : Exception;
```

```csharp
// apps/api/src/Template.Application/Authentication/Ports/IAuthenticationUnitOfWork.cs
namespace Template.Application.Authentication.Ports;

public interface IAuthenticationUnitOfWork
{
    Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken);
}
```

```csharp
// apps/api/src/Template.Application/Authentication/Ports/IBrowserSessionGateway.cs
namespace Template.Application.Authentication.Ports;

public interface IBrowserSessionGateway
{
    Task<AuthenticatedSession?> GetCurrentAsync(CancellationToken cancellationToken);
    Task<BrowserSession> SignInAsync(AuthUser user, CancellationToken cancellationToken);
    Task SignOutAsync(CancellationToken cancellationToken);
}
```

```csharp
// apps/api/src/Template.Application/Authentication/Ports/ILocalAutomationCredentialGenerator.cs
namespace Template.Application.Authentication.Ports;

public interface ILocalAutomationCredentialGenerator
{
    LocalAutomationCredentials Generate();
}
```

```csharp
// apps/api/src/Template.Application/Authentication/Ports/ILocalIdentityGateway.cs
using Template.Domain.Authentication;

namespace Template.Application.Authentication.Ports;

public interface ILocalIdentityGateway
{
    Task<AuthUser> CreateLocalAsync(
        LocalAutomationCredentials credentials,
        CancellationToken cancellationToken);

    Task<AuthUser?> CheckLocalPasswordAsync(
        string email,
        string password,
        CancellationToken cancellationToken);

    Task DeleteAsync(UserId userId, CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Implement scenario/sign-in/cleanup orchestration**

```csharp
// apps/api/src/Template.Application/Authentication/LocalAutomationAuthService.cs
using Template.Application.Authentication.Ports;

namespace Template.Application.Authentication;

public sealed class LocalAutomationAuthService(
    ILocalIdentityGateway identities,
    IBrowserSessionGateway sessions,
    ILocalAutomationCredentialGenerator credentialGenerator,
    IAuthenticationUnitOfWork transactions)
{
    public async Task<AuthOperationResult<LocalAutomationScenario>> CreateScenarioAsync(
        CreateLocalScenarioInput input,
        CancellationToken cancellationToken)
    {
        var explicitEmail = !string.IsNullOrWhiteSpace(input.Email);
        var maxAttempts = explicitEmail
            ? 1
            : LocalAutomationCredentialPolicy.GeneratedCollisionAttempts;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var generated = credentialGenerator.Generate();
            var credentials = new LocalAutomationCredentials(
                LocalAutomationCredentialPolicy.NormalizeName(input.Name ?? generated.Name),
                LocalAutomationCredentialPolicy.NormalizeEmail(input.Email ?? generated.Email),
                input.Password ?? generated.Password);

            if (!LocalAutomationCredentialPolicy.IsLocalEmail(credentials.Email))
            {
                return AuthOperationResult<LocalAutomationScenario>.Failed(
                    AuthFailure.InvalidLocalEmail);
            }

            try
            {
                return await transactions.ExecuteAsync(
                    async transactionCancellationToken =>
                    {
                        var user = await identities.CreateLocalAsync(
                            credentials,
                            transactionCancellationToken);
                        var session = await sessions.SignInAsync(
                            user,
                            transactionCancellationToken);
                        return AuthOperationResult<LocalAutomationScenario>.Success(
                            new LocalAutomationScenario(
                                user,
                                session,
                                credentials,
                                LocalAutomationCredentialPolicy.CleanupPath));
                    },
                    cancellationToken);
            }
            catch (DuplicateLocalIdentityException)
            {
                if (explicitEmail || attempt == maxAttempts - 1)
                {
                    return AuthOperationResult<LocalAutomationScenario>.Failed(
                        AuthFailure.UserExists);
                }
            }
        }

        return AuthOperationResult<LocalAutomationScenario>.Failed(AuthFailure.UserExists);
    }

    public async Task<AuthOperationResult<AuthenticatedSession>> SignInAsync(
        LocalCredentialInput input,
        CancellationToken cancellationToken)
    {
        var email = LocalAutomationCredentialPolicy.NormalizeEmail(input.Email);
        if (!LocalAutomationCredentialPolicy.IsLocalEmail(email))
        {
            return AuthOperationResult<AuthenticatedSession>.Failed(
                AuthFailure.InvalidCredentials);
        }

        return await transactions.ExecuteAsync(
            async transactionCancellationToken =>
            {
                var user = await identities.CheckLocalPasswordAsync(
                    email,
                    input.Password,
                    transactionCancellationToken);
                if (user is null || !user.IsLocalAutomation)
                {
                    return AuthOperationResult<AuthenticatedSession>.Failed(
                        AuthFailure.InvalidCredentials);
                }

                var session = await sessions.SignInAsync(
                    user,
                    transactionCancellationToken);
                return AuthOperationResult<AuthenticatedSession>.Success(
                    new AuthenticatedSession(user, session));
            },
            cancellationToken);
    }

    public async Task<AuthOperationResult<LocalAutomationCleanup>> CleanupAsync(
        CancellationToken cancellationToken)
    {
        var current = await sessions.GetCurrentAsync(cancellationToken);
        if (current is null)
        {
            return AuthOperationResult<LocalAutomationCleanup>.Failed(
                AuthFailure.SessionRequired);
        }

        if (!current.User.IsLocalAutomation ||
            !LocalAutomationCredentialPolicy.IsLocalEmail(current.User.Email))
        {
            return AuthOperationResult<LocalAutomationCleanup>.Failed(
                AuthFailure.LocalUserRequired);
        }

        return await transactions.ExecuteAsync(
            async transactionCancellationToken =>
            {
                await identities.DeleteAsync(
                    current.User.Id,
                    transactionCancellationToken);
                await sessions.SignOutAsync(transactionCancellationToken);
                return AuthOperationResult<LocalAutomationCleanup>.Success(
                    new LocalAutomationCleanup(DeletedOrganizations: 0));
            },
            cancellationToken);
    }
}
```

```csharp
// apps/api/src/Template.Application/Authentication/BrowserAuthenticationService.cs
using Template.Application.Authentication.Ports;

namespace Template.Application.Authentication;

public sealed class BrowserAuthenticationService(
    IBrowserSessionGateway sessions,
    IAuthenticationUnitOfWork transactions)
{
    public async Task<SessionState> GetSessionAsync(CancellationToken cancellationToken)
    {
        var current = await sessions.GetCurrentAsync(cancellationToken);
        return current is null ? SessionState.Anonymous : SessionState.From(current);
    }

    public async Task<AuthOperationResult<SessionState>> LogoutAsync(
        CancellationToken cancellationToken)
    {
        var current = await sessions.GetCurrentAsync(cancellationToken);
        if (current is null)
        {
            return AuthOperationResult<SessionState>.Failed(AuthFailure.SessionRequired);
        }

        return await transactions.ExecuteAsync(
            async transactionCancellationToken =>
            {
                await sessions.SignOutAsync(transactionCancellationToken);
                return AuthOperationResult<SessionState>.Success(SessionState.Anonymous);
            },
            cancellationToken);
    }
}
```

- [ ] **Step 5: Run all Application tests**

Run:

```bash
dotnet test apps/api/tests/Template.Application.Tests/Template.Application.Tests.csproj
```

Expected: all Domain/Application tests pass, including the retry and transaction-count assertions.

- [ ] **Step 6: Commit**

```bash
git add \
  apps/api/src/Template.Application/Authentication \
  apps/api/tests/Template.Application.Tests
git commit -m "Add authentication application use cases"
```

## Task 4: Add the PostgreSQL Identity/session model and initial migration

**Files:**

- Modify: `Directory.Packages.props`
- Create: `.config/dotnet-tools.json`
- Modify: `apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj`
- Modify: `apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj`
- Create: `apps/api/src/Template.Infrastructure/Identity/ApplicationUser.cs`
- Create: `apps/api/src/Template.Infrastructure/Persistence/AuthSessionEntity.cs`
- Create: `apps/api/src/Template.Infrastructure/Persistence/AuthDbContext.cs`
- Create: `apps/api/src/Template.Infrastructure/Persistence/AuthDbContextFactory.cs`
- Create: `apps/api/src/Template.Infrastructure/Properties/AssemblyInfo.cs`
- Create: `apps/api/tests/Template.Api.Tests/Infrastructure/PostgreSqlContainerFixture.cs`
- Create: `apps/api/tests/Template.Api.Tests/Infrastructure/TestAssembly.cs`
- Create: `apps/api/tests/Template.Api.Tests/AuthPersistenceTests.cs`
- Generate: `apps/api/src/Template.Infrastructure/Persistence/Migrations/*_InitialAuthPersistence.cs`
- Generate: `apps/api/src/Template.Infrastructure/Persistence/Migrations/*_InitialAuthPersistence.Designer.cs`
- Generate: `apps/api/src/Template.Infrastructure/Persistence/Migrations/AuthDbContextModelSnapshot.cs`

**Interfaces:**

- Consumes: Domain `UserId`/`SessionId` values only at mapping boundaries.
- Produces: `ApplicationUser`, `AuthSessionEntity`, `AuthDbContext`, and `AuthDbContextFactory`; schema `auth` with migration history, users, Identity support tables, and persistent sessions.

- [ ] **Step 1: Pin exact dependencies and the EF tool**

Add these entries to the central package list:

```xml
<PackageVersion Include="Microsoft.AspNetCore.Identity.EntityFrameworkCore" Version="10.0.10" />
<PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.10" />
<PackageVersion Include="Npgsql" Version="10.0.3" />
<PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.3" />
<PackageVersion Include="Testcontainers.PostgreSql" Version="4.13.0" />
```

Create the local tool manifest:

```json
{
  "version": 1,
  "isRoot": true,
  "tools": {
    "dotnet-ef": {
      "version": "10.0.10",
      "commands": ["dotnet-ef"],
      "rollForward": false
    }
  }
}
```

Use these project references:

```xml
<!-- Add inside Template.Infrastructure.csproj -->
<ItemGroup>
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
</ItemGroup>
<ItemGroup>
  <PackageReference Include="Microsoft.AspNetCore.Identity.EntityFrameworkCore" />
  <PackageReference Include="Microsoft.EntityFrameworkCore.Design" PrivateAssets="all" />
  <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
</ItemGroup>
```

```xml
<!-- Add inside Template.Api.Tests.csproj -->
<ItemGroup>
  <PackageReference Include="Npgsql" />
  <PackageReference Include="Testcontainers.PostgreSql" />
</ItemGroup>
<ItemGroup>
  <ProjectReference Include="..\..\src\Template.Infrastructure\Template.Infrastructure.csproj" />
</ItemGroup>
```

Run:

```bash
dotnet tool restore
dotnet restore Template.sln
```

Expected: restore succeeds and resolves the exact versions above.

- [ ] **Step 2: Add the shared PostgreSQL 18.4 fixture**

```csharp
// apps/api/tests/Template.Api.Tests/Infrastructure/TestAssembly.cs
using Template.Api.Tests.Infrastructure;

[assembly: AssemblyFixture(typeof(PostgreSqlContainerFixture))]
```

```csharp
// apps/api/tests/Template.Api.Tests/Infrastructure/PostgreSqlContainerFixture.cs
using Npgsql;
using Testcontainers.PostgreSql;

namespace Template.Api.Tests.Infrastructure;

public sealed class PostgreSqlContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder()
            .WithImage("postgres:18.4")
            .WithDatabase("postgres")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

    public async ValueTask InitializeAsync() =>
        await _container.StartAsync();

    public async Task<(string DatabaseName, string ConnectionString)> CreateDatabaseAsync(
        CancellationToken cancellationToken)
    {
        var databaseName = $"template_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{databaseName}\"";
        await command.ExecuteNonQueryAsync(cancellationToken);

        var builder = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = databaseName,
            IncludeErrorDetail = false
        };
        return (databaseName, builder.ConnectionString);
    }

    public async Task DropDatabaseAsync(
        string databaseName,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        await using (var terminate = connection.CreateCommand())
        {
            terminate.CommandText =
                """
                SELECT pg_terminate_backend(pid)
                FROM pg_stat_activity
                WHERE datname = @databaseName AND pid <> pg_backend_pid()
                """;
            terminate.Parameters.AddWithValue("databaseName", databaseName);
            await terminate.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var drop = connection.CreateCommand();
        drop.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\"";
        await drop.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync() =>
        await _container.DisposeAsync();
}
```

- [ ] **Step 3: Write the failing clean-migration and cascade tests**

```csharp
// apps/api/tests/Template.Api.Tests/AuthPersistenceTests.cs
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Template.Api.Tests.Infrastructure;
using Template.Infrastructure.Identity;
using Template.Infrastructure.Persistence;

namespace Template.Api.Tests;

public sealed class AuthPersistenceTests(PostgreSqlContainerFixture postgres)
    : IAsyncLifetime
{
    private string _databaseName = string.Empty;
    private string _connectionString = string.Empty;

    public async ValueTask InitializeAsync()
    {
        (_databaseName, _connectionString) = await postgres.CreateDatabaseAsync(
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task InitialMigrationCreatesExpectedAuthSchema()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'auth'
            ORDER BY table_name
            """;

        await using var reader = await command.ExecuteReaderAsync(
            TestContext.Current.CancellationToken);
        var tables = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            tables.Add(reader.GetString(0));
        }

        Assert.Contains("users", tables);
        Assert.Contains("sessions", tables);
        Assert.Contains("user_claims", tables);
        Assert.Contains("user_logins", tables);
        Assert.Contains("user_tokens", tables);
        Assert.DoesNotContain("roles", tables);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync(
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeletingUserCascadesPersistentSessions()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        var now = new DateTimeOffset(2026, 7, 24, 0, 0, 0, TimeSpan.Zero);
        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = "local-agent+cascade@local-agent.test",
            NormalizedUserName = "LOCAL-AGENT+CASCADE@LOCAL-AGENT.TEST",
            Email = "local-agent+cascade@local-agent.test",
            NormalizedEmail = "LOCAL-AGENT+CASCADE@LOCAL-AGENT.TEST",
            DisplayName = "Cascade User",
            IsLocalAutomation = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        var session = new AuthSessionEntity
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            TicketKeyHash = new byte[32],
            ProtectedTicket = [1, 2, 3],
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = now.AddDays(7)
        };
        db.Users.Add(user);
        db.Sessions.Add(session);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.Users.Remove(user);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.False(await db.Sessions.AnyAsync(
            row => row.Id == session.Id,
            TestContext.Current.CancellationToken));
    }

    private AuthDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>();
        AuthDbContext.Configure(options, _connectionString);
        return new AuthDbContext(options.Options);
    }

    public async ValueTask DisposeAsync()
    {
        if (_databaseName.Length > 0)
        {
            await postgres.DropDatabaseAsync(
                _databaseName,
                TestContext.Current.CancellationToken);
        }
    }
}
```

- [ ] **Step 4: Run the persistence test and verify the intended red state**

Run:

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --filter FullyQualifiedName~AuthPersistenceTests
```

Expected: build fails because the Identity/persistence entities and context do not exist.

- [ ] **Step 5: Implement the EF entities and exact relational mapping**

```csharp
// apps/api/src/Template.Infrastructure/Identity/ApplicationUser.cs
using Microsoft.AspNetCore.Identity;

namespace Template.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public required string DisplayName { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsLocalAutomation { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
```

```csharp
// apps/api/src/Template.Infrastructure/Persistence/AuthSessionEntity.cs
using System.Net;

namespace Template.Infrastructure.Persistence;

public sealed class AuthSessionEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required byte[] TicketKeyHash { get; set; }
    public required byte[] ProtectedTicket { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public IPAddress? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}
```

```csharp
// apps/api/src/Template.Infrastructure/Persistence/AuthDbContext.cs
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Template.Infrastructure.Identity;

namespace Template.Infrastructure.Persistence;

public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options)
    : IdentityUserContext<ApplicationUser, Guid>(options)
{
    public const string Schema = "auth";

    public DbSet<AuthSessionEntity> Sessions => Set<AuthSessionEntity>();

    public static void Configure(
        DbContextOptionsBuilder options,
        string connectionString) =>
        options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", Schema));

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(Schema);

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.ToTable("users");
            entity.Property(value => value.Id).HasColumnName("id");
            entity.Property(value => value.UserName).HasColumnName("user_name").HasMaxLength(254);
            entity.Property(value => value.NormalizedUserName)
                .HasColumnName("normalized_user_name")
                .HasMaxLength(254);
            entity.Property(value => value.Email).HasColumnName("email").HasMaxLength(254);
            entity.Property(value => value.NormalizedEmail)
                .HasColumnName("normalized_email")
                .HasMaxLength(254);
            entity.Property(value => value.EmailConfirmed).HasColumnName("email_confirmed");
            entity.Property(value => value.PasswordHash).HasColumnName("password_hash");
            entity.Property(value => value.SecurityStamp).HasColumnName("security_stamp");
            entity.Property(value => value.ConcurrencyStamp).HasColumnName("concurrency_stamp");
            entity.Property(value => value.PhoneNumber).HasColumnName("phone_number");
            entity.Property(value => value.PhoneNumberConfirmed)
                .HasColumnName("phone_number_confirmed");
            entity.Property(value => value.TwoFactorEnabled).HasColumnName("two_factor_enabled");
            entity.Property(value => value.LockoutEnd).HasColumnName("lockout_end");
            entity.Property(value => value.LockoutEnabled).HasColumnName("lockout_enabled");
            entity.Property(value => value.AccessFailedCount).HasColumnName("access_failed_count");
            entity.Property(value => value.DisplayName)
                .HasColumnName("display_name")
                .HasMaxLength(50)
                .IsRequired();
            entity.Property(value => value.ImageUrl)
                .HasColumnName("image_url")
                .HasMaxLength(2048);
            entity.Property(value => value.IsLocalAutomation)
                .HasColumnName("is_local_automation");
            entity.Property(value => value.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("timestamp with time zone");
            entity.Property(value => value.UpdatedAt)
                .HasColumnName("updated_at")
                .HasColumnType("timestamp with time zone");
            entity.HasIndex(value => value.NormalizedUserName)
                .IsUnique()
                .HasDatabaseName("ux_users_normalized_user_name");
            entity.HasIndex(value => value.NormalizedEmail)
                .IsUnique()
                .HasDatabaseName("ux_users_normalized_email");
        });

        builder.Entity<IdentityUserClaim<Guid>>(entity =>
        {
            entity.ToTable("user_claims");
            entity.Property(value => value.Id).HasColumnName("id");
            entity.Property(value => value.UserId).HasColumnName("user_id");
            entity.Property(value => value.ClaimType).HasColumnName("claim_type");
            entity.Property(value => value.ClaimValue).HasColumnName("claim_value");
            entity.HasIndex(value => value.UserId).HasDatabaseName("ix_user_claims_user_id");
        });

        builder.Entity<IdentityUserLogin<Guid>>(entity =>
        {
            entity.ToTable("user_logins");
            entity.Property(value => value.LoginProvider).HasColumnName("login_provider");
            entity.Property(value => value.ProviderKey).HasColumnName("provider_key");
            entity.Property(value => value.ProviderDisplayName).HasColumnName("provider_display_name");
            entity.Property(value => value.UserId).HasColumnName("user_id");
            entity.HasIndex(value => value.UserId).HasDatabaseName("ix_user_logins_user_id");
        });

        builder.Entity<IdentityUserToken<Guid>>(entity =>
        {
            entity.ToTable("user_tokens");
            entity.Property(value => value.UserId).HasColumnName("user_id");
            entity.Property(value => value.LoginProvider).HasColumnName("login_provider");
            entity.Property(value => value.Name).HasColumnName("name");
            entity.Property(value => value.Value).HasColumnName("value");
        });

        builder.Entity<AuthSessionEntity>(entity =>
        {
            entity.ToTable("sessions", table =>
                table.HasCheckConstraint(
                    "ck_sessions_expiry",
                    "expires_at > created_at"));
            entity.HasKey(value => value.Id).HasName("pk_sessions");
            entity.Property(value => value.Id).HasColumnName("id");
            entity.Property(value => value.UserId).HasColumnName("user_id");
            entity.Property(value => value.TicketKeyHash)
                .HasColumnName("ticket_key_hash")
                .HasColumnType("bytea");
            entity.Property(value => value.ProtectedTicket)
                .HasColumnName("protected_ticket")
                .HasColumnType("bytea");
            entity.Property(value => value.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("timestamp with time zone");
            entity.Property(value => value.UpdatedAt)
                .HasColumnName("updated_at")
                .HasColumnType("timestamp with time zone");
            entity.Property(value => value.ExpiresAt)
                .HasColumnName("expires_at")
                .HasColumnType("timestamp with time zone");
            entity.Property(value => value.IpAddress)
                .HasColumnName("ip_address")
                .HasColumnType("inet");
            entity.Property(value => value.UserAgent)
                .HasColumnName("user_agent")
                .HasMaxLength(512);
            entity.HasIndex(value => value.TicketKeyHash)
                .IsUnique()
                .HasDatabaseName("ux_sessions_ticket_key_hash");
            entity.HasIndex(value => value.UserId)
                .HasDatabaseName("ix_sessions_user_id");
            entity.HasIndex(value => value.ExpiresAt)
                .HasDatabaseName("ix_sessions_expires_at");
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(value => value.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_sessions_users_user_id");
        });
    }
}
```

```csharp
// apps/api/src/Template.Infrastructure/Persistence/AuthDbContextFactory.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Template.Infrastructure.Persistence;

public sealed class AuthDbContextFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
    public AuthDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Postgres") ??
            "Host=127.0.0.1;Database=template_design";
        var options = new DbContextOptionsBuilder<AuthDbContext>();
        AuthDbContext.Configure(options, connectionString);
        return new AuthDbContext(options.Options);
    }
}
```

```csharp
// apps/api/src/Template.Infrastructure/Properties/AssemblyInfo.cs
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Template.Api.Tests")]
```

- [ ] **Step 6: Generate and inspect the initial migration**

Run:

```bash
dotnet tool restore
dotnet ef migrations add InitialAuthPersistence \
  --project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --startup-project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --context AuthDbContext \
  --output-dir Persistence/Migrations
```

Expected: EF generates one migration, its designer, and
`AuthDbContextModelSnapshot.cs`. Inspect the generated `Up` method and confirm
it creates schema `auth`, the five planned tables, both unique indexes, both
session lookup indexes, the cascade FK, the expiry check, and no role tables.
Inspect `Down` and confirm it drops only the iteration-3 auth objects.

- [ ] **Step 7: Run the focused PostgreSQL tests**

Run:

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --filter FullyQualifiedName~AuthPersistenceTests
```

Expected: both tests pass against `postgres:18.4`.

- [ ] **Step 8: Verify model drift and the idempotent SQL script**

Run:

```bash
dotnet ef migrations has-pending-model-changes \
  --project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --startup-project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --context AuthDbContext

dotnet ef migrations script --idempotent \
  --project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --startup-project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --context AuthDbContext \
  --output /tmp/template-auth-idempotent.sql
test -s /tmp/template-auth-idempotent.sql
```

Expected: no pending model changes and a non-empty idempotent script.

- [ ] **Step 9: Commit**

```bash
git add .config/dotnet-tools.json Directory.Packages.props Template.sln \
  apps/api/src/Template.Infrastructure \
  apps/api/tests/Template.Api.Tests
git commit -m "Add PostgreSQL Identity persistence model"
```

## Task 5: Implement Identity, credential generation, and EF transactions

**Files:**

- Create: `apps/api/src/Template.Infrastructure/Authentication/CryptographicLocalAutomationCredentialGenerator.cs`
- Create: `apps/api/src/Template.Infrastructure/Identity/IdentityGateway.cs`
- Create: `apps/api/src/Template.Infrastructure/Persistence/EfAuthenticationUnitOfWork.cs`
- Create: `apps/api/src/Template.Infrastructure/Persistence/InfrastructureServiceCollectionExtensions.cs`
- Create: `apps/api/tests/Template.Api.Tests/IdentityGatewayTests.cs`

**Interfaces:**

- Consumes: `ILocalIdentityGateway`, `ILocalAutomationCredentialGenerator`, `IAuthenticationUnitOfWork`, `AuthDbContext`, and Identity Core.
- Produces: concrete scoped Identity/transaction adapters plus singleton cryptographic credential generation; `AddAuthInfrastructure(IConfiguration)` is the Api composition entry point.

- [ ] **Step 1: Write failing real-PostgreSQL Identity tests**

```csharp
// apps/api/tests/Template.Api.Tests/IdentityGatewayTests.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Template.Application.Authentication;
using Template.Application.Authentication.Ports;
using Template.Api.Tests.Infrastructure;
using Template.Infrastructure.Identity;
using Template.Infrastructure.Persistence;

namespace Template.Api.Tests;

public sealed class IdentityGatewayTests(PostgreSqlContainerFixture postgres)
    : IAsyncLifetime
{
    private string _databaseName = string.Empty;
    private ServiceProvider _services = null!;

    public async ValueTask InitializeAsync()
    {
        var database = await postgres.CreateDatabaseAsync(
            TestContext.Current.CancellationToken);
        _databaseName = database.DatabaseName;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = database.ConnectionString
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(TimeProvider.System);
        services.AddAuthentication();
        services.AddAuthInfrastructure(configuration);
        _services = services.BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AuthDbContext>()
            .Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void GeneratorProducesReservedEmailAndThirtyTwoRandomPasswordBytes()
    {
        var generator = _services
            .GetRequiredService<ILocalAutomationCredentialGenerator>();

        var first = generator.Generate();
        var second = generator.Generate();
        var passwordHex = first.Password["local-".Length..];

        Assert.True(LocalAutomationCredentialPolicy.IsLocalEmail(first.Email));
        Assert.StartsWith("Local Automation ", first.Name);
        Assert.Equal(64, passwordHex.Length);
        Assert.True(Convert.TryFromHexString(passwordHex, new byte[32], out var bytesWritten));
        Assert.Equal(32, bytesWritten);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task CreateHashesPasswordAndMarksUnverifiedLocalUser()
    {
        await using var scope = _services.CreateAsyncScope();
        var gateway = scope.ServiceProvider.GetRequiredService<ILocalIdentityGateway>();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var credentials = new LocalAutomationCredentials(
            "Local Identity",
            "local-agent+identity@local-agent.test",
            "local-identity-password");

        var created = await gateway.CreateLocalAsync(
            credentials,
            TestContext.Current.CancellationToken);
        var row = await db.Users.SingleAsync(
            user => user.Id == created.Id.Value,
            TestContext.Current.CancellationToken);

        Assert.True(row.IsLocalAutomation);
        Assert.False(row.EmailConfirmed);
        Assert.NotNull(row.PasswordHash);
        Assert.DoesNotContain(credentials.Password, row.PasswordHash, StringComparison.Ordinal);
        Assert.Equal(credentials.Email, row.UserName);
        Assert.Equal(credentials.Email, row.Email);
    }

    [Fact]
    public async Task DuplicateNormalizedEmailUsesStableDuplicateException()
    {
        await using var scope = _services.CreateAsyncScope();
        var gateway = scope.ServiceProvider.GetRequiredService<ILocalIdentityGateway>();
        var first = new LocalAutomationCredentials(
            "First",
            "local-agent+duplicate@local-agent.test",
            "local-duplicate-password");
        var second = first with
        {
            Name = "Second",
            Email = "LOCAL-AGENT+DUPLICATE@LOCAL-AGENT.TEST"
        };
        await gateway.CreateLocalAsync(first, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<DuplicateLocalIdentityException>(
            () => gateway.CreateLocalAsync(
                second,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FifthBadPasswordLocksUserAndNeverReturnsIdentity()
    {
        await using var scope = _services.CreateAsyncScope();
        var gateway = scope.ServiceProvider.GetRequiredService<ILocalIdentityGateway>();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var credentials = new LocalAutomationCredentials(
            "Locked User",
            "local-agent+locked@local-agent.test",
            "local-correct-password");
        var created = await gateway.CreateLocalAsync(
            credentials,
            TestContext.Current.CancellationToken);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.Null(await gateway.CheckLocalPasswordAsync(
                credentials.Email,
                "local-wrong-password",
                TestContext.Current.CancellationToken));
        }

        var row = await db.Users.SingleAsync(
            user => user.Id == created.Id.Value,
            TestContext.Current.CancellationToken);
        Assert.NotNull(row.LockoutEnd);
        Assert.True(row.LockoutEnd > DateTimeOffset.UtcNow);
        Assert.Null(await gateway.CheckLocalPasswordAsync(
            credentials.Email,
            credentials.Password,
            TestContext.Current.CancellationToken));
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        if (_databaseName.Length > 0)
        {
            await postgres.DropDatabaseAsync(
                _databaseName,
                TestContext.Current.CancellationToken);
        }
    }
}
```

- [ ] **Step 2: Run the focused class and verify it fails**

Run:

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --filter FullyQualifiedName~IdentityGatewayTests
```

Expected: build fails because `AddAuthInfrastructure` and its adapters do not exist.

- [ ] **Step 3: Implement cryptographic credentials**

```csharp
// apps/api/src/Template.Infrastructure/Authentication/CryptographicLocalAutomationCredentialGenerator.cs
using System.Security.Cryptography;
using Template.Application.Authentication;
using Template.Application.Authentication.Ports;

namespace Template.Infrastructure.Authentication;

internal sealed class CryptographicLocalAutomationCredentialGenerator
    : ILocalAutomationCredentialGenerator
{
    public LocalAutomationCredentials Generate()
    {
        var seed = Convert.ToHexString(RandomNumberGenerator.GetBytes(8))
            .ToLowerInvariant();
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
            .ToLowerInvariant();
        return new LocalAutomationCredentials(
            $"Local Automation {seed}",
            $"{LocalAutomationCredentialPolicy.EmailPrefix}{seed}@{LocalAutomationCredentialPolicy.EmailDomain}",
            $"local-{password}");
    }
}
```

- [ ] **Step 4: Implement Identity create/check/delete behavior**

```csharp
// apps/api/src/Template.Infrastructure/Identity/IdentityGateway.cs
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Template.Application.Authentication;
using Template.Application.Authentication.Ports;
using Template.Domain.Authentication;

namespace Template.Infrastructure.Identity;

internal sealed class IdentityGateway(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signInManager,
    TimeProvider timeProvider)
    : ILocalIdentityGateway
{
    public async Task<AuthUser> CreateLocalAsync(
        LocalAutomationCredentials credentials,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = timeProvider.GetUtcNow();
        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(now),
            UserName = credentials.Email,
            Email = credentials.Email,
            DisplayName = credentials.Name,
            EmailConfirmed = false,
            IsLocalAutomation = true,
            LockoutEnabled = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        try
        {
            var result = await users.CreateAsync(user, credentials.Password);
            if (!result.Succeeded)
            {
                if (result.Errors.Any(error =>
                        error.Code is "DuplicateEmail" or "DuplicateUserName"))
                {
                    throw new DuplicateLocalIdentityException();
                }

                throw new InvalidOperationException(
                    $"Identity user creation failed with codes: {string.Join(',', result.Errors.Select(error => error.Code))}");
            }
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
                  {
                      SqlState: PostgresErrorCodes.UniqueViolation
                  })
        {
            throw new DuplicateLocalIdentityException();
        }

        return Map(user);
    }

    public async Task<AuthUser?> CheckLocalPasswordAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await users.FindByEmailAsync(email);
        if (user is null || !user.IsLocalAutomation)
        {
            return null;
        }

        var result = await signInManager.CheckPasswordSignInAsync(
            user,
            password,
            lockoutOnFailure: true);
        return result.Succeeded ? Map(user) : null;
    }

    public async Task DeleteAsync(UserId userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await users.FindByIdAsync(userId.Value.ToString());
        if (user is null)
        {
            return;
        }

        var result = await users.DeleteAsync(user);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Identity user deletion failed with codes: {string.Join(',', result.Errors.Select(error => error.Code))}");
        }
    }

    private static AuthUser Map(ApplicationUser user) =>
        new(
            new UserId(user.Id),
            user.DisplayName,
            user.Email ?? string.Empty,
            user.EmailConfirmed,
            user.ImageUrl,
            user.IsLocalAutomation);
}
```

- [ ] **Step 5: Implement the EF unit of work and DI entry point**

```csharp
// apps/api/src/Template.Infrastructure/Persistence/EfAuthenticationUnitOfWork.cs
using Microsoft.EntityFrameworkCore;
using Template.Application.Authentication.Ports;

namespace Template.Infrastructure.Persistence;

internal sealed class EfAuthenticationUnitOfWork(AuthDbContext db)
    : IAuthenticationUnitOfWork
{
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            return await action(cancellationToken);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(
            cancellationToken);
        try
        {
            var result = await action(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            throw;
        }
    }
}
```

```csharp
// apps/api/src/Template.Infrastructure/Persistence/InfrastructureServiceCollectionExtensions.cs
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Template.Application.Authentication.Ports;
using Template.Infrastructure.Authentication;
using Template.Infrastructure.Identity;

namespace Template.Infrastructure.Persistence;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddAuthInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<AuthDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("Postgres");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "ConnectionStrings:Postgres is required when authentication persistence is used.");
            }

            AuthDbContext.Configure(options, connectionString);
        });

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 1;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                options.SignIn.RequireConfirmedEmail = false;
            })
            .AddEntityFrameworkStores<AuthDbContext>()
            .AddSignInManager();

        services.AddScoped<ILocalIdentityGateway, IdentityGateway>();
        services.AddScoped<IAuthenticationUnitOfWork, EfAuthenticationUnitOfWork>();
        services.AddSingleton<
            ILocalAutomationCredentialGenerator,
            CryptographicLocalAutomationCredentialGenerator>();
        return services;
    }
}
```

- [ ] **Step 6: Run the focused Identity tests and all Application tests**

Run:

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --filter FullyQualifiedName~IdentityGatewayTests
dotnet test apps/api/tests/Template.Application.Tests/Template.Application.Tests.csproj
```

Expected: all focused Identity and Application tests pass; the lockout row has
a future `lockout_end`.

- [ ] **Step 7: Commit**

```bash
git add \
  apps/api/src/Template.Infrastructure/Authentication/CryptographicLocalAutomationCredentialGenerator.cs \
  apps/api/src/Template.Infrastructure/Identity/IdentityGateway.cs \
  apps/api/src/Template.Infrastructure/Persistence/EfAuthenticationUnitOfWork.cs \
  apps/api/src/Template.Infrastructure/Persistence/InfrastructureServiceCollectionExtensions.cs \
  apps/api/tests/Template.Api.Tests/IdentityGatewayTests.cs
git commit -m "Implement Identity authentication adapters"
```

## Task 6: Persist cookie tickets and expose the browser-session gateway

**Files:**

- Create: `apps/api/src/Template.Infrastructure/Authentication/BrowserSessionClaimTypes.cs`
- Create: `apps/api/src/Template.Infrastructure/Authentication/PostgresTicketStore.cs`
- Create: `apps/api/src/Template.Infrastructure/Authentication/BrowserSessionGateway.cs`
- Modify: `apps/api/src/Template.Infrastructure/Persistence/InfrastructureServiceCollectionExtensions.cs`
- Create: `apps/api/tests/Template.Api.Tests/PostgresTicketStoreTests.cs`

**Interfaces:**

- Consumes: .NET 10 `ITicketStore` HttpContext overloads, Data Protection, the request-scoped `AuthDbContext`, and `IBrowserSessionGateway`.
- Produces: singleton `PostgresTicketStore`, scoped `BrowserSessionGateway`, non-public `urn:template:session_id` principal claim, and database revocation semantics.

- [ ] **Step 1: Write failing store/retrieve/renew/remove/expiry tests**

```csharp
// apps/api/tests/Template.Api.Tests/PostgresTicketStoreTests.cs
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Template.Api.Tests.Infrastructure;
using Template.Infrastructure.Authentication;
using Template.Infrastructure.Identity;
using Template.Infrastructure.Persistence;

namespace Template.Api.Tests;

public sealed class PostgresTicketStoreTests(PostgreSqlContainerFixture postgres)
    : IAsyncLifetime
{
    private string _databaseName = string.Empty;
    private ServiceProvider _services = null!;
    private Guid _userId;
    private readonly MutableTimeProvider _time = new(
        new DateTimeOffset(2026, 7, 24, 0, 0, 0, TimeSpan.Zero));

    public async ValueTask InitializeAsync()
    {
        var database = await postgres.CreateDatabaseAsync(
            TestContext.Current.CancellationToken);
        _databaseName = database.DatabaseName;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = database.ConnectionString
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddDataProtection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<TimeProvider>(_time);
        services.AddHttpContextAccessor();
        services.AddAuthentication("TicketStoreTest")
            .AddCookie("TicketStoreTest");
        services.AddAuthInfrastructure(configuration);
        _services = services.BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        _userId = Guid.CreateVersion7();
        var now = _time.GetUtcNow();
        db.Users.Add(new ApplicationUser
        {
            Id = _userId,
            UserName = "local-agent+ticket@local-agent.test",
            NormalizedUserName = "LOCAL-AGENT+TICKET@LOCAL-AGENT.TEST",
            Email = "local-agent+ticket@local-agent.test",
            NormalizedEmail = "LOCAL-AGENT+TICKET@LOCAL-AGENT.TEST",
            DisplayName = "Ticket User",
            IsLocalAutomation = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StoreRetrieveRenewAndRemoveUseOnlyHashedLookupKey()
    {
        await using var scope = _services.CreateAsyncScope();
        var context = CreateHttpContext(scope.ServiceProvider);
        var store = scope.ServiceProvider.GetRequiredService<PostgresTicketStore>();
        var sessionId = Guid.CreateVersion7();
        var ticket = CreateTicket(sessionId, _time.GetUtcNow().AddDays(7));

        var key = await store.StoreAsync(
            ticket,
            context,
            TestContext.Current.CancellationToken);
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var row = await db.Sessions.SingleAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(sessionId, row.Id);
        Assert.Equal(32, row.TicketKeyHash.Length);
        Assert.DoesNotContain(
            key,
            Convert.ToHexString(row.TicketKeyHash),
            StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(TicketSerializer.Default.Serialize(ticket), row.ProtectedTicket);

        var retrieved = await store.RetrieveAsync(
            key,
            context,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            sessionId.ToString(),
            retrieved!.Principal.FindFirstValue(BrowserSessionClaimTypes.SessionId));

        var renewedExpiry = _time.GetUtcNow().AddDays(8);
        var renewed = CreateTicket(sessionId, renewedExpiry);
        await store.RenewAsync(
            key,
            renewed,
            context,
            TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();
        Assert.Equal(
            renewedExpiry,
            (await db.Sessions.SingleAsync(TestContext.Current.CancellationToken)).ExpiresAt,
            TimeSpan.FromSeconds(1));

        await store.RemoveAsync(
            key,
            context,
            TestContext.Current.CancellationToken);
        Assert.False(await db.Sessions.AnyAsync(TestContext.Current.CancellationToken));
        Assert.Null(await store.RetrieveAsync(
            key,
            context,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RetrieveLazilyDeletesExpiredSession()
    {
        await using var scope = _services.CreateAsyncScope();
        var context = CreateHttpContext(scope.ServiceProvider);
        var store = scope.ServiceProvider.GetRequiredService<PostgresTicketStore>();
        var key = await store.StoreAsync(
            CreateTicket(Guid.CreateVersion7(), _time.GetUtcNow().AddMinutes(1)),
            context,
            TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromMinutes(2));

        Assert.Null(await store.RetrieveAsync(
            key,
            context,
            TestContext.Current.CancellationToken));
        Assert.False(await scope.ServiceProvider.GetRequiredService<AuthDbContext>()
            .Sessions.AnyAsync(TestContext.Current.CancellationToken));
    }

    private DefaultHttpContext CreateHttpContext(IServiceProvider services)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        context.Request.Headers.UserAgent = "ticket-store-test";
        services.GetRequiredService<IHttpContextAccessor>().HttpContext = context;
        return context;
    }

    private AuthenticationTicket CreateTicket(Guid sessionId, DateTimeOffset expiresAt)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, _userId.ToString()),
                new Claim(BrowserSessionClaimTypes.SessionId, sessionId.ToString())
            ],
            "TicketStoreTest");
        return new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IsPersistent = true,
                IssuedUtc = _time.GetUtcNow(),
                ExpiresUtc = expiresAt,
                AllowRefresh = true
            },
            "TicketStoreTest");
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow)
        : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        internal void Advance(TimeSpan value) => _utcNow += value;
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        if (_databaseName.Length > 0)
        {
            await postgres.DropDatabaseAsync(
                _databaseName,
                TestContext.Current.CancellationToken);
        }
    }
}
```

- [ ] **Step 2: Run the focused tests and verify they fail**

Run:

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --filter FullyQualifiedName~PostgresTicketStoreTests
```

Expected: build fails because the ticket store and claim constant do not exist.

- [ ] **Step 3: Implement the database-backed `ITicketStore`**

```csharp
// apps/api/src/Template.Infrastructure/Authentication/BrowserSessionClaimTypes.cs
namespace Template.Infrastructure.Authentication;

internal static class BrowserSessionClaimTypes
{
    internal const string SessionId = "urn:template:session_id";
}
```

```csharp
// apps/api/src/Template.Infrastructure/Authentication/PostgresTicketStore.cs
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Template.Infrastructure.Persistence;

namespace Template.Infrastructure.Authentication;

public sealed class PostgresTicketStore(
    IDataProtectionProvider dataProtectionProvider,
    IHttpContextAccessor httpContextAccessor,
    TimeProvider timeProvider)
    : ITicketStore
{
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(
        "Template.Infrastructure.Authentication.PostgresTicketStore.v1");

    public Task<string> StoreAsync(AuthenticationTicket ticket) =>
        StoreAsync(ticket, CancellationToken.None);

    public Task<string> StoreAsync(
        AuthenticationTicket ticket,
        CancellationToken cancellationToken) =>
        StoreAsync(ticket, RequiredHttpContext(), cancellationToken);

    public async Task<string> StoreAsync(
        AuthenticationTicket ticket,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var key = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var now = timeProvider.GetUtcNow();
        var expiresAt = ticket.Properties.ExpiresUtc ??
            throw new InvalidOperationException("A persistent ticket requires ExpiresUtc.");
        var sessionId = ParseRequiredGuid(
            ticket.Principal,
            BrowserSessionClaimTypes.SessionId);
        var userId = ParseRequiredGuid(ticket.Principal, ClaimTypes.NameIdentifier);
        var userAgent = httpContext.Request.Headers.UserAgent.ToString();
        if (userAgent.Length > 512)
        {
            userAgent = userAgent[..512];
        }

        var db = GetDb(httpContext);
        db.Sessions.Add(new AuthSessionEntity
        {
            Id = sessionId,
            UserId = userId,
            TicketKeyHash = HashKey(key),
            ProtectedTicket = Protect(ticket),
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = expiresAt.ToUniversalTime(),
            IpAddress = httpContext.Connection.RemoteIpAddress,
            UserAgent = userAgent.Length == 0 ? null : userAgent
        });
        await db.SaveChangesAsync(cancellationToken);
        return key;
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket) =>
        RenewAsync(key, ticket, CancellationToken.None);

    public Task RenewAsync(
        string key,
        AuthenticationTicket ticket,
        CancellationToken cancellationToken) =>
        RenewAsync(key, ticket, RequiredHttpContext(), cancellationToken);

    public async Task RenewAsync(
        string key,
        AuthenticationTicket ticket,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var db = GetDb(httpContext);
        var hash = HashKey(key);
        var row = await db.Sessions.SingleOrDefaultAsync(
            session => session.TicketKeyHash.SequenceEqual(hash),
            cancellationToken);
        if (row is null)
        {
            return;
        }

        row.ProtectedTicket = Protect(ticket);
        row.UpdatedAt = timeProvider.GetUtcNow();
        row.ExpiresAt = (ticket.Properties.ExpiresUtc ??
            throw new InvalidOperationException("A persistent ticket requires ExpiresUtc."))
            .ToUniversalTime();
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key) =>
        RetrieveAsync(key, CancellationToken.None);

    public Task<AuthenticationTicket?> RetrieveAsync(
        string key,
        CancellationToken cancellationToken) =>
        RetrieveAsync(key, RequiredHttpContext(), cancellationToken);

    public async Task<AuthenticationTicket?> RetrieveAsync(
        string key,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var db = GetDb(httpContext);
        var hash = HashKey(key);
        var row = await db.Sessions.SingleOrDefaultAsync(
            session => session.TicketKeyHash.SequenceEqual(hash),
            cancellationToken);
        if (row is null)
        {
            return null;
        }

        if (row.ExpiresAt <= timeProvider.GetUtcNow())
        {
            db.Sessions.Remove(row);
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }

        try
        {
            return TicketSerializer.Default.Deserialize(
                _protector.Unprotect(row.ProtectedTicket));
        }
        catch (CryptographicException)
        {
            db.Sessions.Remove(row);
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }
    }

    public Task RemoveAsync(string key) =>
        RemoveAsync(key, CancellationToken.None);

    public Task RemoveAsync(string key, CancellationToken cancellationToken) =>
        RemoveAsync(key, RequiredHttpContext(), cancellationToken);

    public async Task RemoveAsync(
        string key,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var db = GetDb(httpContext);
        var hash = HashKey(key);
        var row = await db.Sessions.SingleOrDefaultAsync(
            session => session.TicketKeyHash.SequenceEqual(hash),
            cancellationToken);
        if (row is null)
        {
            return;
        }

        db.Sessions.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
    }

    private HttpContext RequiredHttpContext() =>
        httpContextAccessor.HttpContext ??
        throw new InvalidOperationException(
            "PostgresTicketStore requires the .NET 10 HttpContext overload.");

    private static AuthDbContext GetDb(HttpContext context) =>
        context.RequestServices.GetRequiredService<AuthDbContext>();

    private static byte[] HashKey(string key) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(key));

    private byte[] Protect(AuthenticationTicket ticket) =>
        _protector.Protect(TicketSerializer.Default.Serialize(ticket));

    private static Guid ParseRequiredGuid(ClaimsPrincipal principal, string claimType)
    {
        var value = principal.FindFirstValue(claimType);
        return Guid.TryParse(value, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Required claim '{claimType}' is missing.");
    }
}
```

- [ ] **Step 4: Implement the HTTP-backed browser-session port**

```csharp
// apps/api/src/Template.Infrastructure/Authentication/BrowserSessionGateway.cs
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Template.Application.Authentication;
using Template.Application.Authentication.Ports;
using Template.Domain.Authentication;
using Template.Infrastructure.Identity;
using Template.Infrastructure.Persistence;

namespace Template.Infrastructure.Authentication;

internal sealed class BrowserSessionGateway(
    IHttpContextAccessor httpContextAccessor,
    IUserClaimsPrincipalFactory<ApplicationUser> principalFactory,
    UserManager<ApplicationUser> users,
    AuthDbContext db,
    TimeProvider timeProvider)
    : IBrowserSessionGateway
{
    internal static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(7);

    public async Task<AuthenticatedSession?> GetCurrentAsync(
        CancellationToken cancellationToken)
    {
        var context = RequiredHttpContext();
        if (context.User.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(
                context.User.FindFirstValue(BrowserSessionClaimTypes.SessionId),
                out var sessionId))
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var value = await (
            from session in db.Sessions.AsNoTracking()
            join user in db.Users.AsNoTracking() on session.UserId equals user.Id
            where session.Id == sessionId && session.ExpiresAt > now
            select new { session, user })
            .SingleOrDefaultAsync(cancellationToken);
        return value is null
            ? null
            : new AuthenticatedSession(Map(value.user), Map(value.session));
    }

    public async Task<BrowserSession> SignInAsync(
        AuthUser user,
        CancellationToken cancellationToken)
    {
        var context = RequiredHttpContext();
        var applicationUser = await users.FindByIdAsync(user.Id.Value.ToString()) ??
            throw new InvalidOperationException("Identity user disappeared before sign-in.");
        var principal = await principalFactory.CreateAsync(applicationUser);
        var identity = principal.Identity as ClaimsIdentity ??
            throw new InvalidOperationException("Identity principal has no ClaimsIdentity.");
        var sessionId = SessionId.New();
        identity.AddClaim(new Claim(
            BrowserSessionClaimTypes.SessionId,
            sessionId.Value.ToString()));
        var window = SessionWindow.Start(timeProvider.GetUtcNow(), SessionLifetime);

        await context.SignInAsync(
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                AllowRefresh = true,
                IssuedUtc = window.CreatedAt,
                ExpiresUtc = window.ExpiresAt
            });

        var stored = await db.Sessions.AsNoTracking().SingleAsync(
            row => row.Id == sessionId.Value,
            cancellationToken);
        return Map(stored);
    }

    public Task SignOutAsync(CancellationToken cancellationToken) =>
        RequiredHttpContext().SignOutAsync();

    private HttpContext RequiredHttpContext() =>
        httpContextAccessor.HttpContext ??
        throw new InvalidOperationException("A browser-session operation requires HttpContext.");

    private static AuthUser Map(ApplicationUser user) =>
        new(
            new UserId(user.Id),
            user.DisplayName,
            user.Email ?? string.Empty,
            user.EmailConfirmed,
            user.ImageUrl,
            user.IsLocalAutomation);

    private static BrowserSession Map(AuthSessionEntity session) =>
        new(
            new SessionId(session.Id),
            session.CreatedAt,
            session.UpdatedAt,
            session.ExpiresAt);
}
```

Add these registrations inside `AddAuthInfrastructure`:

```csharp
services.AddHttpContextAccessor();
services.AddScoped<IBrowserSessionGateway, BrowserSessionGateway>();
services.AddSingleton<PostgresTicketStore>();
```

- [ ] **Step 5: Run the direct ticket-store tests**

Run:

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --filter FullyQualifiedName~PostgresTicketStoreTests
```

Expected: both tests pass; expired and removed keys retrieve as `null`.

- [ ] **Step 6: Run persistence and Identity regression tests**

Run:

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --filter "FullyQualifiedName~AuthPersistenceTests|FullyQualifiedName~IdentityGatewayTests|FullyQualifiedName~PostgresTicketStoreTests"
```

Expected: all selected PostgreSQL tests pass against one exact-pinned container
server with isolated databases.

- [ ] **Step 7: Commit**

```bash
git add \
  apps/api/src/Template.Infrastructure/Authentication \
  apps/api/src/Template.Infrastructure/Persistence/InfrastructureServiceCollectionExtensions.cs \
  apps/api/tests/Template.Api.Tests/PostgresTicketStoreTests.cs
git commit -m "Persist browser authentication tickets"
```

## Task 7: Compose persistent auth, cookie policy, test databases, and readiness

**Files:**

- Create: `apps/api/src/Template.Infrastructure/Health/AuthDatabaseHealthCheck.cs`
- Modify: `apps/api/src/Template.Api/Authentication/ApiAuthenticationDefaults.cs`
- Modify: `apps/api/src/Template.Api/Authentication/ApiPolicies.cs`
- Modify: `apps/api/src/Template.Api/Authentication/AuthenticationServiceCollectionExtensions.cs`
- Modify: `apps/api/src/Template.Api/Endpoints/EndpointModuleExtensions.cs`
- Modify: `apps/api/src/Template.Api/Program.cs`
- Modify: `apps/api/src/Template.Api/appsettings.json`
- Modify: `apps/api/src/Template.Api/appsettings.Development.json`
- Modify: `apps/api/src/Template.Api/Properties/launchSettings.json`
- Rewrite: `apps/api/tests/Template.Api.Tests/Infrastructure/ApiWebApplicationFactory.cs`
- Create: `apps/api/tests/Template.Api.Tests/DatabaseReadinessTests.cs`
- Modify: existing API tests to call `factory.CreateApiClient()`

**Interfaces:**

- Consumes: `AddAuthInfrastructure`, `PostgresTicketStore`, Application auth services, and the existing endpoint-module/auth-policy seams.
- Produces: real `Template.Session` cookie defaults, policy `Api.BrowserSession`, migrated isolated PostgreSQL per API test class, and database-aware readiness without database-aware liveness.

- [ ] **Step 1: Write the failing readiness/configuration tests**

```csharp
// apps/api/tests/Template.Api.Tests/DatabaseReadinessTests.cs
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Template.Api.Tests.Infrastructure;

namespace Template.Api.Tests;

public sealed class DatabaseReadinessTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task MigratedPostgresIsReady()
    {
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(
            "/api/health/ready",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UnavailablePostgresFailsReadinessButNotLiveness()
    {
        await using var unavailable = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Postgres"] =
                        "Host=127.0.0.1;Port=1;Database=missing;Username=missing;Timeout=1"
                })));
        using var client = unavailable.CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var ready = await client.GetAsync(
            "/api/health/ready",
            TestContext.Current.CancellationToken);
        using var live = await client.GetAsync(
            "/api/health/live",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    [Fact]
    public async Task ConnectedButUnmigratedPostgresFailsReadinessButNotLiveness()
    {
        var database = await factory.CreateUnmigratedDatabaseAsync(
            TestContext.Current.CancellationToken);
        try
        {
            await using var unmigrated = factory.WithWebHostBuilder(builder =>
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["ConnectionStrings:Postgres"] =
                                database.ConnectionString
                        })));
            using var client = unmigrated.CreateClient(new()
            {
                BaseAddress = new Uri("https://localhost")
            });

            using var ready = await client.GetAsync(
                "/api/health/ready",
                TestContext.Current.CancellationToken);
            using var live = await client.GetAsync(
                "/api/health/live",
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        }
        finally
        {
            await factory.DropDatabaseAsync(
                database.DatabaseName,
                CancellationToken.None);
        }
    }
}
```

Also extend `SystemEndpointTests.ProductionCookieUsesHostPrefixSecurityRequirements`
with:

```csharp
Assert.Equal(TimeSpan.FromDays(7), options.ExpireTimeSpan);
Assert.True(options.SlidingExpiration);
Assert.IsType<PostgresTicketStore>(options.SessionStore);
```

- [ ] **Step 2: Run focused tests and verify the red state**

Run:

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --filter "FullyQualifiedName~DatabaseReadinessTests|FullyQualifiedName~ProductionCookieUsesHostPrefixSecurityRequirements"
```

Expected: tests fail because the factory has no PostgreSQL configuration,
readiness has no database check, and the cookie has no persistent ticket store.

- [ ] **Step 3: Implement the bounded schema-aware readiness check**

```csharp
// apps/api/src/Template.Infrastructure/Health/AuthDatabaseHealthCheck.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Template.Infrastructure.Health;

public sealed class AuthDatabaseHealthCheck(IConfiguration configuration)
    : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return HealthCheckResult.Unhealthy();
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT EXISTS (SELECT 1 FROM auth.users LIMIT 1)";
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (
            exception is NpgsqlException or TimeoutException or OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy();
        }
    }
}
```

- [ ] **Step 4: Extend the established dual-scheme cookie composition and add the browser-only policy**

> **Security-critical supersession:** Task 6 has already established the
> dual-scheme rotation composition described in the security amendment above.
> Do **not** replace it with a single `AddCookie` registration or set the
> primary scheme as the default sign-in scheme. The primary handler reads and
> signs out; `Template.Session.Issuer` is the explicit write-only issuer used by
> `BrowserSessionGateway` to force a new opaque lookup key. Preserve shared
> `PostgresTicketStore`, shared explicit `TicketDataFormat`, shared host-cookie
> attributes, `PrimaryBrowserSessionCookieManager`, and
> `WriteOnlyBrowserSessionCookieManager`.

```csharp
// apps/api/src/Template.Api/Authentication/ApiAuthenticationDefaults.cs
using Template.Infrastructure.Authentication;

namespace Template.Api.Authentication;

internal static class ApiAuthenticationDefaults
{
    internal const string SchemeName = BrowserSessionAuthenticationDefaults.PrimaryScheme;
    internal const string IssuerSchemeName = BrowserSessionAuthenticationDefaults.IssuerScheme;
    internal const string CookieName = BrowserSessionAuthenticationDefaults.CookieName;
    internal static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);
}
```

```csharp
// apps/api/src/Template.Api/Authentication/ApiPolicies.cs
namespace Template.Api.Authentication;

internal static class ApiPolicies
{
    internal const string BrowserSession = "Api.BrowserSession";
}
```

```csharp
// apps/api/src/Template.Api/Authentication/AuthenticationServiceCollectionExtensions.cs
// Retain Task 6's two AddCookie registrations and shared persistent-ticket
// configuration. Extend ConfigureHostCookie so it applies these options to
// *both* handlers:
options.ExpireTimeSpan = ApiAuthenticationDefaults.Lifetime;
options.SlidingExpiration = true;

// Keep the existing primary redirect overrides and default authenticate,
// challenge, forbid and sign-out selections. Do not add DefaultSignInScheme.
// Add/retain this browser-only policy:
services.AddAuthorization(options =>
    options.AddPolicy(
        ApiPolicies.BrowserSession,
        policy => policy
            .AddAuthenticationSchemes(ApiAuthenticationDefaults.SchemeName)
            .RequireAuthenticatedUser()));
```

Change the versioned API group in
`EndpointModuleExtensions.MapEndpointModules` to:

```csharp
endpoints.MapGroup("/api/v1")
    .RequireAuthorization(ApiPolicies.BrowserSession)
```

- [ ] **Step 5: Register Infrastructure, Application services, and readiness**

Replace the corresponding service-registration block in `Program.cs` with:

```csharp
builder.Services.AddValidation();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddAuthInfrastructure(builder.Configuration);
builder.Services.AddScoped<LocalAutomationAuthService>();
builder.Services.AddScoped<BrowserAuthenticationService>();
builder.Services
    .AddHealthChecks()
    .AddCheck<AuthDatabaseHealthCheck>(
        "postgres-auth-schema",
        tags: ["ready"],
        timeout: TimeSpan.FromSeconds(2));
builder.Services.AddApiAuthentication();
builder.Services.AddApiErrorHandling();
builder.Services.AddApiOpenApi();
builder.Services.AddEndpointModules();
```

Add these `using` directives:

```csharp
using Template.Application.Authentication;
using Template.Infrastructure.Health;
using Template.Infrastructure.Persistence;
```

Do not call `Migrate`, `EnsureCreated`, or `EnsureDeleted` from Api runtime code.

- [ ] **Step 6: Add safe committed defaults and an explicit launch-profile opt-in**

Add to `appsettings.json`:

```json
"LocalAutomationAuth": {
  "Enabled": false,
  "CreateRateLimitPerMinute": 20,
  "SignInRateLimitPerFiveMinutes": 10
}
```

Keep `appsettings.Development.json` free of connection credentials and add:

```json
"LocalAutomationAuth": {
  "Enabled": false
}
```

Add this explicit environment variable to the `http` launch profile:

```json
"LocalAutomationAuth__Enabled": "true"
```

Do not add `ConnectionStrings:Postgres` to committed settings; local developers
use `ConnectionStrings__Postgres` or user-secrets.

- [ ] **Step 7: Rewrite the API factory to create and migrate an isolated database**

Retain the current logging provider, test-only endpoint module, test
authorization policy, and temporary `TestAuthenticationHandler` registration
until Task 9 replaces header auth with real cookies. Replace the factory body
with:

```csharp
// apps/api/tests/Template.Api.Tests/Infrastructure/ApiWebApplicationFactory.cs
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Template.Api.Endpoints;
using Template.Infrastructure.Persistence;

namespace Template.Api.Tests.Infrastructure;

public sealed class ApiWebApplicationFactory(
    PostgreSqlContainerFixture postgres)
    : WebApplicationFactory<Program>, IAsyncLifetime
{
    private string _databaseName = string.Empty;
    private string _connectionString = string.Empty;

    public async ValueTask InitializeAsync()
    {
        (_databaseName, _connectionString) = await postgres.CreateDatabaseAsync(
            TestContext.Current.CancellationToken);
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AuthDbContext>()
            .Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    public HttpClient CreateApiClient() =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });

    public async Task ResetAuthDataAsync(CancellationToken cancellationToken)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        await db.Sessions.ExecuteDeleteAsync(cancellationToken);
        await db.Users.ExecuteDeleteAsync(cancellationToken);
    }

    public Task<(string DatabaseName, string ConnectionString)>
        CreateUnmigratedDatabaseAsync(CancellationToken cancellationToken) =>
        postgres.CreateDatabaseAsync(cancellationToken);

    public Task DropDatabaseAsync(
        string databaseName,
        CancellationToken cancellationToken) =>
        postgres.DropDatabaseAsync(databaseName, cancellationToken);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _connectionString,
                ["LocalAutomationAuth:Enabled"] = "true",
                ["LocalAutomationAuth:CreateRateLimitPerMinute"] = "20",
                ["LocalAutomationAuth:SignInRateLimitPerFiveMinutes"] = "10"
            }));
        builder.ConfigureLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Debug);
            logging.AddFilter<CapturedLogProvider>(level => level >= LogLevel.Debug);
        });
        builder.ConfigureTestServices(services =>
        {
            services
                .AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultForbidScheme = TestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName,
                    _ => { });

            services.AddAuthorization(options =>
                options.AddPolicy(
                    TestEndpointModule.ForbiddenPolicy,
                    policy => policy.RequireClaim("test.permission", "granted")));
            services.AddSingleton<IEndpointModule, TestEndpointModule>();
            services.AddSingleton<CapturedLogProvider>();
            services.AddSingleton<ILoggerProvider>(
                provider => provider.GetRequiredService<CapturedLogProvider>());
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_databaseName.Length > 0)
        {
            await postgres.DropDatabaseAsync(
                _databaseName,
                CancellationToken.None);
        }
    }
}
```

Replace every existing `factory.CreateClient()` in API tests with
`factory.CreateApiClient()`. For a child `WithWebHostBuilder` factory, create
the client with `BaseAddress = new Uri("https://localhost")` as shown in the
readiness test.

- [ ] **Step 8: Run readiness, cookie, and existing foundation tests**

Run:

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --filter "FullyQualifiedName~DatabaseReadinessTests|FullyQualifiedName~HealthEndpointTests|FullyQualifiedName~SystemEndpointTests"
```

Expected: migrated PostgreSQL readiness and liveness separation pass; all
existing system/cookie behavior remains green.

- [ ] **Step 9: Commit**

```bash
git add \
  apps/api/src/Template.Api \
  apps/api/src/Template.Infrastructure/Health \
  apps/api/tests/Template.Api.Tests
git commit -m "Compose persistent authentication infrastructure"
```

## Task 8: Add stable auth errors, local gating, CSRF, and rate limiting

**Files:**

- Create: `apps/api/src/Template.Api/Authentication/LocalAutomationAuthOptions.cs`
- Create: `apps/api/src/Template.Api/Authentication/LocalAutomationAuthAvailability.cs`
- Create: `apps/api/src/Template.Api/Authentication/LocalAutomationAvailabilityMiddleware.cs`
- Create: `apps/api/src/Template.Api/Authentication/AuthResponseCacheMiddleware.cs`
- Create: `apps/api/src/Template.Api/Authentication/AuthEndpointMetadata.cs`
- Create: `apps/api/src/Template.Api/Authentication/AntiforgeryEndpointFilter.cs`
- Create: `apps/api/src/Template.Api/Authentication/AuthSecurityServiceCollectionExtensions.cs`
- Create: `apps/api/src/Template.Api/Errors/ApiProblemException.cs`
- Create: `apps/api/src/Template.Api/Errors/ApiValidationException.cs`
- Modify: `apps/api/src/Template.Api/Errors/ApiProblemCodes.cs`
- Modify: `apps/api/src/Template.Api/Errors/ApiProblemDetailsDefaults.cs`
- Modify: `apps/api/src/Template.Api/Errors/ApiExceptionHandler.cs`
- Modify: `apps/api/src/Template.Api/Program.cs`
- Modify: `apps/api/tests/Template.Api.Tests/Infrastructure/TestEndpointModule.cs`
- Create: `apps/api/tests/Template.Api.Tests/AuthHttpBoundaryTests.cs`

**Interfaces:**

- Consumes: existing Problem Details writer/correlation contract and ASP.NET Core antiforgery/rate limiting.
- Produces: `ILocalAutomationAuthAvailability`, endpoint metadata helpers `RequireApiAntiforgery()`/`WithLocalOnly()`, stable auth Problem Details codes, and named policies `LocalAutomationCreate`/`LocalAutomationSignIn`.

- [ ] **Step 1: Add failing HTTP-boundary tests**

Extend `TestEndpointModule.MapEndpoints` with test-only routes that are excluded
from OpenAPI:

```csharp
context.Root.MapGet(
        "/api/testing/csrf",
        (IAntiforgery antiforgery, HttpContext httpContext) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(httpContext);
            return Results.Ok(new { requestToken = tokens.RequestToken });
        })
    .AllowAnonymous()
    .ExcludeFromDescription();

context.Root.MapPost(
        "/api/testing/csrf",
        () => Results.Ok(new { accepted = true }))
    .AllowAnonymous()
    .RequireApiAntiforgery()
    .ExcludeFromDescription();

context.Root.MapGet(
        "/api/local-auth/testing",
        () => Results.Ok(new { enabled = true }))
    .AllowAnonymous()
    .WithLocalOnly()
    .ExcludeFromDescription();

context.Root.MapPost(
        "/api/local-auth/testing-rate",
        () => Results.Ok(new { accepted = true }))
    .AllowAnonymous()
    .RequireApiAntiforgery()
    .RequireRateLimiting(AuthRateLimitPolicies.LocalAutomationCreate)
    .WithLocalOnly()
    .ExcludeFromDescription();
```

```csharp
// apps/api/tests/Template.Api.Tests/AuthHttpBoundaryTests.cs
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Template.Api.Tests.Infrastructure;

namespace Template.Api.Tests;

public sealed class AuthHttpBoundaryTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task MissingAntiforgeryTokenUsesStableProblem()
    {
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsync(
            "/api/local-auth/testing-rate",
            content: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        var problem = await response.Content.ReadFromJsonAsync<ApiProblem>(
            TestContext.Current.CancellationToken);
        Assert.Equal("antiforgery_failed", problem!.Code);
    }

    [Fact]
    public async Task ValidHeaderAndHttpOnlySecureAntiforgeryCookieAreAccepted()
    {
        using var client = factory.CreateApiClient();
        using var csrf = await client.GetAsync(
            "/api/testing/csrf",
            TestContext.Current.CancellationToken);
        var token = await csrf.Content.ReadFromJsonAsync<CsrfToken>(
            TestContext.Current.CancellationToken);
        var setCookie = csrf.Headers.GetValues("Set-Cookie").Single();
        Assert.Contains("__Host-template.antiforgery=", setCookie);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", setCookie, StringComparison.OrdinalIgnoreCase);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/testing/csrf");
        request.Headers.Add("X-CSRF-TOKEN", token!.RequestToken);
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ProductionLocalRouteAlwaysReturnsLocalDisabledProblem()
    {
        await using var production = factory.WithWebHostBuilder(
            builder => builder.UseEnvironment("Production"));
        using var client = production.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync(
            "/api/local-auth/testing",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ApiProblem>(
            TestContext.Current.CancellationToken);
        Assert.Equal("local_auth_disabled", problem!.Code);
    }

    [Fact]
    public async Task CreateLimiterReturnsTyped429AndRetryAfter()
    {
        await using var limited = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["LocalAutomationAuth:CreateRateLimitPerMinute"] = "1"
                })));
        using var client = limited.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        var csrf = await client.GetFromJsonAsync<CsrfToken>(
            "/api/testing/csrf",
            TestContext.Current.CancellationToken);

        using var first = await SendProtectedPost(client, csrf!.RequestToken);
        using var second = await SendProtectedPost(client, csrf.RequestToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.True(second.Headers.RetryAfter is not null);
        var problem = await second.Content.ReadFromJsonAsync<ApiProblem>(
            TestContext.Current.CancellationToken);
        Assert.Equal("rate_limited", problem!.Code);
    }

    private static Task<HttpResponseMessage> SendProtectedPost(
        HttpClient client,
        string token)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/local-auth/testing-rate");
        request.Headers.Add("X-CSRF-TOKEN", token);
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private sealed record CsrfToken(string RequestToken);
    private sealed record ApiProblem(string Code);
}
```

- [ ] **Step 2: Run the boundary tests and verify they fail**

Run:

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --filter FullyQualifiedName~AuthHttpBoundaryTests
```

Expected: build fails because the metadata extensions, policy constants, and
security services do not exist.

- [ ] **Step 3: Add stable error types and definitions**

```csharp
// apps/api/src/Template.Api/Errors/ApiProblemException.cs
namespace Template.Api.Errors;

internal sealed class ApiProblemException(int statusCode, string code) : Exception
{
    internal int StatusCode { get; } = statusCode;
    internal string Code { get; } = code;
}
```

```csharp
// apps/api/src/Template.Api/Errors/ApiValidationException.cs
namespace Template.Api.Errors;

internal sealed class ApiValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
```

Add constants to `ApiProblemCodes`:

```csharp
internal const string AntiforgeryFailed = "antiforgery_failed";
internal const string LocalAuthInvalidCredentials = "local_auth_invalid_credentials";
internal const string LocalAuthUserRequired = "local_auth_user_required";
internal const string LocalAuthDisabled = "local_auth_disabled";
internal const string LocalAuthUserExists = "local_auth_user_exists";
internal const string RateLimited = "rate_limited";
```

Change `ApiExceptionHandler.TryHandleAsync` to select exact status/code and
validation shape:

```csharp
var (status, code, validationErrors) = exception switch
{
    ApiValidationException validation => (
        StatusCodes.Status400BadRequest,
        ApiProblemCodes.ValidationFailed,
        validation.Errors),
    ApiProblemException problem => (
        problem.StatusCode,
        problem.Code,
        null),
    AntiforgeryValidationException => (
        StatusCodes.Status400BadRequest,
        ApiProblemCodes.AntiforgeryFailed,
        null),
    BadHttpRequestException badRequest => (
        badRequest.StatusCode,
        ApiProblemCodes.InvalidRequest,
        null),
    _ => (
        StatusCodes.Status500InternalServerError,
        ApiProblemCodes.InternalError,
        null)
};

if (status >= StatusCodes.Status500InternalServerError)
{
    logger.LogError(exception, "Unhandled API exception");
}
else
{
    logger.LogWarning("API request rejected with {Code}", code);
}

httpContext.Response.StatusCode = status;
var details = validationErrors is null
    ? new ProblemDetails { Status = status }
    : new HttpValidationProblemDetails(
        validationErrors.ToDictionary(pair => pair.Key, pair => pair.Value))
    {
        Status = status
    };
details.Extensions["code"] = code;
return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
{
    HttpContext = httpContext,
    ProblemDetails = details
});
```

Add:

```csharp
using Microsoft.AspNetCore.Antiforgery;
```

Change `ApiProblemDetailsDefaults.Customize` so a preselected stable code wins:

```csharp
var requestedCode = problem.Extensions.TryGetValue("code", out var rawCode)
    ? rawCode as string
    : null;
var definition = Resolve(status, isValidation, requestedCode);
```

Replace `Resolve` with:

```csharp
private static ProblemDefinition Resolve(
    int status,
    bool isValidation,
    string? requestedCode)
{
    var custom = requestedCode switch
    {
        ApiProblemCodes.AntiforgeryFailed => new ProblemDefinition(
            requestedCode,
            "Antiforgery validation failed",
            "The request antiforgery token is missing or invalid."),
        ApiProblemCodes.LocalAuthInvalidCredentials => new ProblemDefinition(
            requestedCode,
            "Authentication failed",
            "The supplied local credentials are invalid."),
        ApiProblemCodes.LocalAuthUserRequired => new ProblemDefinition(
            requestedCode,
            "Local automation user required",
            "This operation requires a local automation user."),
        ApiProblemCodes.LocalAuthDisabled => new ProblemDefinition(
            requestedCode,
            "Local authentication unavailable",
            "Local automation authentication is not available."),
        ApiProblemCodes.LocalAuthUserExists => new ProblemDefinition(
            requestedCode,
            "Local automation user already exists",
            "The requested local automation identity cannot be created."),
        ApiProblemCodes.RateLimited => new ProblemDefinition(
            requestedCode,
            "Too many requests",
            "The authentication request rate limit was exceeded."),
        _ => null
    };
    if (custom is not null)
    {
        return custom;
    }

    return (status, isValidation) switch
    {
        (StatusCodes.Status400BadRequest, true) => new(
            ApiProblemCodes.ValidationFailed,
            "Request validation failed",
            "One or more validation errors occurred."),
        (StatusCodes.Status400BadRequest, false) => new(
            ApiProblemCodes.InvalidRequest,
            "Invalid request",
            "The request could not be processed."),
        (StatusCodes.Status401Unauthorized, _) => new(
            ApiProblemCodes.Unauthorized,
            "Authentication required",
            "Authentication is required to access this resource."),
        (StatusCodes.Status403Forbidden, _) => new(
            ApiProblemCodes.Forbidden,
            "Access forbidden",
            "You do not have permission to access this resource."),
        (StatusCodes.Status404NotFound, _) => new(
            ApiProblemCodes.NotFound,
            "Resource not found",
            "The requested resource was not found."),
        (StatusCodes.Status405MethodNotAllowed, _) => new(
            ApiProblemCodes.MethodNotAllowed,
            "Method not allowed",
            "The HTTP method is not supported for this resource."),
        _ when status >= StatusCodes.Status500InternalServerError => new(
            ApiProblemCodes.InternalError,
            "Internal server error",
            "An unexpected error occurred."),
        _ => new(
            ApiProblemCodes.InvalidRequest,
            "Invalid request",
            "The request could not be processed.")
    };
}
```

- [ ] **Step 4: Implement the two-part local feature gate**

```csharp
// apps/api/src/Template.Api/Authentication/LocalAutomationAuthOptions.cs
namespace Template.Api.Authentication;

internal sealed class LocalAutomationAuthOptions
{
    internal const string SectionName = "LocalAutomationAuth";

    public bool Enabled { get; init; }
    public int CreateRateLimitPerMinute { get; init; } = 20;
    public int SignInRateLimitPerFiveMinutes { get; init; } = 10;
}
```

```csharp
// apps/api/src/Template.Api/Authentication/LocalAutomationAuthAvailability.cs
using Microsoft.Extensions.Options;

namespace Template.Api.Authentication;

internal interface ILocalAutomationAuthAvailability
{
    bool IsEnabled { get; }
}

internal sealed class LocalAutomationAuthAvailability(
    IWebHostEnvironment environment,
    IOptions<LocalAutomationAuthOptions> options)
    : ILocalAutomationAuthAvailability
{
    public bool IsEnabled =>
        (environment.IsDevelopment() || environment.IsEnvironment("Test")) &&
        options.Value.Enabled;
}
```

```csharp
// apps/api/src/Template.Api/Authentication/LocalAutomationAvailabilityMiddleware.cs
using Template.Api.Errors;

namespace Template.Api.Authentication;

internal sealed class LocalAutomationAvailabilityMiddleware(
    RequestDelegate next)
{
    public Task InvokeAsync(
        HttpContext context,
        ILocalAutomationAuthAvailability availability)
    {
        if (context.Request.Path.StartsWithSegments("/api/local-auth") &&
            !availability.IsEnabled)
        {
            throw new ApiProblemException(
                StatusCodes.Status404NotFound,
                ApiProblemCodes.LocalAuthDisabled);
        }

        return next(context);
    }
}
```

- [ ] **Step 5: Disable caching for every auth success and failure**

```csharp
// apps/api/src/Template.Api/Authentication/AuthResponseCacheMiddleware.cs
namespace Template.Api.Authentication;

internal sealed class AuthResponseCacheMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api/local-auth") ||
            context.Request.Path.StartsWithSegments("/api/v1/auth"))
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.CacheControl = "no-store";
                return Task.CompletedTask;
            });
        }

        return next(context);
    }
}
```

- [ ] **Step 6: Add endpoint metadata and explicit antiforgery validation**

```csharp
// apps/api/src/Template.Api/Authentication/AuthEndpointMetadata.cs
namespace Template.Api.Authentication;

internal sealed class AntiforgeryProtectedEndpointMetadata;
internal sealed class LocalOnlyEndpointMetadata;

internal static class AuthEndpointConventionExtensions
{
    internal static RouteHandlerBuilder RequireApiAntiforgery(
        this RouteHandlerBuilder builder) =>
        builder
            .WithMetadata(new AntiforgeryProtectedEndpointMetadata())
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

    internal static RouteHandlerBuilder WithLocalOnly(
        this RouteHandlerBuilder builder) =>
        builder
            .WithMetadata(new LocalOnlyEndpointMetadata())
            .WithTags("local-only");
}
```

```csharp
// apps/api/src/Template.Api/Authentication/AntiforgeryEndpointFilter.cs
using Microsoft.AspNetCore.Antiforgery;

namespace Template.Api.Authentication;

internal sealed class AntiforgeryEndpointFilter(IAntiforgery antiforgery)
    : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        await antiforgery.ValidateRequestAsync(context.HttpContext);
        return await next(context);
    }
}
```

- [ ] **Step 7: Register antiforgery and named fixed-window limiters**

```csharp
// apps/api/src/Template.Api/Authentication/AuthSecurityServiceCollectionExtensions.cs
using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Template.Api.Errors;

namespace Template.Api.Authentication;

internal static class AuthRateLimitPolicies
{
    internal const string LocalAutomationCreate = "LocalAutomationCreate";
    internal const string LocalAutomationSignIn = "LocalAutomationSignIn";
}

internal static class AuthSecurityServiceCollectionExtensions
{
    internal static IServiceCollection AddApiAuthSecurity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<LocalAutomationAuthOptions>()
            .Bind(configuration.GetSection(LocalAutomationAuthOptions.SectionName))
            .Validate(
                options =>
                    options.CreateRateLimitPerMinute > 0 &&
                    options.SignInRateLimitPerFiveMinutes > 0,
                "Local automation rate limits must be positive.")
            .ValidateOnStart();
        services.AddSingleton<
            ILocalAutomationAuthAvailability,
            LocalAutomationAuthAvailability>();
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = "__Host-template.antiforgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.Path = "/";
            options.Cookie.Domain = null;
        });

        var local = configuration
            .GetSection(LocalAutomationAuthOptions.SectionName)
            .Get<LocalAutomationAuthOptions>() ?? new LocalAutomationAuthOptions();
        services.AddRateLimiter(options =>
        {
            options.AddPolicy(
                AuthRateLimitPolicies.LocalAutomationCreate,
                context => Partition(
                    context,
                    local.CreateRateLimitPerMinute,
                    TimeSpan.FromMinutes(1)));
            options.AddPolicy(
                AuthRateLimitPolicies.LocalAutomationSignIn,
                context => Partition(
                    context,
                    local.SignInRateLimitPerFiveMinutes,
                    TimeSpan.FromMinutes(5)));
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (rejected, cancellationToken) =>
            {
                if (rejected.Lease.TryGetMetadata(
                        MetadataName.RetryAfter,
                        out var retryAfter))
                {
                    rejected.HttpContext.Response.Headers.RetryAfter =
                        Math.Ceiling(retryAfter.TotalSeconds)
                            .ToString(CultureInfo.InvariantCulture);
                }

                rejected.HttpContext.Response.StatusCode =
                    StatusCodes.Status429TooManyRequests;
                var details = new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests
                };
                details.Extensions["code"] = ApiProblemCodes.RateLimited;
                await rejected.HttpContext.RequestServices
                    .GetRequiredService<IProblemDetailsService>()
                    .TryWriteAsync(new ProblemDetailsContext
                    {
                        HttpContext = rejected.HttpContext,
                        ProblemDetails = details
                    });
            };
        });
        return services;
    }

    private static RateLimitPartition<string> Partition(
        HttpContext context,
        int permitLimit,
        TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = permitLimit,
                QueueLimit = 0,
                Window = window
            });
}
```

- [ ] **Step 8: Wire middleware in the correct order**

Add before endpoint registration:

```csharp
builder.Services.AddApiAuthSecurity(builder.Configuration);
```

Inside the `/api` `UseWhen` branch, after request logging:

```csharp
api.UseMiddleware<AuthResponseCacheMiddleware>();
api.UseMiddleware<LocalAutomationAvailabilityMiddleware>();
```

After authentication/authorization:

```csharp
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
```

Do not call `UseCors`; this slice is same-origin only.

- [ ] **Step 9: Run focused and foundation error tests**

Run:

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --filter "FullyQualifiedName~AuthHttpBoundaryTests|FullyQualifiedName~ProblemDetailsTests|FullyQualifiedName~ObservabilityTests"
```

Expected: CSRF, Production gate, typed `429`, existing Problem Details
invariants, correlation IDs, and safe logging all pass.

- [ ] **Step 10: Commit**

```bash
git add \
  apps/api/src/Template.Api/Authentication \
  apps/api/src/Template.Api/Errors \
  apps/api/src/Template.Api/Program.cs \
  apps/api/tests/Template.Api.Tests
git commit -m "Add authentication HTTP security boundaries"
```

## Task 9: Deliver the complete REST authentication contract

**Files:**

- Create: `apps/api/src/Template.Api/Features/Auth/AuthContracts.cs`
- Create: `apps/api/src/Template.Api/Features/Auth/ApiJsonRequestReader.cs`
- Create: `apps/api/src/Template.Api/Features/Auth/AuthSecurityEvents.cs`
- Create: `apps/api/src/Template.Api/Features/Auth/AuthEndpointModule.cs`
- Modify: `apps/api/src/Template.Api/Endpoints/EndpointModuleExtensions.cs`
- Modify: `apps/api/src/Template.Api/OpenApi/OpenApiEndpointConventionExtensions.cs`
- Modify: `apps/api/tests/Template.Api.Tests/Infrastructure/ApiWebApplicationFactory.cs`
- Modify: `apps/api/tests/Template.Api.Tests/Infrastructure/TestEndpointModule.cs`
- Delete: `apps/api/tests/Template.Api.Tests/Infrastructure/TestAuthenticationHandler.cs`
- Create: `apps/api/tests/Template.Api.Tests/Infrastructure/LocalAuthTestClient.cs`
- Create: `apps/api/tests/Template.Api.Tests/AuthEndpointTests.cs`
- Modify: `apps/api/tests/Template.Api.Tests/SystemEndpointTests.cs`
- Modify: `apps/api/tests/Template.Api.Tests/ProblemDetailsTests.cs`

**Interfaces:**

- Consumes: Application auth services, feature/security metadata from Task 8, and the existing `{ data }`/Problem Details conventions.
- Produces: all seven approved endpoints and exact operation names: `GetAuthCapabilities`, `GetAuthSession`, `GetAuthCsrf`, `Logout`, `CreateLocalAutomationScenario`, `SignInLocalAutomation`, and `DeleteLocalAutomationScenario`; structured `AuthOperation`/`AuthOutcome` events contain only safe user/session IDs.

- [ ] **Step 1: Write failing lifecycle tests through real cookies**

Create `LocalAuthTestClient` so every mutation obtains a real antiforgery token
and every test shares the same exact wire contract:

```csharp
// apps/api/tests/Template.Api.Tests/Infrastructure/LocalAuthTestClient.cs
using System.Net.Http.Json;

namespace Template.Api.Tests.Infrastructure;

internal static class LocalAuthTestClient
{
    internal static async Task<string> GetCsrfAsync(HttpClient client)
    {
        var envelope = await client.GetFromJsonAsync<CsrfEnvelope>(
            "/api/v1/auth/csrf",
            TestContext.Current.CancellationToken);
        return envelope!.Data.RequestToken;
    }

    internal static async Task<HttpResponseMessage> CreateScenarioAsync(
        HttpClient client,
        object? body = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/local-auth/scenario");
        request.Headers.Add("X-CSRF-TOKEN", await GetCsrfAsync(client));
        request.Content = JsonContent.Create(body ?? new { });
        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    internal static async Task<HttpResponseMessage> SignInAsync(
        HttpClient client,
        string email,
        string password)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/local-auth/sign-in");
        request.Headers.Add("X-CSRF-TOKEN", await GetCsrfAsync(client));
        request.Content = JsonContent.Create(new { email, password });
        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    internal static async Task<HttpResponseMessage> LogoutAsync(HttpClient client)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/logout");
        request.Headers.Add("X-CSRF-TOKEN", await GetCsrfAsync(client));
        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    internal static async Task<HttpResponseMessage> CleanupAsync(HttpClient client)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Delete,
            "/api/local-auth/scenario");
        request.Headers.Add("X-CSRF-TOKEN", await GetCsrfAsync(client));
        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    internal sealed record CsrfEnvelope(CsrfData Data);
    internal sealed record CsrfData(string RequestToken);
    internal sealed record ScenarioEnvelope(ScenarioData Data);
    internal sealed record ScenarioData(
        UserData User,
        string Email,
        string Password,
        string CleanupUrl);
    internal sealed record UserData(
        Guid Id,
        string Name,
        string Email,
        bool EmailVerified,
        string? Image);
}
```

Add `AuthEndpointTests` with these exact cases:

```csharp
// apps/api/tests/Template.Api.Tests/AuthEndpointTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Template.Api.Authentication;
using Template.Api.Tests.Infrastructure;
using Template.Infrastructure.Persistence;

namespace Template.Api.Tests;

public sealed class AuthEndpointTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>, IAsyncLifetime
{
    public async ValueTask InitializeAsync() =>
        await factory.ResetAuthDataAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task CapabilitiesAndAnonymousSessionAreTypedAndNotCached()
    {
        using var client = factory.CreateApiClient();

        using var capabilities = await client.GetAsync(
            "/api/v1/auth/capabilities",
            TestContext.Current.CancellationToken);
        using var session = await client.GetAsync(
            "/api/v1/auth/session",
            TestContext.Current.CancellationToken);
        var capabilitiesBody = await capabilities.Content
            .ReadFromJsonAsync<CapabilitiesEnvelope>(
                TestContext.Current.CancellationToken);
        var sessionBody = await session.Content.ReadFromJsonAsync<SessionEnvelope>(
            TestContext.Current.CancellationToken);

        Assert.True(capabilitiesBody!.Data.LocalAutomationEnabled);
        Assert.Empty(capabilitiesBody.Data.Providers);
        Assert.False(sessionBody!.Data.Authenticated);
        Assert.Null(sessionBody.Data.User);
        Assert.Null(sessionBody.Data.Session);
        Assert.Contains("no-store", capabilities.Headers.CacheControl!.ToString());
        Assert.Contains("no-store", session.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task ScenarioCreatesExactlyOnePersistentUserAndSession()
    {
        using var client = factory.CreateApiClient();

        using var response = await LocalAuthTestClient.CreateScenarioAsync(client);
        var payload = await response.Content
            .ReadFromJsonAsync<LocalAuthTestClient.ScenarioEnvelope>(
                TestContext.Current.CancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Matches(
            "^local-agent\\+.+@local-agent\\.test$",
            payload!.Data.Email);
        Assert.StartsWith("local-", payload.Data.Password);
        Assert.Equal("/api/local-auth/scenario", payload.Data.CleanupUrl);
        Assert.Equal(1, await db.Users.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await db.Sessions.CountAsync(TestContext.Current.CancellationToken));
        var setCookie = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("__Host-template.session=", StringComparison.Ordinal));
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReloadAndSecondCredentialSignInHaveDistinctSessionIds()
    {
        using var first = factory.CreateApiClient();
        using var created = await LocalAuthTestClient.CreateScenarioAsync(first);
        var scenario = await created.Content
            .ReadFromJsonAsync<LocalAuthTestClient.ScenarioEnvelope>(
                TestContext.Current.CancellationToken);
        var firstSession = await first.GetFromJsonAsync<SessionEnvelope>(
            "/api/v1/auth/session",
            TestContext.Current.CancellationToken);
        var reloaded = await first.GetFromJsonAsync<SessionEnvelope>(
            "/api/v1/auth/session",
            TestContext.Current.CancellationToken);
        using var second = factory.CreateApiClient();
        using var signedIn = await LocalAuthTestClient.SignInAsync(
            second,
            scenario!.Data.Email,
            scenario.Data.Password);
        var secondSession = await second.GetFromJsonAsync<SessionEnvelope>(
            "/api/v1/auth/session",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        Assert.True(firstSession!.Data.Authenticated);
        Assert.Equal(firstSession.Data.Session!.Id, reloaded!.Data.Session!.Id);
        Assert.NotEqual(firstSession.Data.Session.Id, secondSession!.Data.Session!.Id);
    }

    [Fact]
    public async Task LogoutDeletesOnlyCurrentSession()
    {
        using var first = factory.CreateApiClient();
        using var created = await LocalAuthTestClient.CreateScenarioAsync(first);
        var scenario = await created.Content
            .ReadFromJsonAsync<LocalAuthTestClient.ScenarioEnvelope>(
                TestContext.Current.CancellationToken);
        using var second = factory.CreateApiClient();
        using var signedIn = await LocalAuthTestClient.SignInAsync(
            second,
            scenario!.Data.Email,
            scenario.Data.Password);

        using var logout = await LocalAuthTestClient.LogoutAsync(first);
        var firstState = await first.GetFromJsonAsync<SessionEnvelope>(
            "/api/v1/auth/session",
            TestContext.Current.CancellationToken);
        var secondState = await second.GetFromJsonAsync<SessionEnvelope>(
            "/api/v1/auth/session",
            TestContext.Current.CancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var sessionCount = await scope.ServiceProvider
            .GetRequiredService<AuthDbContext>()
            .Sessions.CountAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        Assert.False(firstState!.Data.Authenticated);
        Assert.True(secondState!.Data.Authenticated);
        Assert.Equal(1, sessionCount);
        var expiredCookie = logout.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith(
                "__Host-template.session=",
                StringComparison.Ordinal));
        Assert.Contains(
            "expires=Thu, 01 Jan 1970",
            expiredCookie,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CleanupDeletesUserAndEverySession()
    {
        using var first = factory.CreateApiClient();
        using var created = await LocalAuthTestClient.CreateScenarioAsync(first);
        var scenario = await created.Content
            .ReadFromJsonAsync<LocalAuthTestClient.ScenarioEnvelope>(
                TestContext.Current.CancellationToken);
        using var second = factory.CreateApiClient();
        using var signedIn = await LocalAuthTestClient.SignInAsync(
            second,
            scenario!.Data.Email,
            scenario.Data.Password);

        using var cleanup = await LocalAuthTestClient.CleanupAsync(second);
        var firstState = await first.GetFromJsonAsync<SessionEnvelope>(
            "/api/v1/auth/session",
            TestContext.Current.CancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        Assert.Equal(HttpStatusCode.OK, cleanup.StatusCode);
        Assert.False(firstState!.Data.Authenticated);
        Assert.False(await db.Users.AnyAsync(TestContext.Current.CancellationToken));
        Assert.False(await db.Sessions.AnyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UnknownMemberIsInvalidAndExplicitDuplicateIsConflict()
    {
        using var client = factory.CreateApiClient();
        using var unknown = await LocalAuthTestClient.CreateScenarioAsync(
            client,
            new { unsupported = true });
        var explicitBody = new
        {
            name = "Explicit User",
            email = "local-agent+explicit@local-agent.test",
            password = "local-explicit-password"
        };
        using var first = await LocalAuthTestClient.CreateScenarioAsync(
            client,
            explicitBody);
        using var duplicateClient = factory.CreateApiClient();
        using var duplicate = await LocalAuthTestClient.CreateScenarioAsync(
            duplicateClient,
            explicitBody);

        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(
            "invalid_request",
            (await unknown.Content.ReadFromJsonAsync<ApiProblem>(
                TestContext.Current.CancellationToken))!.Code);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(
            "local_auth_user_exists",
            (await duplicate.Content.ReadFromJsonAsync<ApiProblem>(
                TestContext.Current.CancellationToken))!.Code);
    }

    [Fact]
    public async Task ExplicitNameAndEmailAreTrimmedAndEmailIsNormalized()
    {
        using var client = factory.CreateApiClient();

        using var response = await LocalAuthTestClient.CreateScenarioAsync(
            client,
            new
            {
                name = "  Trimmed User  ",
                email = "  LOCAL-AGENT+TRIMMED@LOCAL-AGENT.TEST  ",
                password = "local-trimmed-password"
            });
        var scenario = await response.Content
            .ReadFromJsonAsync<LocalAuthTestClient.ScenarioEnvelope>(
                TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Trimmed User", scenario!.Data.User.Name);
        Assert.Equal(
            "local-agent+trimmed@local-agent.test",
            scenario.Data.Email);
    }

    [Fact]
    public async Task InvalidCredentialFailureDoesNotRevealUserExistence()
    {
        using var client = factory.CreateApiClient();
        using var missing = await LocalAuthTestClient.SignInAsync(
            client,
            "local-agent+missing@local-agent.test",
            "local-invalid-password");

        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        var problem = await missing.Content.ReadFromJsonAsync<ApiProblem>(
            TestContext.Current.CancellationToken);
        Assert.Equal("local_auth_invalid_credentials", problem!.Code);
        Assert.DoesNotContain("missing", problem.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DisabledFlagHidesLocalRoutesAndCapabilities()
    {
        await using var disabled = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["LocalAutomationAuth:Enabled"] = "false"
                    })));
        using var client = disabled.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false,
                HandleCookies = true
            });

        var capabilities = await client.GetFromJsonAsync<CapabilitiesEnvelope>(
            "/api/v1/auth/capabilities",
            TestContext.Current.CancellationToken);
        using var hidden = await client.PostAsync(
            "/api/local-auth/scenario",
            JsonContent.Create(new { }),
            TestContext.Current.CancellationToken);

        Assert.False(capabilities!.Data.LocalAutomationEnabled);
        Assert.Empty(capabilities.Data.Providers);
        await AssertProblemAsync(
            hidden,
            HttpStatusCode.NotFound,
            "local_auth_disabled");
    }

    [Fact]
    public async Task EveryUnsafeAuthEndpointRejectsMissingAntiforgery()
    {
        using var client = factory.CreateApiClient();

        using var scenarioWithoutToken = await client.PostAsync(
            "/api/local-auth/scenario",
            JsonContent.Create(new { }),
            TestContext.Current.CancellationToken);
        await AssertProblemAsync(
            scenarioWithoutToken,
            HttpStatusCode.BadRequest,
            "antiforgery_failed");

        using var invalidTokenRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/local-auth/scenario");
        invalidTokenRequest.Headers.Add("X-CSRF-TOKEN", "invalid");
        invalidTokenRequest.Content = JsonContent.Create(new { });
        using var scenarioWithInvalidToken = await client.SendAsync(
            invalidTokenRequest,
            TestContext.Current.CancellationToken);
        await AssertProblemAsync(
            scenarioWithInvalidToken,
            HttpStatusCode.BadRequest,
            "antiforgery_failed");

        using var created = await LocalAuthTestClient.CreateScenarioAsync(client);
        var scenario = await created.Content
            .ReadFromJsonAsync<LocalAuthTestClient.ScenarioEnvelope>(
                TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var signInWithoutToken = await client.PostAsync(
            "/api/local-auth/sign-in",
            JsonContent.Create(new
            {
                scenario!.Data.Email,
                scenario.Data.Password
            }),
            TestContext.Current.CancellationToken);
        await AssertProblemAsync(
            signInWithoutToken,
            HttpStatusCode.BadRequest,
            "antiforgery_failed");

        using var logoutWithoutToken = await client.PostAsync(
            "/api/v1/auth/logout",
            content: null,
            TestContext.Current.CancellationToken);
        await AssertProblemAsync(
            logoutWithoutToken,
            HttpStatusCode.BadRequest,
            "antiforgery_failed");

        using var cleanupWithoutToken = await client.DeleteAsync(
            "/api/local-auth/scenario",
            TestContext.Current.CancellationToken);
        await AssertProblemAsync(
            cleanupWithoutToken,
            HttpStatusCode.BadRequest,
            "antiforgery_failed");
    }

    [Theory]
    [InlineData("POST", "/api/v1/auth/logout")]
    [InlineData("DELETE", "/api/local-auth/scenario")]
    public async Task AnonymousProtectedMutationUsesUnauthorized(
        string method,
        string path)
    {
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add(
            "X-CSRF-TOKEN",
            await LocalAuthTestClient.GetCsrfAsync(client));

        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        await AssertProblemAsync(
            response,
            HttpStatusCode.Unauthorized,
            "unauthorized");
    }

    [Fact]
    public async Task MalformedJsonAndInvalidFieldsUseDistinctStableProblems()
    {
        using var client = factory.CreateApiClient();
        var csrf = await LocalAuthTestClient.GetCsrfAsync(client);
        using var malformedRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/local-auth/scenario");
        malformedRequest.Headers.Add("X-CSRF-TOKEN", csrf);
        malformedRequest.Content = new StringContent(
            "{",
            Encoding.UTF8,
            "application/json");
        using var malformed = await client.SendAsync(
            malformedRequest,
            TestContext.Current.CancellationToken);

        using var shortName = await LocalAuthTestClient.CreateScenarioAsync(
            client,
            new { name = "x" });
        using var shortPassword = await LocalAuthTestClient.SignInAsync(
            client,
            "local-agent+validation@local-agent.test",
            "short");

        await AssertProblemAsync(
            malformed,
            HttpStatusCode.BadRequest,
            "invalid_request");
        await AssertProblemAsync(
            shortName,
            HttpStatusCode.BadRequest,
            "validation_failed");
        await AssertProblemAsync(
            shortPassword,
            HttpStatusCode.BadRequest,
            "validation_failed");
    }

    [Fact]
    public async Task SignInLimiterReturnsTyped429AfterConfiguredPermit()
    {
        await using var limited = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["LocalAutomationAuth:SignInRateLimitPerFiveMinutes"] = "1"
                    })));
        using var client = limited.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false,
                HandleCookies = true
            });

        using var first = await LocalAuthTestClient.SignInAsync(
            client,
            "local-agent+missing@local-agent.test",
            "local-invalid-password");
        using var second = await LocalAuthTestClient.SignInAsync(
            client,
            "local-agent+missing@local-agent.test",
            "local-invalid-password");

        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.True(second.Headers.RetryAfter is not null);
        await AssertProblemAsync(
            second,
            HttpStatusCode.TooManyRequests,
            "rate_limited");
    }

    [Fact]
    public async Task CleanupRejectsNonLocalSession()
    {
        using var client = factory.CreateApiClient();
        using var signIn = await client.PostAsync(
            "/api/testing/non-local-session",
            content: null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);

        using var cleanup = await LocalAuthTestClient.CleanupAsync(client);

        await AssertProblemAsync(
            cleanup,
            HttpStatusCode.Forbidden,
            "local_auth_user_required");
    }

    [Fact]
    public async Task TicketStoreFailureRollsBackScenarioUser()
    {
        await using var failing = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<
                    IPostConfigureOptions<CookieAuthenticationOptions>>(
                    new FailingTicketStorePostConfigure())));
        using var client = failing.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false,
                HandleCookies = true
            });

        using var response = await LocalAuthTestClient.CreateScenarioAsync(client);
        await AssertProblemAsync(
            response,
            HttpStatusCode.InternalServerError,
            "internal_error");
        Assert.False(
            response.Headers.TryGetValues("Set-Cookie", out var cookies) &&
            cookies.Any(value => value.StartsWith(
                "__Host-template.session=",
                StringComparison.Ordinal)));
        await using var scope = failing.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        Assert.False(await db.Users.AnyAsync(TestContext.Current.CancellationToken));
        Assert.False(await db.Sessions.AnyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SecurityEventIsStructuredAndExcludesCredentialsAndCookie()
    {
        var logs = factory.Services.GetRequiredService<CapturedLogProvider>();
        logs.Clear();
        using var client = factory.CreateApiClient();
        var email = $"local-agent+log-{Guid.NewGuid():N}@local-agent.test";
        const string password = "local-log-password";

        using var created = await LocalAuthTestClient.CreateScenarioAsync(
            client,
            new
            {
                name = "Log Test",
                email,
                password
            });
        var scenario = await created.Content
            .ReadFromJsonAsync<LocalAuthTestClient.ScenarioEnvelope>(
                TestContext.Current.CancellationToken);
        var session = await client.GetFromJsonAsync<SessionEnvelope>(
            "/api/v1/auth/session",
            TestContext.Current.CancellationToken);
        var securityEvent = Assert.Single(
            logs.Logs,
            log =>
                Equals(log.State.GetValueOrDefault("AuthOperation"), "scenario_create") &&
                Equals(log.State.GetValueOrDefault("AuthOutcome"), "succeeded"));
        var cookie = created.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith(
                "__Host-template.session=",
                StringComparison.Ordinal))
            .Split(';', 2)[0]
            .Split('=', 2)[1];
        var renderedLogs = string.Join(
            "\n",
            logs.Logs.Select(log =>
                $"{log.Message} {string.Join(" ", log.State.Values)}"));

        Assert.Equal(scenario!.Data.User.Id, securityEvent.State["UserId"]);
        Assert.Equal(session!.Data.Session!.Id, securityEvent.State["SessionId"]);
        Assert.DoesNotContain(email, renderedLogs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(password, renderedLogs, StringComparison.Ordinal);
        Assert.DoesNotContain(cookie, renderedLogs, StringComparison.Ordinal);
    }

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        var problem = await response.Content.ReadFromJsonAsync<ApiProblem>(
            TestContext.Current.CancellationToken);
        Assert.Equal(expectedCode, problem!.Code);
    }

    private sealed record CapabilitiesEnvelope(CapabilitiesData Data);
    private sealed record CapabilitiesData(
        bool LocalAutomationEnabled,
        ProviderData[] Providers);
    private sealed record ProviderData(string Id, string DisplayName);
    internal sealed record SessionEnvelope(SessionData Data);
    internal sealed record SessionData(
        bool Authenticated,
        LocalAuthTestClient.UserData? User,
        SessionMetadata? Session);
    internal sealed record SessionMetadata(
        Guid Id,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        DateTimeOffset ExpiresAt);
    private sealed record ApiProblem(string Code, string Detail);

    private sealed class FailingTicketStore : ITicketStore
    {
        public Task<string> StoreAsync(AuthenticationTicket ticket) =>
            throw new IOException("Injected ticket storage failure.");

        public Task<string> StoreAsync(
            AuthenticationTicket ticket,
            CancellationToken cancellationToken) =>
            throw new IOException("Injected ticket storage failure.");

        public Task<string> StoreAsync(
            AuthenticationTicket ticket,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
            throw new IOException("Injected ticket storage failure.");

        public Task RenewAsync(string key, AuthenticationTicket ticket) =>
            Task.CompletedTask;

        public Task RenewAsync(
            string key,
            AuthenticationTicket ticket,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task RenewAsync(
            string key,
            AuthenticationTicket ticket,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<AuthenticationTicket?> RetrieveAsync(string key) =>
            Task.FromResult<AuthenticationTicket?>(null);

        public Task<AuthenticationTicket?> RetrieveAsync(
            string key,
            CancellationToken cancellationToken) =>
            Task.FromResult<AuthenticationTicket?>(null);

        public Task<AuthenticationTicket?> RetrieveAsync(
            string key,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
            Task.FromResult<AuthenticationTicket?>(null);

        public Task RemoveAsync(string key) => Task.CompletedTask;

        public Task RemoveAsync(
            string key,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task RemoveAsync(
            string key,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FailingTicketStorePostConfigure
        : IPostConfigureOptions<CookieAuthenticationOptions>
    {
        public void PostConfigure(
            string? name,
            CookieAuthenticationOptions options)
        {
            if (name == ApiAuthenticationDefaults.SchemeName)
            {
                options.SessionStore = new FailingTicketStore();
            }
        }
    }
}
```

- [ ] **Step 2: Run the endpoint class and verify it fails**

Run:

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --filter FullyQualifiedName~AuthEndpointTests
```

Expected: the class is red because the seven auth operations do not exist yet;
the test-only non-local session route is also absent.

- [ ] **Step 3: Define strict request and response contracts**

```csharp
// apps/api/src/Template.Api/Features/Auth/AuthContracts.cs
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Template.Api.Features.Auth;

internal sealed record AuthProviderResponse(string Id, string DisplayName);

internal sealed record AuthCapabilitiesResponse(
    bool LocalAutomationEnabled,
    IReadOnlyList<AuthProviderResponse> Providers);

internal sealed record AuthUserResponse(
    Guid Id,
    string Name,
    string Email,
    bool EmailVerified,
    string? Image);

internal sealed record AuthSessionMetadataResponse(
    Guid Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset ExpiresAt);

internal sealed record AuthSessionResponse(
    bool Authenticated,
    AuthUserResponse? User,
    AuthSessionMetadataResponse? Session);

internal sealed record AuthCsrfResponse(string RequestToken);

internal sealed record LocalAutomationScenarioResponse(
    AuthUserResponse User,
    string Email,
    string Password,
    string CleanupUrl);

internal sealed record LocalAutomationCleanupResponse(int DeletedOrganizations);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record CreateLocalAutomationScenarioRequest
{
    public string? Name { get; init; }

    public string? Email { get; init; }

    [StringLength(128, MinimumLength = 12)]
    public string? Password { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record LocalAutomationSignInRequest
{
    public string? Email { get; init; }

    [Required]
    [StringLength(128, MinimumLength = 12)]
    public string? Password { get; init; }
}
```

- [ ] **Step 4: Implement strict manual JSON reading after the Production gate**

```csharp
// apps/api/src/Template.Api/Features/Auth/ApiJsonRequestReader.cs
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using Template.Api.Errors;

namespace Template.Api.Features.Auth;

internal sealed class ApiJsonRequestReader(IOptions<JsonOptions> jsonOptions)
{
    internal async Task<T> ReadAsync<T>(
        HttpContext context,
        Func<T>? emptyBodyFactory,
        CancellationToken cancellationToken)
        where T : class
    {
        using var reader = new StreamReader(
            context.Request.Body,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            leaveOpen: true);
        var json = await reader.ReadToEndAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            if (emptyBodyFactory is not null)
            {
                return emptyBodyFactory();
            }

            throw new ApiProblemException(
                StatusCodes.Status400BadRequest,
                ApiProblemCodes.InvalidRequest);
        }

        T value;
        try
        {
            value = JsonSerializer.Deserialize<T>(
                json,
                jsonOptions.Value.SerializerOptions) ??
                throw new JsonException("A JSON object is required.");
        }
        catch (JsonException)
        {
            throw new ApiProblemException(
                StatusCodes.Status400BadRequest,
                ApiProblemCodes.InvalidRequest);
        }

        var validationResults = new List<ValidationResult>();
        if (!Validator.TryValidateObject(
                value,
                new ValidationContext(value),
                validationResults,
                validateAllProperties: true))
        {
            var errors = validationResults
                .SelectMany(result =>
                    result.MemberNames.DefaultIfEmpty("body")
                        .Select(member => (member, result.ErrorMessage)))
                .GroupBy(value => value.member, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Select(value => value.ErrorMessage ?? "The value is invalid.")
                        .ToArray(),
                    StringComparer.Ordinal);
            throw new ApiValidationException(errors);
        }

        return value;
    }
}
```

- [ ] **Step 5: Map every REST endpoint and every stable failure**

```csharp
// apps/api/src/Template.Api/Features/Auth/AuthSecurityEvents.cs
using Microsoft.Extensions.Logging;

namespace Template.Api.Features.Auth;

internal static partial class AuthSecurityEvents
{
    [LoggerMessage(
        EventId = 3100,
        Level = LogLevel.Information,
        Message =
            "Authentication operation {AuthOperation} finished with {AuthOutcome}; UserId={UserId}; SessionId={SessionId}")]
    internal static partial void Write(
        ILogger logger,
        string authOperation,
        string authOutcome,
        Guid? userId,
        Guid? sessionId);
}
```

```csharp
// apps/api/src/Template.Api/Features/Auth/AuthEndpointModule.cs
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using Template.Api.Authentication;
using Template.Api.Contracts;
using Template.Api.Endpoints;
using Template.Api.Errors;
using Template.Api.OpenApi;
using Template.Application.Authentication;

namespace Template.Api.Features.Auth;

internal sealed class AuthEndpointModule : IEndpointModule
{
    private static readonly IReadOnlyList<AuthProviderResponse> NoProviders =
        Array.Empty<AuthProviderResponse>();

    public void MapEndpoints(EndpointRouteContext context)
    {
        context.VersionedApi.MapGet(
                "/auth/capabilities",
                (ILocalAutomationAuthAvailability availability, HttpContext http) =>
                {
                    NoStore(http);
                    return Results.Ok(new ApiResponse<AuthCapabilitiesResponse>(
                        new AuthCapabilitiesResponse(
                            availability.IsEnabled,
                            NoProviders)));
                })
            .AllowAnonymous()
            .WithName("GetAuthCapabilities")
            .Produces<ApiResponse<AuthCapabilitiesResponse>>()
            .ProducesPublicApiProblems();

        context.VersionedApi.MapGet(
                "/auth/session",
                async (
                    BrowserAuthenticationService auth,
                    HttpContext http,
                    CancellationToken cancellationToken) =>
                {
                    NoStore(http);
                    return Results.Ok(new ApiResponse<AuthSessionResponse>(
                        Map(await auth.GetSessionAsync(cancellationToken))));
                })
            .AllowAnonymous()
            .WithName("GetAuthSession")
            .Produces<ApiResponse<AuthSessionResponse>>()
            .ProducesPublicApiProblems();

        context.VersionedApi.MapGet(
                "/auth/csrf",
                (IAntiforgery antiforgery, HttpContext http) =>
                {
                    NoStore(http);
                    var tokens = antiforgery.GetAndStoreTokens(http);
                    return Results.Ok(new ApiResponse<AuthCsrfResponse>(
                        new AuthCsrfResponse(
                            tokens.RequestToken ??
                            throw new InvalidOperationException(
                                "Antiforgery did not issue a request token."))));
                })
            .AllowAnonymous()
            .WithName("GetAuthCsrf")
            .Produces<ApiResponse<AuthCsrfResponse>>()
            .ProducesPublicApiProblems();

        context.VersionedApi.MapPost(
                "/auth/logout",
                async (
                    BrowserAuthenticationService auth,
                    ILogger<AuthEndpointModule> logger,
                    HttpContext http,
                    CancellationToken cancellationToken) =>
                {
                    NoStore(http);
                    var userId = CurrentUserId(http.User);
                    var result = await auth.LogoutAsync(cancellationToken);
                    if (!result.Succeeded)
                    {
                        AuthSecurityEvents.Write(
                            logger,
                            "logout",
                            "unauthorized",
                            userId,
                            sessionId: null);
                        throw new ApiProblemException(
                            StatusCodes.Status401Unauthorized,
                            ApiProblemCodes.Unauthorized);
                    }

                    AuthSecurityEvents.Write(
                        logger,
                        "logout",
                        "succeeded",
                        userId,
                        sessionId: null);
                    return Results.Ok(new ApiResponse<AuthSessionResponse>(
                        Map(result.Value!)));
                })
            .WithName("Logout")
            .RequireApiAntiforgery()
            .Produces<ApiResponse<AuthSessionResponse>>()
            .ProducesProtectedApiProblems();

        context.Root.MapPost(
                "/api/local-auth/scenario",
                CreateScenarioAsync)
            .AllowAnonymous()
            .WithName("CreateLocalAutomationScenario")
            .Accepts<CreateLocalAutomationScenarioRequest>("application/json")
            .RequireApiAntiforgery()
            .RequireRateLimiting(AuthRateLimitPolicies.LocalAutomationCreate)
            .WithLocalOnly()
            .Produces<ApiResponse<LocalAutomationScenarioResponse>>(
                StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesLocalCreateProblems();

        context.Root.MapPost(
                "/api/local-auth/sign-in",
                SignInAsync)
            .AllowAnonymous()
            .WithName("SignInLocalAutomation")
            .Accepts<LocalAutomationSignInRequest>("application/json")
            .RequireApiAntiforgery()
            .RequireRateLimiting(AuthRateLimitPolicies.LocalAutomationSignIn)
            .WithLocalOnly()
            .Produces<ApiResponse<AuthSessionResponse>>()
            .ProducesValidationProblem()
            .ProducesLocalSignInProblems();

        context.Root.MapDelete(
                "/api/local-auth/scenario",
                CleanupAsync)
            .RequireAuthorization(ApiPolicies.BrowserSession)
            .WithName("DeleteLocalAutomationScenario")
            .RequireApiAntiforgery()
            .WithLocalOnly()
            .Produces<ApiResponse<LocalAutomationCleanupResponse>>()
            .ProducesProtectedApiProblems();
    }

    private static async Task<IResult> CreateScenarioAsync(
        ApiJsonRequestReader reader,
        LocalAutomationAuthService auth,
        ILogger<AuthEndpointModule> logger,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        NoStore(http);
        var request = await reader.ReadAsync(
            http,
            () => new CreateLocalAutomationScenarioRequest(),
            cancellationToken);
        ValidateTrimmed(request.Name, "name", 2, 50);
        var email = ValidateAndTrimEmail(request.Email, required: false);
        var result = await auth.CreateScenarioAsync(
            new CreateLocalScenarioInput(
                request.Name?.Trim(),
                email,
                request.Password),
            cancellationToken);
        if (!result.Succeeded)
        {
            AuthSecurityEvents.Write(
                logger,
                "scenario_create",
                result.Failure!.Value.ToString(),
                userId: null,
                sessionId: null);
            ThrowCreateFailure(result.Failure!.Value);
        }

        var value = result.Value!;
        AuthSecurityEvents.Write(
            logger,
            "scenario_create",
            "succeeded",
            value.User.Id.Value,
            value.Session.Id.Value);
        return Results.Json(
            new ApiResponse<LocalAutomationScenarioResponse>(
                new(
                    Map(value.User),
                    value.Credentials.Email,
                    value.Credentials.Password,
                    value.CleanupUrl)),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> SignInAsync(
        ApiJsonRequestReader reader,
        LocalAutomationAuthService auth,
        ILogger<AuthEndpointModule> logger,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        NoStore(http);
        var request = await reader.ReadAsync<LocalAutomationSignInRequest>(
            http,
            emptyBodyFactory: null,
            cancellationToken);
        var email = ValidateAndTrimEmail(request.Email, required: true)!;
        var result = await auth.SignInAsync(
            new LocalCredentialInput(
                email,
                request.Password!),
            cancellationToken);
        if (!result.Succeeded)
        {
            AuthSecurityEvents.Write(
                logger,
                "credential_sign_in",
                "invalid_credentials",
                userId: null,
                sessionId: null);
            throw new ApiProblemException(
                StatusCodes.Status401Unauthorized,
                ApiProblemCodes.LocalAuthInvalidCredentials);
        }

        var value = result.Value!;
        AuthSecurityEvents.Write(
            logger,
            "credential_sign_in",
            "succeeded",
            value.User.Id.Value,
            value.Session.Id.Value);
        return Results.Ok(new ApiResponse<AuthSessionResponse>(
            Map(SessionState.From(value))));
    }

    private static async Task<IResult> CleanupAsync(
        LocalAutomationAuthService auth,
        ILogger<AuthEndpointModule> logger,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        NoStore(http);
        var userId = CurrentUserId(http.User);
        var result = await auth.CleanupAsync(cancellationToken);
        if (!result.Succeeded)
        {
            AuthSecurityEvents.Write(
                logger,
                "scenario_cleanup",
                result.Failure!.Value.ToString(),
                userId,
                sessionId: null);
            throw result.Failure switch
            {
                AuthFailure.SessionRequired => new ApiProblemException(
                    StatusCodes.Status401Unauthorized,
                    ApiProblemCodes.Unauthorized),
                AuthFailure.LocalUserRequired => new ApiProblemException(
                    StatusCodes.Status403Forbidden,
                    ApiProblemCodes.LocalAuthUserRequired),
                _ => new InvalidOperationException(
                    "Unexpected cleanup failure.")
            };
        }

        AuthSecurityEvents.Write(
            logger,
            "scenario_cleanup",
            "succeeded",
            userId,
            sessionId: null);
        return Results.Ok(new ApiResponse<LocalAutomationCleanupResponse>(
            new(result.Value!.DeletedOrganizations)));
    }

    private static void ThrowCreateFailure(AuthFailure failure)
    {
        if (failure == AuthFailure.InvalidLocalEmail)
        {
            throw new ApiValidationException(
                new Dictionary<string, string[]>
                {
                    ["email"] =
                    [
                        "Email must use the local-agent+...@local-agent.test namespace."
                    ]
                });
        }

        if (failure == AuthFailure.UserExists)
        {
            throw new ApiProblemException(
                StatusCodes.Status409Conflict,
                ApiProblemCodes.LocalAuthUserExists);
        }

        throw new InvalidOperationException("Unexpected scenario creation failure.");
    }

    private static void ValidateTrimmed(
        string? value,
        string field,
        int minimum,
        int maximum)
    {
        if (value is null)
        {
            return;
        }

        var length = value.Trim().Length;
        if (length < minimum || length > maximum)
        {
            throw new ApiValidationException(
                new Dictionary<string, string[]>
                {
                    [field] =
                    [
                        $"The field {field} must be between {minimum} and {maximum} characters."
                    ]
                });
        }
    }

    private static string? ValidateAndTrimEmail(
        string? value,
        bool required)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (!required && value is null)
            {
                return null;
            }

            throw new ApiValidationException(
                new Dictionary<string, string[]>
                {
                    ["email"] = ["A valid email is required."]
                });
        }

        var trimmed = value.Trim();
        if (trimmed.Length > 254 ||
            !new EmailAddressAttribute().IsValid(trimmed))
        {
            throw new ApiValidationException(
                new Dictionary<string, string[]>
                {
                    ["email"] = ["A valid email of at most 254 characters is required."]
                });
        }

        return trimmed;
    }

    private static AuthSessionResponse Map(SessionState state) =>
        new(
            state.Authenticated,
            state.User is null ? null : Map(state.User),
            state.Session is null
                ? null
                : new AuthSessionMetadataResponse(
                    state.Session.Id.Value,
                    state.Session.CreatedAt,
                    state.Session.UpdatedAt,
                    state.Session.ExpiresAt));

    private static AuthUserResponse Map(AuthUser user) =>
        new(
            user.Id.Value,
            user.Name,
            user.Email,
            user.EmailVerified,
            user.Image);

    private static Guid? CurrentUserId(ClaimsPrincipal principal) =>
        Guid.TryParse(
            principal.FindFirstValue(ClaimTypes.NameIdentifier),
            out var parsedUserId)
            ? parsedUserId
            : null;

    private static void NoStore(HttpContext context) =>
        context.Response.Headers.CacheControl = "no-store";
}
```

Register `AuthEndpointModule` and `ApiJsonRequestReader`:

```csharp
services.AddSingleton<IEndpointModule, AuthEndpointModule>();
services.AddScoped<ApiJsonRequestReader>();
```

Add to `OpenApiEndpointConventionExtensions`:

```csharp
internal static RouteHandlerBuilder ProducesLocalCreateProblems(
    this RouteHandlerBuilder builder) =>
    builder
        .Produces<ProblemDetails>(
            StatusCodes.Status409Conflict,
            OpenApiDefaults.ProblemContentType)
        .Produces<ProblemDetails>(
            StatusCodes.Status429TooManyRequests,
            OpenApiDefaults.ProblemContentType)
        .ProducesPublicApiProblems();

internal static RouteHandlerBuilder ProducesLocalSignInProblems(
    this RouteHandlerBuilder builder) =>
    builder
        .Produces<ProblemDetails>(
            StatusCodes.Status401Unauthorized,
            OpenApiDefaults.ProblemContentType)
        .Produces<ProblemDetails>(
            StatusCodes.Status429TooManyRequests,
            OpenApiDefaults.ProblemContentType)
        .ProducesPublicApiProblems();
```

- [ ] **Step 6: Verify the basic endpoint tests and inspect the first real cookie**

Run:

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --filter "FullyQualifiedName~AuthEndpointTests.ScenarioCreatesExactlyOnePersistentUserAndSession|FullyQualifiedName~AuthEndpointTests.CapabilitiesAndAnonymousSessionAreTypedAndNotCached"
```

Expected at this intermediate point: scenario still fails to authenticate on
subsequent calls because the factory still overrides the default scheme; the
capability/anonymous assertions pass.

- [ ] **Step 7: Remove header authentication and adapt foundation tests to real sessions**

Delete `TestAuthenticationHandler.cs` and remove its `AddAuthentication`
override from `ApiWebApplicationFactory.ConfigureTestServices`. Keep only the
forbidden-claim policy, test endpoint module, and captured logging.

In `SystemEndpointTests.ProtectedProbeAcceptsTestAuthenticatedRequest`, replace
client/header setup with:

```csharp
using var client = factory.CreateApiClient();
using var scenario = await LocalAuthTestClient.CreateScenarioAsync(client);
Assert.Equal(HttpStatusCode.Created, scenario.StatusCode);
```

Keep the existing protected request/assertions and use that same `client`.
In `ProblemDetailsTests.AuthenticatedPrincipalWithoutRequiredClaimGetsForbiddenProblem`,
replace client/header setup before `/api/testing/forbidden` with:

```csharp
using var client = factory.CreateApiClient();
using var scenario = await LocalAuthTestClient.CreateScenarioAsync(client);
Assert.Equal(HttpStatusCode.Created, scenario.StatusCode);
```

In the authenticated half of
`SystemEndpointTests.VersionedConsumerRoutesAreProtectedByDefault`, use:

```csharp
using var authenticatedClient = factory.CreateApiClient();
using var scenario = await LocalAuthTestClient.CreateScenarioAsync(
    authenticatedClient);
Assert.Equal(HttpStatusCode.Created, scenario.StatusCode);
using var authenticatedResponse = await authenticatedClient.GetAsync(
    "/api/v1/testing/consumer",
    TestContext.Current.CancellationToken);
```

- [ ] **Step 8: Add non-local and storage-failure test adapters**

Add a test-only non-local sign-in endpoint to `TestEndpointModule`:

```csharp
context.Root.MapPost(
        "/api/testing/non-local-session",
        async (
            UserManager<ApplicationUser> users,
            IBrowserSessionGateway sessions,
            TimeProvider timeProvider,
            CancellationToken cancellationToken) =>
        {
            var now = timeProvider.GetUtcNow();
            var row = new ApplicationUser
            {
                Id = Guid.CreateVersion7(now),
                UserName = "person@example.test",
                Email = "person@example.test",
                DisplayName = "Non Local User",
                EmailConfirmed = true,
                IsLocalAutomation = false,
                CreatedAt = now,
                UpdatedAt = now
            };
            var created = await users.CreateAsync(row);
            if (!created.Succeeded)
            {
                throw new InvalidOperationException("Could not create test user.");
            }

            await sessions.SignInAsync(
                new AuthUser(
                    new UserId(row.Id),
                    row.DisplayName,
                    row.Email,
                    row.EmailConfirmed,
                    row.ImageUrl,
                    row.IsLocalAutomation),
                cancellationToken);
            return Results.Ok();
        })
    .AllowAnonymous()
    .ExcludeFromDescription();
```

Add these exact directives to `TestEndpointModule.cs`:

```csharp
using Microsoft.AspNetCore.Identity;
using Template.Application.Authentication;
using Template.Application.Authentication.Ports;
using Template.Domain.Authentication;
using Template.Infrastructure.Identity;
```

The failing `ITicketStore`, its named cookie post-configurer, and the child
factory override are already part of the red
`TicketStoreFailureRollsBackScenarioUser` test from Step 1. Keep them
test-private; do not add a production failure-injection seam.

- [ ] **Step 9: Run all auth/API tests**

Run:

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --filter "FullyQualifiedName~AuthEndpointTests|FullyQualifiedName~AuthHttpBoundaryTests|FullyQualifiedName~SystemEndpointTests|FullyQualifiedName~ProblemDetailsTests"
```

Expected: all real-cookie lifecycle, CSRF, validation, duplicate, generic
credential, logout isolation, cleanup cascade, rollback, safe structured
security logging, authorization, and existing foundation cases pass.

- [ ] **Step 10: Commit**

```bash
git add \
  apps/api/src/Template.Api/Features/Auth \
  apps/api/src/Template.Api/Endpoints/EndpointModuleExtensions.cs \
  apps/api/src/Template.Api/OpenApi/OpenApiEndpointConventionExtensions.cs \
  apps/api/tests/Template.Api.Tests
git commit -m "Add REST browser authentication endpoints"
```

## Task 10: Publish auth OpenAPI metadata and regenerate the SDK

**Files:**

- Modify: `apps/api/src/Template.Api/OpenApi/OpenApiServiceCollectionExtensions.cs`
- Modify: `apps/api/tests/Template.Api.Tests/OpenApiContractTests.cs`
- Regenerate: `contracts/openapi/v1.json`
- Regenerate: `apps/web/src/lib/api/generated/`
- Modify: `apps/web/test/contracts/generated-sdk.test.ts`
- Modify: `apps/web/scripts/check-boundaries.mjs`
- Modify: `apps/web/scripts/check-boundaries.node-test.mjs`

**Interfaces:**

- Consumes: `AntiforgeryProtectedEndpointMetadata`, `LocalOnlyEndpointMetadata`, endpoint operation names, and the existing `cookieAuth` scheme.
- Produces: required generated header input `X-CSRF-TOKEN`, visible local-only metadata, cookie security on only protected operations, and generated SDK functions/types for every auth route.

- [ ] **Step 1: Write failing OpenAPI assertions**

Add this test to `OpenApiContractTests`:

```csharp
[Fact]
public async Task AuthOperationsDeclareLocalCsrfAndCookieBoundaries()
{
    using var client = factory.CreateApiClient();
    var document = JsonNode.Parse(await client.GetStringAsync(
        "/api/openapi/v1.json",
        TestContext.Current.CancellationToken))!;
    var paths = document["paths"]!;

    Assert.Equal(
        "GetAuthCapabilities",
        paths["/api/v1/auth/capabilities"]!["get"]!["operationId"]!.GetValue<string>());
    Assert.Equal(
        "GetAuthSession",
        paths["/api/v1/auth/session"]!["get"]!["operationId"]!.GetValue<string>());
    Assert.Equal(
        "GetAuthCsrf",
        paths["/api/v1/auth/csrf"]!["get"]!["operationId"]!.GetValue<string>());
    Assert.Equal(
        "Logout",
        paths["/api/v1/auth/logout"]!["post"]!["operationId"]!.GetValue<string>());
    Assert.Equal(
        "CreateLocalAutomationScenario",
        paths["/api/local-auth/scenario"]!["post"]!["operationId"]!.GetValue<string>());
    Assert.Equal(
        "DeleteLocalAutomationScenario",
        paths["/api/local-auth/scenario"]!["delete"]!["operationId"]!.GetValue<string>());
    Assert.Equal(
        "SignInLocalAutomation",
        paths["/api/local-auth/sign-in"]!["post"]!["operationId"]!.GetValue<string>());

    foreach (var operation in new[]
             {
                 paths["/api/v1/auth/logout"]!["post"]!,
                 paths["/api/local-auth/scenario"]!["post"]!,
                 paths["/api/local-auth/scenario"]!["delete"]!,
                 paths["/api/local-auth/sign-in"]!["post"]!
             })
    {
        var header = Assert.Single(
            operation["parameters"]!.AsArray(),
            parameter => parameter!["name"]!.GetValue<string>() == "X-CSRF-TOKEN");
        Assert.Equal("header", header!["in"]!.GetValue<string>());
        Assert.True(header["required"]!.GetValue<bool>());
    }

    foreach (var operation in new[]
             {
                 paths["/api/local-auth/scenario"]!["post"]!,
                 paths["/api/local-auth/scenario"]!["delete"]!,
                 paths["/api/local-auth/sign-in"]!["post"]!
             })
    {
        Assert.True(operation["x-local-only"]!.GetValue<bool>());
        Assert.Contains(
            operation["tags"]!.AsArray(),
            tag => tag!.GetValue<string>() == "local-only");
    }

    Assert.Null(paths["/api/v1/auth/capabilities"]!["get"]!["security"]);
    Assert.Null(paths["/api/v1/auth/session"]!["get"]!["security"]);
    Assert.Null(paths["/api/v1/auth/csrf"]!["get"]!["security"]);
    Assert.Null(paths["/api/local-auth/scenario"]!["post"]!["security"]);
    Assert.Null(paths["/api/local-auth/sign-in"]!["post"]!["security"]);
    Assert.NotNull(paths["/api/v1/auth/logout"]!["post"]!["security"]);
    Assert.NotNull(paths["/api/local-auth/scenario"]!["delete"]!["security"]);

    var schemes = document["components"]!["securitySchemes"]!.AsObject();
    Assert.Equal(["cookieAuth"], schemes.Select(pair => pair.Key).ToArray());
}
```

Add these assertions to
`TestHostPublishesVersionedOpenApi31Contract`:

```csharp
Assert.NotNull(document["paths"]!["/api/v1/auth/capabilities"]);
Assert.NotNull(document["paths"]!["/api/v1/auth/session"]);
Assert.NotNull(document["paths"]!["/api/v1/auth/csrf"]);
Assert.NotNull(document["paths"]!["/api/v1/auth/logout"]);
Assert.NotNull(document["paths"]!["/api/local-auth/scenario"]);
Assert.NotNull(document["paths"]!["/api/local-auth/sign-in"]);
Assert.NotNull(
    document["paths"]!["/api/local-auth/scenario"]!["delete"]);
```

- [ ] **Step 2: Run the focused contract test and verify it fails**

Run:

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --filter FullyQualifiedName~AuthOperationsDeclareLocalCsrfAndCookieBoundaries
```

Expected: paths exist, but required CSRF header and `x-local-only` assertions fail.

- [ ] **Step 3: Transform auth endpoint metadata into OpenAPI**

Add these branches to the existing operation transformer in
`OpenApiServiceCollectionExtensions` before its cookie-security return path:

```csharp
if (metadata.OfType<AntiforgeryProtectedEndpointMetadata>().Any())
{
    operation.Parameters ??= [];
    operation.Parameters.Add(new OpenApiParameter
    {
        Name = "X-CSRF-TOKEN",
        In = ParameterLocation.Header,
        Required = true,
        Description = "Request token returned by GET /api/v1/auth/csrf.",
        Schema = new OpenApiSchema { Type = JsonSchemaType.String }
    });
}

if (metadata.OfType<LocalOnlyEndpointMetadata>().Any())
{
    operation.Extensions ??=
        new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal);
    operation.Extensions["x-local-only"] =
        new JsonNodeExtension(JsonValue.Create(true)!);
}
```

Add:

```csharp
using System.Text.Json.Nodes;
```

The existing authorization logic remains unchanged: `IAllowAnonymous` wins,
and only endpoints with authorization metadata receive `cookieAuth`.

- [ ] **Step 4: Run all OpenAPI tests before exporting**

Run:

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --filter FullyQualifiedName~OpenApiContractTests
```

Expected: metadata assertions pass; only
`RuntimeDocumentSemanticallyMatchesCommittedContract` fails because the
committed document still represents iteration 2.

- [ ] **Step 5: Export the deterministic contract**

Run from repository root:

```bash
dotnet build apps/api/src/Template.Api/Template.Api.csproj \
  --no-restore \
  -p:OpenApiGenerateDocuments=true
```

Expected: `contracts/openapi/v1.json` contains all seven operations, the
required header, `x-local-only`, only `cookieAuth`, and no server URL.

Rerun:

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --filter FullyQualifiedName~OpenApiContractTests
```

Expected: every OpenAPI test passes.

- [ ] **Step 6: Regenerate the TypeScript SDK**

Run:

```bash
cd apps/web
npm ci
npm run api:generate
```

Expected generated exports:

```ts
getAuthCapabilities;
getAuthSession;
getAuthCsrf;
logout;
createLocalAutomationScenario;
signInLocalAutomation;
deleteLocalAutomationScenario;
```

Expected generated request types require `headers: { "X-CSRF-TOKEN": string }`
for the four unsafe operations and generated response types include nullable
session/user fields without any raw cookie, password hash, ticket key, or
bearer token.

- [ ] **Step 7: Strengthen generated-contract and handwritten-DTO guards**

Replace the SDK test imports with:

```ts
import {
  createLocalAutomationScenario,
  deleteLocalAutomationScenario,
  getAuthCapabilities,
  getAuthCsrf,
  getAuthSession,
  getSystemStatus,
  logout,
  signInLocalAutomation,
} from "@/src/lib/api/generated";
```

Add this test:

```ts
it("tracks every iteration-3 auth operation", () => {
  expect(getAuthCapabilities).toEqual(expect.any(Function));
  expect(getAuthSession).toEqual(expect.any(Function));
  expect(getAuthCsrf).toEqual(expect.any(Function));
  expect(logout).toEqual(expect.any(Function));
  expect(createLocalAutomationScenario).toEqual(expect.any(Function));
  expect(signInLocalAutomation).toEqual(expect.any(Function));
  expect(deleteLocalAutomationScenario).toEqual(expect.any(Function));
});
```

Extend `check-boundaries.mjs` with one exact handwritten transport-type regex:

```js
const handwrittenTransportTypePattern =
  /(?:interface|type)\s+(?:SystemStatusResponse|ProblemDetails|HttpValidationProblemDetails|ApiResponseOfSystemStatusResponse|AuthCapabilitiesResponse|AuthSessionResponse|AuthUserResponse|AuthSessionMetadataResponse|AuthCsrfResponse|LocalAutomationScenarioResponse|LocalAutomationCleanupResponse|CreateLocalAutomationScenarioRequest|LocalAutomationSignInRequest)\b/;
```

Replace the current inline DTO condition with:

```js
if (handwrittenTransportTypePattern.test(content)) {
  violations.push(`handwritten OpenAPI DTO: ${localPath}`);
}
```

Extend `check-boundaries.node-test.mjs`:

```js
test("rejects handwritten authentication transport DTOs", async () => {
  await expectViolation(
    "src/__boundary_guard_test__/auth-dto.ts",
    "export type AuthSessionResponse = { authenticated: boolean };",
    /handwritten OpenAPI DTO/,
  );
});
```

Also require every generated operation:

```js
for (const operation of [
  "getSystemStatus",
  "getAuthCapabilities",
  "getAuthSession",
  "getAuthCsrf",
  "logout",
  "createLocalAutomationScenario",
  "signInLocalAutomation",
  "deleteLocalAutomationScenario",
]) {
  if (!new RegExp(`export const ${operation}\\b`).test(generatedSdk)) {
    violations.push(`generated ${operation} operation is missing`);
  }
}
```

- [ ] **Step 8: Run contract/generation/boundary checks**

Run:

```bash
cd apps/web
npm run api:check
npm run boundaries:check
npm test -- --runInBand test/contracts/generated-sdk.test.ts
```

Expected: regeneration is byte-clean, boundary self-tests pass, and generated
auth exports are callable.

- [ ] **Step 9: Commit**

```bash
cd ../..
git add \
  apps/api/src/Template.Api/OpenApi/OpenApiServiceCollectionExtensions.cs \
  apps/api/tests/Template.Api.Tests/OpenApiContractTests.cs \
  contracts/openapi/v1.json \
  apps/web/src/lib/api/generated \
  apps/web/test/contracts/generated-sdk.test.ts \
  apps/web/scripts/check-boundaries.mjs \
  apps/web/scripts/check-boundaries.node-test.mjs
git commit -m "Publish authentication REST contract"
```

## Task 11: Add generated-SDK auth adapters and safe redirects

**Files:**

- Modify: `apps/web/src/lib/api/result.ts`
- Create: `apps/web/src/features/authentication/authentication-routes.ts`
- Create: `apps/web/src/features/authentication/sanitize-auth-redirect.ts`
- Create: `apps/web/src/lib/api/auth/load-auth-capabilities.ts`
- Create: `apps/web/src/lib/api/auth/load-auth-session.ts`
- Create: `apps/web/src/lib/api/auth/server/load-server-auth-state.ts`
- Create: `apps/web/src/lib/api/auth/browser/get-auth-csrf.ts`
- Create: `apps/web/src/lib/api/auth/browser/create-local-automation-browser-session.ts`
- Create: `apps/web/src/lib/api/auth/browser/logout-browser-session.ts`
- Create: `apps/web/src/lib/api/server/request-headers.ts`
- Modify: `apps/web/src/features/application/application-routes.ts`
- Create: `apps/web/test/features/sanitize-auth-redirect.test.ts`
- Create: `apps/web/test/lib/api/auth-api.test.ts`
- Create: `apps/web/test/lib/api/server-request-headers.test.ts`

**Interfaces:**

- Consumes: only generated SDK operations/types and existing per-request browser/server clients.
- Produces: safe redirect helpers, request-bound SSR auth state, and browser CSRF-first create/logout adapters for the UI tasks.

- [ ] **Step 1: Re-read the installed Next.js 16.2.11 request-time rules**

Run:

```bash
sed -n '1,180p' apps/web/node_modules/next/dist/docs/01-app/03-api-reference/04-functions/connection.md
sed -n '1,180p' apps/web/node_modules/next/dist/docs/01-app/03-api-reference/04-functions/headers.md
sed -n '1,180p' apps/web/node_modules/next/dist/docs/01-app/03-api-reference/04-functions/redirect.md
sed -n '1,220p' apps/web/node_modules/next/dist/docs/01-app/03-api-reference/05-config/01-next-config-js/cacheComponents.md
```

Expected: `connection()` excludes work below it from prerendering; `headers()`
is async/read-only/request-time; `redirect()` throws and must remain outside
caught request failures; Cache Components requires runtime work under Suspense.

- [ ] **Step 2: Write failing redirect and adapter tests**

```ts
// apps/web/test/features/sanitize-auth-redirect.test.ts
import {
  authLoginUrl,
  sanitizeAuthRedirect,
} from "@/src/features/authentication/sanitize-auth-redirect";

describe("authentication redirect policy", () => {
  it.each([
    ["/dashboard", "/dashboard"],
    ["/settings?tab=profile", "/settings?tab=profile"],
    ["https://evil.test", "/dashboard"],
    ["//evil.test", "/dashboard"],
    ["/api/v1/auth/session", "/dashboard"],
    ["/auth/login", "/dashboard"],
    ["/auth/login?redirect=/dashboard", "/dashboard"],
    ["dashboard", "/dashboard"],
    [undefined, "/dashboard"],
  ])("sanitizes %p to %p", (value, expected) => {
    expect(sanitizeAuthRedirect(value)).toBe(expected);
  });

  it("encodes the protected target into the login URL", () => {
    expect(authLoginUrl("/dashboard")).toBe(
      "/auth/login?redirect=%2Fdashboard",
    );
  });
});
```

```ts
// apps/web/test/lib/api/auth-api.test.ts
/** @jest-environment node */

import type { Client } from "@/src/lib/api/generated/client";
import {
  createLocalAutomationScenario,
  getAuthCapabilities,
  getAuthCsrf,
  getAuthSession,
  logout,
} from "@/src/lib/api/generated";
import { createLocalAutomationBrowserSession } from "@/src/lib/api/auth/browser/create-local-automation-browser-session";
import { logoutBrowserSession } from "@/src/lib/api/auth/browser/logout-browser-session";
import { loadAuthCapabilities } from "@/src/lib/api/auth/load-auth-capabilities";
import { loadAuthSession } from "@/src/lib/api/auth/load-auth-session";

jest.mock("@/src/lib/api/generated", () => ({
  createLocalAutomationScenario: jest.fn(),
  getAuthCapabilities: jest.fn(),
  getAuthCsrf: jest.fn(),
  getAuthSession: jest.fn(),
  logout: jest.fn(),
}));

const client = {} as Client;
const mockedCapabilities = jest.mocked(getAuthCapabilities);
const mockedSession = jest.mocked(getAuthSession);
const mockedCsrf = jest.mocked(getAuthCsrf);
const mockedCreate = jest.mocked(createLocalAutomationScenario);
const mockedLogout = jest.mocked(logout);

beforeEach(() => {
  jest.clearAllMocks();
});

it("loads capability and session data from generated envelopes", async () => {
  mockedCapabilities.mockResolvedValue({
    data: {
      data: { localAutomationEnabled: true, providers: [] },
    },
    error: undefined,
    request: new Request("https://example.test"),
    response: new Response(),
  });
  mockedSession.mockResolvedValue({
    data: {
      data: { authenticated: false, user: null, session: null },
    },
    error: undefined,
    request: new Request("https://example.test"),
    response: new Response(),
  });

  await expect(loadAuthCapabilities(client)).resolves.toEqual({
    ok: true,
    data: { localAutomationEnabled: true, providers: [] },
  });
  await expect(loadAuthSession(client)).resolves.toEqual({
    ok: true,
    data: { authenticated: false, user: null, session: null },
  });
});

it("gets CSRF before creating a local browser session", async () => {
  mockedCsrf.mockResolvedValue({
    data: { data: { requestToken: "csrf-create" } },
    error: undefined,
    request: new Request("https://example.test"),
    response: new Response(),
  });
  mockedCreate.mockResolvedValue({
    data: {
      data: {
        user: {
          id: "01900000-0000-7000-8000-000000000001",
          name: "Local User",
          email: "local-agent+ui@local-agent.test",
          emailVerified: false,
          image: null,
        },
        email: "local-agent+ui@local-agent.test",
        password: "local-secret-password",
        cleanupUrl: "/api/local-auth/scenario",
      },
    },
    error: undefined,
    request: new Request("https://example.test"),
    response: new Response(),
  });

  const result = await createLocalAutomationBrowserSession(client);

  expect(result.ok).toBe(true);
  expect(mockedCreate).toHaveBeenCalledWith({
    client,
    body: {},
    headers: { "X-CSRF-TOKEN": "csrf-create" },
  });
  expect(mockedCsrf.mock.invocationCallOrder[0]).toBeLessThan(
    mockedCreate.mock.invocationCallOrder[0],
  );
});

it("gets CSRF before logout", async () => {
  mockedCsrf.mockResolvedValue({
    data: { data: { requestToken: "csrf-logout" } },
    error: undefined,
    request: new Request("https://example.test"),
    response: new Response(),
  });
  mockedLogout.mockResolvedValue({
    data: {
      data: { authenticated: false, user: null, session: null },
    },
    error: undefined,
    request: new Request("https://example.test"),
    response: new Response(),
  });

  await logoutBrowserSession(client);

  expect(mockedLogout).toHaveBeenCalledWith({
    client,
    headers: { "X-CSRF-TOKEN": "csrf-logout" },
  });
});
```

```ts
// apps/web/test/lib/api/server-request-headers.test.ts
/** @jest-environment node */

const headersMock = jest.fn();

jest.mock("next/headers", () => ({
  headers: () => headersMock(),
}));

import { readForwardedApiHeaders } from "@/src/lib/api/server/request-headers";

it("forwards only Cookie and correlation ID", async () => {
  headersMock.mockResolvedValue(
    new Headers({
      cookie: "__Host-template.session=opaque",
      authorization: "Bearer must-not-forward",
      "x-correlation-id": "trace-auth",
      "x-extra": "must-not-forward",
    }),
  );

  await expect(readForwardedApiHeaders()).resolves.toEqual({
    cookie: "__Host-template.session=opaque",
    correlationId: "trace-auth",
  });
});
```

- [ ] **Step 3: Run focused Jest tests and verify they fail**

Run:

```bash
cd apps/web
npm test -- --runInBand \
  test/features/sanitize-auth-redirect.test.ts \
  test/lib/api/auth-api.test.ts \
  test/lib/api/server-request-headers.test.ts
```

Expected: module-resolution failures for the new auth feature/adapters.

- [ ] **Step 4: Implement routes and redirect sanitization**

```ts
// apps/web/src/features/authentication/authentication-routes.ts
import type { Route } from "next";

export const authenticationRoutes = {
  login: "/auth/login" as Route,
  dashboard: "/dashboard" as Route,
} as const;
```

```ts
// apps/web/src/features/authentication/sanitize-auth-redirect.ts
import type { Route } from "next";

import { authenticationRoutes } from "@/src/features/authentication/authentication-routes";

export function sanitizeAuthRedirect(
  value: string | string[] | undefined,
): Route {
  const candidate = Array.isArray(value) ? value[0] : value;
  if (
    !candidate ||
    !candidate.startsWith("/") ||
    candidate.startsWith("//") ||
    candidate === authenticationRoutes.login ||
    candidate.startsWith(`${authenticationRoutes.login}?`) ||
    candidate.startsWith("/auth/") ||
    candidate === "/api" ||
    candidate.startsWith("/api/")
  ) {
    return authenticationRoutes.dashboard;
  }

  return candidate as Route;
}

export function authLoginUrl(redirectPath: string): Route {
  const safe = sanitizeAuthRedirect(redirectPath);
  return `${authenticationRoutes.login}?redirect=${encodeURIComponent(safe)}` as Route;
}
```

Extend `applicationRoutes`:

```ts
export const applicationRoutes = {
  home: "/" as Route,
  login: "/auth/login" as Route,
  dashboard: "/dashboard" as Route,
} as const;
```

- [ ] **Step 5: Add generated-SDK result aliases and read adapters**

Extend `result.ts` imports and aliases:

```ts
import type {
  AuthCapabilitiesResponse,
  AuthSessionResponse,
  LocalAutomationScenarioResponse,
} from "@/src/lib/api/generated";

export type AuthCapabilitiesResult = ApiResult<AuthCapabilitiesResponse>;
export type AuthSessionResult = ApiResult<AuthSessionResponse>;
export type LocalAutomationScenarioResult =
  ApiResult<LocalAutomationScenarioResponse>;
export type AuthCsrfResult = ApiResult<string>;
```

```ts
// apps/web/src/lib/api/auth/load-auth-capabilities.ts
import { normalizeApiFailure } from "@/src/lib/api/failures/normalize-api-failure";
import { getAuthCapabilities } from "@/src/lib/api/generated";
import type { Client } from "@/src/lib/api/generated/client";
import type { AuthCapabilitiesResult } from "@/src/lib/api/result";

export async function loadAuthCapabilities(
  client: Client,
): Promise<AuthCapabilitiesResult> {
  try {
    const result = await getAuthCapabilities({ client, cache: "no-store" });
    return result.data !== undefined
      ? { ok: true, data: result.data.data }
      : {
          ok: false,
          failure: normalizeApiFailure(result.error, result.response),
        };
  } catch (error) {
    return { ok: false, failure: normalizeApiFailure(error) };
  }
}
```

```ts
// apps/web/src/lib/api/auth/load-auth-session.ts
import { normalizeApiFailure } from "@/src/lib/api/failures/normalize-api-failure";
import { getAuthSession } from "@/src/lib/api/generated";
import type { Client } from "@/src/lib/api/generated/client";
import type { AuthSessionResult } from "@/src/lib/api/result";

export async function loadAuthSession(
  client: Client,
): Promise<AuthSessionResult> {
  try {
    const result = await getAuthSession({ client, cache: "no-store" });
    return result.data !== undefined
      ? { ok: true, data: result.data.data }
      : {
          ok: false,
          failure: normalizeApiFailure(result.error, result.response),
        };
  } catch (error) {
    return { ok: false, failure: normalizeApiFailure(error) };
  }
}
```

- [ ] **Step 6: Add request-header allowlisting and composed SSR state**

```ts
// apps/web/src/lib/api/server/request-headers.ts
import "server-only";

import { headers } from "next/headers";

import type { ForwardedApiHeaders } from "@/src/lib/api/server/client";

export async function readForwardedApiHeaders(): Promise<ForwardedApiHeaders> {
  const incoming = await headers();
  const cookie = incoming.get("cookie") ?? undefined;
  const correlationId = incoming.get("x-correlation-id") ?? undefined;
  return {
    ...(cookie ? { cookie } : {}),
    ...(correlationId ? { correlationId } : {}),
  };
}
```

```ts
// apps/web/src/lib/api/auth/server/load-server-auth-state.ts
import "server-only";

import type {
  AuthCapabilitiesResponse,
  AuthSessionResponse,
} from "@/src/lib/api/generated";
import { loadAuthCapabilities } from "@/src/lib/api/auth/load-auth-capabilities";
import { loadAuthSession } from "@/src/lib/api/auth/load-auth-session";
import type { ApiResult } from "@/src/lib/api/result";
import { createServerApiClient } from "@/src/lib/api/server/client";
import { readForwardedApiHeaders } from "@/src/lib/api/server/request-headers";

export type AuthPageState = Readonly<{
  capabilities: AuthCapabilitiesResponse;
  session: AuthSessionResponse;
}>;

export async function loadServerAuthState(): Promise<ApiResult<AuthPageState>> {
  const client = createServerApiClient(await readForwardedApiHeaders());
  if (!client.ok) {
    return { ok: false, failure: client.failure };
  }

  const [capabilities, session] = await Promise.all([
    loadAuthCapabilities(client.client),
    loadAuthSession(client.client),
  ]);
  if (!capabilities.ok) {
    return capabilities;
  }
  if (!session.ok) {
    return session;
  }

  return {
    ok: true,
    data: {
      capabilities: capabilities.data,
      session: session.data,
    },
  };
}
```

- [ ] **Step 7: Implement CSRF-first browser mutations**

```ts
// apps/web/src/lib/api/auth/browser/get-auth-csrf.ts
"use client";

import { normalizeApiFailure } from "@/src/lib/api/failures/normalize-api-failure";
import { getAuthCsrf } from "@/src/lib/api/generated";
import type { Client } from "@/src/lib/api/generated/client";
import type { AuthCsrfResult } from "@/src/lib/api/result";

export async function getAuthCsrfToken(
  client: Client,
): Promise<AuthCsrfResult> {
  try {
    const result = await getAuthCsrf({ client, cache: "no-store" });
    return result.data !== undefined
      ? { ok: true, data: result.data.data.requestToken }
      : {
          ok: false,
          failure: normalizeApiFailure(result.error, result.response),
        };
  } catch (error) {
    return { ok: false, failure: normalizeApiFailure(error) };
  }
}
```

```ts
// apps/web/src/lib/api/auth/browser/create-local-automation-browser-session.ts
"use client";

import { getAuthCsrfToken } from "@/src/lib/api/auth/browser/get-auth-csrf";
import { normalizeApiFailure } from "@/src/lib/api/failures/normalize-api-failure";
import { createLocalAutomationScenario } from "@/src/lib/api/generated";
import type { Client } from "@/src/lib/api/generated/client";
import type { LocalAutomationScenarioResult } from "@/src/lib/api/result";

export async function createLocalAutomationBrowserSession(
  client: Client,
): Promise<LocalAutomationScenarioResult> {
  const csrf = await getAuthCsrfToken(client);
  if (!csrf.ok) {
    return csrf;
  }

  try {
    const result = await createLocalAutomationScenario({
      client,
      body: {},
      headers: { "X-CSRF-TOKEN": csrf.data },
    });
    return result.data !== undefined
      ? { ok: true, data: result.data.data }
      : {
          ok: false,
          failure: normalizeApiFailure(result.error, result.response),
        };
  } catch (error) {
    return { ok: false, failure: normalizeApiFailure(error) };
  }
}
```

```ts
// apps/web/src/lib/api/auth/browser/logout-browser-session.ts
"use client";

import { getAuthCsrfToken } from "@/src/lib/api/auth/browser/get-auth-csrf";
import { normalizeApiFailure } from "@/src/lib/api/failures/normalize-api-failure";
import { logout } from "@/src/lib/api/generated";
import type { Client } from "@/src/lib/api/generated/client";
import type { AuthSessionResult } from "@/src/lib/api/result";

export async function logoutBrowserSession(
  client: Client,
): Promise<AuthSessionResult> {
  const csrf = await getAuthCsrfToken(client);
  if (!csrf.ok) {
    return csrf;
  }

  try {
    const result = await logout({
      client,
      headers: { "X-CSRF-TOKEN": csrf.data },
    });
    return result.data !== undefined
      ? { ok: true, data: result.data.data }
      : {
          ok: false,
          failure: normalizeApiFailure(result.error, result.response),
        };
  } catch (error) {
    return { ok: false, failure: normalizeApiFailure(error) };
  }
}
```

- [ ] **Step 8: Run focused tests, typecheck, and boundary checks**

Run:

```bash
cd apps/web
npm test -- --runInBand \
  test/features/sanitize-auth-redirect.test.ts \
  test/lib/api/auth-api.test.ts \
  test/lib/api/server-request-headers.test.ts
npm run typecheck
npm run boundaries:check
```

Expected: all focused tests pass, generated types match adapter signatures, and
the allowlist contains no Authorization forwarding surface.

- [ ] **Step 9: Commit**

```bash
cd ../..
git add \
  apps/web/src/features \
  apps/web/src/lib/api \
  apps/web/test/features \
  apps/web/test/lib/api
git commit -m "Add web authentication API adapters"
```

## Task 12: Build the reference-like login route and one-click local flow

**Files:**

- Modify: `apps/web/src/app/layout.tsx`
- Move: `apps/web/src/app/page.tsx` → `apps/web/src/app/(site)/page.tsx`
- Create: `apps/web/src/app/(site)/layout.tsx`
- Create: `apps/web/src/app/(simple)/layout.tsx`
- Create: `apps/web/src/app/(simple)/auth/login/page.tsx`
- Create: `apps/web/src/app/(simple)/auth/login/loading.tsx`
- Create: `apps/web/src/components/authentication/auth-api-failure.tsx`
- Create: `apps/web/src/components/authentication/local-automation-login-panel.tsx`
- Create: `apps/web/src/components/authentication/login-runtime.tsx`
- Create: `apps/web/src/messages/auth.en.json`
- Create: `apps/web/src/messages/auth.ru.json`
- Modify: `apps/web/src/messages/common.en.json`
- Modify: `apps/web/src/messages/common.ru.json`
- Modify: `apps/web/src/messages/system.en.json`
- Modify: `apps/web/src/messages/system.ru.json`
- Modify: `apps/web/src/i18n/messages.ts`
- Modify: `apps/web/test/support/render.tsx`
- Modify: `apps/web/test/i18n/messages.test.ts`
- Modify: `apps/web/test/app/home-page.test.tsx`
- Modify: `apps/web/test/app/layout.test.tsx`
- Create: `apps/web/test/components/auth-api-failure.test.tsx`
- Create: `apps/web/test/components/local-automation-login-panel.test.tsx`
- Create: `apps/web/test/components/login-runtime.test.tsx`

**Interfaces:**

- Consumes: `loadServerAuthState`, `createLocalAutomationBrowserSession`, `sanitizeAuthRedirect`, generated DTOs, `connection()`, and Next `redirect()`.
- Produces: `/auth/login` with no site header, one visible one-click local button only when enabled, authenticated redirect, safe failure rendering, and a home **Get Started** link.

- [ ] **Step 1: Write failing login/home tests**

Update the home-page test import to:

```ts
import HomePage from "@/src/app/(site)/page";
```

Change its assertion to require:

```ts
expect(screen.getByRole("link", { name: "Get Started" })).toHaveAttribute(
  "href",
  "/auth/login?redirect=%2Fdashboard",
);
```

Create the safe failure test:

```tsx
// apps/web/test/components/auth-api-failure.test.tsx
import { screen } from "@testing-library/react";

import { AuthApiFailure } from "@/src/components/authentication/auth-api-failure";
import type { ApiFailure } from "@/src/lib/api/result";
import { renderWithMessages } from "@/test/support/render";

it("renders only generic localized copy and the trace ID", () => {
  const failure = {
    kind: "problem",
    code: "internal_error",
    status: 500,
    traceId: "trace-safe",
    detail: "sensitive database detail",
  } as ApiFailure;

  renderWithMessages(<AuthApiFailure failure={failure} />);

  expect(
    screen.getByRole("heading", { name: "Authentication is unavailable" }),
  ).toBeInTheDocument();
  expect(screen.getByText("Try again later.")).toBeInTheDocument();
  expect(screen.getByText("trace-safe")).toBeInTheDocument();
  expect(
    screen.queryByText("sensitive database detail"),
  ).not.toBeInTheDocument();
});
```

Create the panel test:

```tsx
// apps/web/test/components/local-automation-login-panel.test.tsx
import { fireEvent, screen, waitFor } from "@testing-library/react";

import { LocalAutomationLoginPanel } from "@/src/components/authentication/local-automation-login-panel";
import { createLocalAutomationBrowserSession } from "@/src/lib/api/auth/browser/create-local-automation-browser-session";
import { renderWithMessages } from "@/test/support/render";

const push = jest.fn();
const refresh = jest.fn();

jest.mock("next/navigation", () => ({
  useRouter: () => ({ push, refresh }),
}));
jest.mock("@/src/lib/api/browser/client", () => ({
  createBrowserApiClient: () => ({ id: "browser-client" }),
}));
jest.mock(
  "@/src/lib/api/auth/browser/create-local-automation-browser-session",
  () => ({ createLocalAutomationBrowserSession: jest.fn() }),
);

const createSession = jest.mocked(createLocalAutomationBrowserSession);

beforeEach(() => {
  jest.clearAllMocks();
});

it("creates a user, discards plaintext credentials, and navigates safely", async () => {
  createSession.mockResolvedValue({
    ok: true,
    data: {
      user: {
        id: "01900000-0000-7000-8000-000000000001",
        name: "Local User",
        email: "local-agent+panel@local-agent.test",
        emailVerified: false,
        image: null,
      },
      email: "local-agent+panel@local-agent.test",
      password: "local-must-never-render",
      cleanupUrl: "/api/local-auth/scenario",
    },
  });
  renderWithMessages(<LocalAutomationLoginPanel redirectPath="/dashboard" />);

  fireEvent.click(
    screen.getByRole("button", { name: "Create local automation user" }),
  );

  await waitFor(() => {
    expect(refresh).toHaveBeenCalledTimes(1);
    expect(push).toHaveBeenCalledWith("/dashboard");
  });
  expect(screen.queryByText("local-must-never-render")).not.toBeInTheDocument();
});

it("localizes stable failures without backend detail", async () => {
  createSession.mockResolvedValue({
    ok: false,
    failure: {
      kind: "problem",
      code: "rate_limited",
      status: 429,
      traceId: "trace-panel",
    },
  });
  renderWithMessages(<LocalAutomationLoginPanel redirectPath="/dashboard" />);

  fireEvent.click(
    screen.getByRole("button", { name: "Create local automation user" }),
  );

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Too many local sign-in attempts.",
  );
  expect(screen.getByRole("alert")).toHaveTextContent("trace-panel");
  expect(push).not.toHaveBeenCalled();
});
```

Create runtime tests:

```tsx
// apps/web/test/components/login-runtime.test.tsx
import { render, screen } from "@testing-library/react";

import { LoginRuntime } from "@/src/components/authentication/login-runtime";
import { loadServerAuthState } from "@/src/lib/api/auth/server/load-server-auth-state";

const redirect = jest.fn((path: string) => {
  throw new Error(`NEXT_REDIRECT:${path}`);
});

jest.mock("next/server", () => ({
  connection: jest.fn().mockResolvedValue(undefined),
}));
jest.mock("next/navigation", () => ({ redirect }));
jest.mock("next-intl/server", () => ({
  getTranslations: async () => (key: string) =>
    ({
      eyebrow: "Authentication",
      title: "Sign in",
      description: "Use an available sign-in method.",
      unavailable: "No production sign-in provider is configured yet.",
      "failure.title": "Authentication is unavailable",
      "failure.description": "Try again later.",
    })[key] ?? key,
}));
jest.mock("@/src/lib/api/auth/server/load-server-auth-state", () => ({
  loadServerAuthState: jest.fn(),
}));
jest.mock("@/src/components/authentication/auth-api-failure", () => ({
  AuthApiFailure: ({ failure }: { failure: { traceId?: string } }) => (
    <section role="alert">
      <h2>Authentication is unavailable</h2>
      <p>Try again later.</p>
      {failure.traceId ? <p>{failure.traceId}</p> : null}
    </section>
  ),
}));
jest.mock(
  "@/src/components/authentication/local-automation-login-panel",
  () => ({
    LocalAutomationLoginPanel: ({ redirectPath }: { redirectPath: string }) => (
      <div data-testid="local-panel">{redirectPath}</div>
    ),
  }),
);

const loadState = jest.mocked(loadServerAuthState);

it("shows one local panel only when local automation is enabled", async () => {
  loadState.mockResolvedValue({
    ok: true,
    data: {
      capabilities: { localAutomationEnabled: true, providers: [] },
      session: { authenticated: false, user: null, session: null },
    },
  });

  render(
    await LoginRuntime({
      searchParams: Promise.resolve({ redirect: "/dashboard" }),
    }),
  );

  expect(screen.getByTestId("local-panel")).toHaveTextContent("/dashboard");
  expect(screen.queryByRole("textbox")).not.toBeInTheDocument();
});

it("shows the deferred-provider state when local automation is disabled", async () => {
  loadState.mockResolvedValue({
    ok: true,
    data: {
      capabilities: { localAutomationEnabled: false, providers: [] },
      session: { authenticated: false, user: null, session: null },
    },
  });

  render(
    await LoginRuntime({
      searchParams: Promise.resolve({ redirect: "/dashboard" }),
    }),
  );

  expect(
    screen.getByText("No production sign-in provider is configured yet."),
  ).toBeInTheDocument();
  expect(screen.queryByTestId("local-panel")).not.toBeInTheDocument();
});

it("redirects an authenticated session to the sanitized target", async () => {
  loadState.mockResolvedValue({
    ok: true,
    data: {
      capabilities: { localAutomationEnabled: true, providers: [] },
      session: {
        authenticated: true,
        user: {
          id: "01900000-0000-7000-8000-000000000001",
          name: "Local User",
          email: "local-agent+runtime@local-agent.test",
          emailVerified: false,
          image: null,
        },
        session: {
          id: "01900000-0000-7000-8000-000000000002",
          createdAt: "2026-07-24T00:00:00Z",
          updatedAt: "2026-07-24T00:00:00Z",
          expiresAt: "2026-07-31T00:00:00Z",
        },
      },
    },
  });

  await expect(
    LoginRuntime({
      searchParams: Promise.resolve({ redirect: "https://evil.test" }),
    }),
  ).rejects.toThrow("NEXT_REDIRECT:/dashboard");
});

it("renders a safe failure instead of local controls", async () => {
  loadState.mockResolvedValue({
    ok: false,
    failure: {
      kind: "problem",
      code: "internal_error",
      status: 500,
      traceId: "trace-login",
    },
  });

  render(
    await LoginRuntime({
      searchParams: Promise.resolve({ redirect: "/dashboard" }),
    }),
  );

  expect(
    screen.getByRole("heading", { name: "Authentication is unavailable" }),
  ).toBeInTheDocument();
  expect(screen.getByText("trace-login")).toBeInTheDocument();
  expect(screen.queryByTestId("local-panel")).not.toBeInTheDocument();
});
```

- [ ] **Step 2: Run focused Jest tests and verify they fail**

Run:

```bash
cd apps/web
npm test -- --runInBand \
  test/app/home-page.test.tsx \
  test/components/auth-api-failure.test.tsx \
  test/components/local-automation-login-panel.test.tsx \
  test/components/login-runtime.test.tsx
```

Expected: moved page and authentication components/messages are missing.

- [ ] **Step 3: Split providers-only, site, and simple layouts**

Change root layout body to:

```tsx
<body>
  <AppProviders locale={locale} messages={messages} timeZone={timeZone}>
    {children}
  </AppProviders>
</body>
```

Move the existing home page:

```bash
mkdir -p 'apps/web/src/app/(site)'
git mv apps/web/src/app/page.tsx 'apps/web/src/app/(site)/page.tsx'
```

```tsx
// apps/web/src/app/(site)/layout.tsx
import type { ReactNode } from "react";

import { SiteHeader } from "@/src/components/application/site-header";

export default function SiteLayout({
  children,
}: Readonly<{ children: ReactNode }>) {
  return (
    <>
      <SiteHeader />
      {children}
    </>
  );
}
```

```tsx
// apps/web/src/app/(simple)/layout.tsx
import type { ReactNode } from "react";

export default function SimpleLayout({
  children,
}: Readonly<{ children: ReactNode }>) {
  return children;
}
```

Add this assertion to `layout.test.tsx` after the existing provider
configuration assertions:

```tsx
expect(provider.props.children).toEqual(<span>Content</span>);
```

Remove any root-layout `SiteHeader` assertion; `SiteHeader` is owned only by
the new `(site)` layout.

- [ ] **Step 4: Add localized auth bundles**

```json
// apps/web/src/messages/auth.en.json
{
  "login": {
    "eyebrow": "Authentication",
    "title": "Sign in",
    "description": "Use an available sign-in method.",
    "unavailable": "No production sign-in provider is configured yet.",
    "loading": "Checking available sign-in methods",
    "failure": {
      "title": "Authentication is unavailable",
      "description": "Try again later."
    }
  },
  "localAutomation": {
    "title": "Local automation",
    "description": "Create a local credential user for development and browser automation.",
    "button": "Create local automation user",
    "pending": "Creating local automation user",
    "failure": "Local authentication failed.",
    "rateLimited": "Too many local sign-in attempts.",
    "invalidRequest": "The local sign-in request was rejected.",
    "unavailable": "The authentication API is unavailable.",
    "traceId": "Trace ID: {traceId}"
  }
}
```

```json
// apps/web/src/messages/auth.ru.json
{
  "login": {
    "eyebrow": "Аутентификация",
    "title": "Вход",
    "description": "Используйте доступный способ входа.",
    "unavailable": "Production-провайдер входа пока не настроен.",
    "loading": "Проверяем доступные способы входа",
    "failure": {
      "title": "Аутентификация недоступна",
      "description": "Повторите попытку позже."
    }
  },
  "localAutomation": {
    "title": "Локальная автоматизация",
    "description": "Создайте локального пользователя с паролем для разработки и браузерной автоматизации.",
    "button": "Создать локального пользователя автоматизации",
    "pending": "Создаём локального пользователя",
    "failure": "Локальная аутентификация не выполнена.",
    "rateLimited": "Слишком много попыток локального входа.",
    "invalidRequest": "Запрос локального входа отклонён.",
    "unavailable": "API аутентификации недоступен.",
    "traceId": "Идентификатор трассировки: {traceId}"
  }
}
```

Use these exact message-object changes in `messages.ts`:

```ts
import authEn from "@/src/messages/auth.en.json";
import authRu from "@/src/messages/auth.ru.json";

const englishMessages = {
  auth: authEn,
  common: commonEn,
  system: systemEn,
};

const messagesByLocale = {
  en: englishMessages,
  ru: {
    auth: authRu,
    common: commonRu,
    system: systemRu,
  },
} satisfies Record<AppLocale, I18nMessages>;
```

In `test/support/render.tsx`, import the English bundle and replace the test
message object with:

```ts
import auth from "@/src/messages/auth.en.json";

export const englishMessages = { auth, common, system };
```

Extend the i18n shape test:

```ts
expect(Object.keys(russian.auth)).toEqual(Object.keys(english.auth));
expect(russian.auth.login.title).not.toBe(english.auth.login.title);
```

Add `"getStarted": "Get Started"` / `"getStarted": "Начать"` under
`system.page`. Change `system.page.eyebrow` to migration iteration 3 in both
locales while preserving the technical REST-status content.

- [ ] **Step 5: Add a safe auth failure component**

```tsx
// apps/web/src/components/authentication/auth-api-failure.tsx
"use client";

import { useTranslations } from "next-intl";

import type { ApiFailure } from "@/src/lib/api/result";

export function AuthApiFailure({ failure }: Readonly<{ failure: ApiFailure }>) {
  const t = useTranslations("auth.login.failure");

  return (
    <section className="space-y-2" role="alert">
      <h2 className="text-lg font-semibold">{t("title")}</h2>
      <p className="text-sm text-muted-foreground">{t("description")}</p>
      {failure.kind === "problem" && failure.traceId ? (
        <p className="font-mono text-xs text-muted-foreground">
          {failure.traceId}
        </p>
      ) : null}
    </section>
  );
}
```

- [ ] **Step 6: Implement the one-click Client Component**

```tsx
// apps/web/src/components/authentication/local-automation-login-panel.tsx
"use client";

import type { Route } from "next";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { useState } from "react";

import { Button } from "@/src/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/src/components/ui/card";
import { createLocalAutomationBrowserSession } from "@/src/lib/api/auth/browser/create-local-automation-browser-session";
import { createBrowserApiClient } from "@/src/lib/api/browser/client";
import type { ApiFailure } from "@/src/lib/api/result";

function failureKey(failure: ApiFailure) {
  if (failure.kind === "network" || failure.kind === "configuration") {
    return "unavailable" as const;
  }
  return failure.code === "rate_limited"
    ? ("rateLimited" as const)
    : failure.code === "validation_failed" ||
        failure.code === "invalid_request" ||
        failure.code === "antiforgery_failed"
      ? ("invalidRequest" as const)
      : ("failure" as const);
}

export function LocalAutomationLoginPanel({
  redirectPath,
}: Readonly<{ redirectPath: Route }>) {
  const router = useRouter();
  const t = useTranslations("auth.localAutomation");
  const [pending, setPending] = useState(false);
  const [failure, setFailure] = useState<ApiFailure | null>(null);

  async function createSession() {
    setPending(true);
    setFailure(null);
    const result = await createLocalAutomationBrowserSession(
      createBrowserApiClient(),
    );
    if (!result.ok) {
      setFailure(result.failure);
      setPending(false);
      return;
    }

    router.refresh();
    router.push(redirectPath);
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t("title")}</CardTitle>
        <CardDescription>{t("description")}</CardDescription>
      </CardHeader>
      <CardContent className="space-y-3">
        {failure ? (
          <div className="space-y-1 text-sm text-destructive" role="alert">
            <p>{t(failureKey(failure))}</p>
            {failure.kind === "problem" && failure.traceId ? (
              <p className="font-mono text-xs">
                {t("traceId", { traceId: failure.traceId })}
              </p>
            ) : null}
          </div>
        ) : null}
        <Button
          className="w-full"
          disabled={pending}
          onClick={() => void createSession()}
          type="button"
        >
          {pending ? t("pending") : t("button")}
        </Button>
      </CardContent>
    </Card>
  );
}
```

- [ ] **Step 7: Implement request-time login composition under Suspense**

```tsx
// apps/web/src/components/authentication/login-runtime.tsx
import { redirect } from "next/navigation";
import { connection } from "next/server";
import { getTranslations } from "next-intl/server";

import { AuthApiFailure } from "@/src/components/authentication/auth-api-failure";
import { LocalAutomationLoginPanel } from "@/src/components/authentication/local-automation-login-panel";
import { sanitizeAuthRedirect } from "@/src/features/authentication/sanitize-auth-redirect";
import { loadServerAuthState } from "@/src/lib/api/auth/server/load-server-auth-state";

export async function LoginRuntime({
  searchParams,
}: Readonly<{
  searchParams: Promise<{ redirect?: string | string[] }>;
}>) {
  await connection();
  const redirectPath = sanitizeAuthRedirect((await searchParams).redirect);
  const result = await loadServerAuthState();
  const t = await getTranslations("auth.login");

  if (!result.ok) {
    return <AuthApiFailure failure={result.failure} />;
  }
  if (result.data.session.authenticated) {
    redirect(redirectPath);
  }

  return (
    <section className="w-full max-w-md space-y-6">
      <div className="space-y-2">
        <p className="text-xs font-medium tracking-[0.2em] text-muted-foreground uppercase">
          {t("eyebrow")}
        </p>
        <h1 className="text-3xl font-semibold tracking-tight">{t("title")}</h1>
        <p className="text-sm text-muted-foreground">{t("description")}</p>
      </div>
      {result.data.capabilities.localAutomationEnabled ? (
        <LocalAutomationLoginPanel redirectPath={redirectPath} />
      ) : (
        <p className="text-sm text-muted-foreground">{t("unavailable")}</p>
      )}
    </section>
  );
}
```

```tsx
// apps/web/src/app/(simple)/auth/login/page.tsx
import { getTranslations } from "next-intl/server";
import { Suspense } from "react";

import { LoginRuntime } from "@/src/components/authentication/login-runtime";

export default async function LoginPage({
  searchParams,
}: Readonly<{
  searchParams: Promise<{ redirect?: string | string[] }>;
}>) {
  const t = await getTranslations("auth.login");
  return (
    <main className="grid min-h-screen place-items-center px-4 py-12">
      <Suspense fallback={<p role="status">{t("loading")}</p>}>
        <LoginRuntime searchParams={searchParams} />
      </Suspense>
    </main>
  );
}
```

```tsx
// apps/web/src/app/(simple)/auth/login/loading.tsx
import { getTranslations } from "next-intl/server";

export default async function LoginLoading() {
  const t = await getTranslations("auth.login");
  return (
    <main className="grid min-h-screen place-items-center px-4 py-12">
      <p role="status">{t("loading")}</p>
    </main>
  );
}
```

- [ ] **Step 8: Add the home CTA without removing the iteration-2 smoke**

In the moved home page, import `Link`, `Button`, `authLoginUrl`, and
`authenticationRoutes`, then add below the description:

```tsx
<Button asChild>
  <Link href={authLoginUrl(authenticationRoutes.dashboard)}>
    {page("getStarted")}
  </Link>
</Button>
```

Use the `system.page.getStarted` labels added in Step 4; do not duplicate this
key under `common.actions`.

- [ ] **Step 9: Run Jest, i18n, type, boundary, and build checks**

Run:

```bash
cd apps/web
npm test -- --runInBand
npm run typecheck
npm run boundaries:check
npm run build
```

Expected: all tests pass; `next build` completes without a live API because
login runtime work is below `connection()` and Suspense; the login route has no
site header.

- [ ] **Step 10: Commit**

```bash
cd ../..
git add \
  apps/web/src/app \
  apps/web/src/components/authentication \
  apps/web/src/i18n \
  apps/web/src/messages \
  apps/web/test
git commit -m "Add local automation login UI"
```

## Task 13: Add the protected dashboard proof and REST logout

**Files:**

- Create: `apps/web/src/lib/api/auth/server/load-server-auth-session.ts`
- Create: `apps/web/src/components/authentication/logout-button.tsx`
- Create: `apps/web/src/components/authentication/dashboard-runtime.tsx`
- Create: `apps/web/src/app/(site)/dashboard/page.tsx`
- Create: `apps/web/src/app/(site)/dashboard/loading.tsx`
- Modify: `apps/web/src/messages/auth.en.json`
- Modify: `apps/web/src/messages/auth.ru.json`
- Create: `apps/web/test/components/logout-button.test.tsx`
- Create: `apps/web/test/components/dashboard-runtime.test.tsx`

**Interfaces:**

- Consumes: generated session/logout operations through Task 11 adapters, safe login URLs, `connection()`, and `redirect()`.
- Produces: protected `/dashboard`, visible safe session proof, network/config failure state without false anonymous redirect, and CSRF-first logout.

- [ ] **Step 1: Write failing dashboard and logout tests**

```tsx
// apps/web/test/components/dashboard-runtime.test.tsx
import { render, screen } from "@testing-library/react";

import { DashboardRuntime } from "@/src/components/authentication/dashboard-runtime";
import { loadServerAuthSession } from "@/src/lib/api/auth/server/load-server-auth-session";

const redirect = jest.fn((path: string) => {
  throw new Error(`NEXT_REDIRECT:${path}`);
});

jest.mock("next/server", () => ({
  connection: jest.fn().mockResolvedValue(undefined),
}));
jest.mock("next/navigation", () => ({ redirect }));
jest.mock("next-intl/server", () => ({
  getTranslations: async () => (key: string) =>
    ({
      eyebrow: "Iteration 3 session proof",
      title: "Authenticated dashboard",
      description: "This temporary page proves the browser session.",
      name: "Name",
      email: "Email",
      emailVerified: "Email verified",
      sessionId: "Session ID",
      expiresAt: "Expires",
      yes: "Yes",
      no: "No",
      "failure.title": "Authentication is unavailable",
      "failure.description": "Try again later.",
    })[key] ?? key,
}));
jest.mock("@/src/lib/api/auth/server/load-server-auth-session", () => ({
  loadServerAuthSession: jest.fn(),
}));
jest.mock("@/src/components/authentication/auth-api-failure", () => ({
  AuthApiFailure: () => (
    <section role="alert">
      <h2>Authentication is unavailable</h2>
      <p>Try again later.</p>
    </section>
  ),
}));
jest.mock("@/src/components/authentication/logout-button", () => ({
  LogoutButton: () => <button type="button">Log out</button>,
}));

const loadSession = jest.mocked(loadServerAuthSession);

it("redirects only an explicit anonymous projection", async () => {
  loadSession.mockResolvedValue({
    ok: true,
    data: { authenticated: false, user: null, session: null },
  });

  await expect(DashboardRuntime()).rejects.toThrow(
    "NEXT_REDIRECT:/auth/login?redirect=%2Fdashboard",
  );
});

it("renders a safe failure instead of redirecting on API outage", async () => {
  loadSession.mockResolvedValue({
    ok: false,
    failure: { kind: "network", code: "api_unavailable" },
  });

  render(await DashboardRuntime());

  expect(
    screen.getByRole("heading", { name: "Authentication is unavailable" }),
  ).toBeInTheDocument();
  expect(redirect).not.toHaveBeenCalled();
});

it("renders only safe user and session fields", async () => {
  loadSession.mockResolvedValue({
    ok: true,
    data: {
      authenticated: true,
      user: {
        id: "01900000-0000-7000-8000-000000000001",
        name: "Local Dashboard User",
        email: "local-agent+dashboard@local-agent.test",
        emailVerified: false,
        image: null,
      },
      session: {
        id: "01900000-0000-7000-8000-000000000002",
        createdAt: "2026-07-24T00:00:00Z",
        updatedAt: "2026-07-24T00:00:00Z",
        expiresAt: "2026-07-31T00:00:00Z",
      },
    },
  });

  render(await DashboardRuntime());

  expect(
    screen.getByRole("heading", { name: "Authenticated dashboard" }),
  ).toBeInTheDocument();
  expect(screen.getByText("Local Dashboard User")).toBeInTheDocument();
  expect(
    screen.getByText("local-agent+dashboard@local-agent.test"),
  ).toBeInTheDocument();
  expect(
    screen.getByText("01900000-0000-7000-8000-000000000002"),
  ).toBeInTheDocument();
  expect(document.body.textContent).not.toMatch(/password|ticket_key|cookie/i);
});
```

```tsx
// apps/web/test/components/logout-button.test.tsx
import { fireEvent, screen, waitFor } from "@testing-library/react";

import { LogoutButton } from "@/src/components/authentication/logout-button";
import { logoutBrowserSession } from "@/src/lib/api/auth/browser/logout-browser-session";
import { renderWithMessages } from "@/test/support/render";

const replace = jest.fn();
const refresh = jest.fn();

jest.mock("next/navigation", () => ({
  useRouter: () => ({ replace, refresh }),
}));
jest.mock("@/src/lib/api/browser/client", () => ({
  createBrowserApiClient: () => ({ id: "browser-client" }),
}));
jest.mock("@/src/lib/api/auth/browser/logout-browser-session", () => ({
  logoutBrowserSession: jest.fn(),
}));

const logout = jest.mocked(logoutBrowserSession);

beforeEach(() => {
  jest.clearAllMocks();
});

it("logs out through REST, refreshes, and replaces dashboard history", async () => {
  logout.mockResolvedValue({
    ok: true,
    data: { authenticated: false, user: null, session: null },
  });
  renderWithMessages(<LogoutButton />);

  fireEvent.click(screen.getByRole("button", { name: "Log out" }));

  await waitFor(() => {
    expect(refresh).toHaveBeenCalledTimes(1);
    expect(replace).toHaveBeenCalledWith("/auth/login");
  });
});

it("renders a localized safe failure without navigation", async () => {
  logout.mockResolvedValue({
    ok: false,
    failure: {
      kind: "problem",
      code: "antiforgery_failed",
      status: 400,
      traceId: "trace-logout",
    },
  });
  renderWithMessages(<LogoutButton />);

  fireEvent.click(screen.getByRole("button", { name: "Log out" }));

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Could not log out safely.",
  );
  expect(screen.getByRole("alert")).toHaveTextContent("trace-logout");
  expect(replace).not.toHaveBeenCalled();
});
```

- [ ] **Step 2: Run the focused tests and verify they fail**

Run:

```bash
cd apps/web
npm test -- --runInBand \
  test/components/dashboard-runtime.test.tsx \
  test/components/logout-button.test.tsx
```

Expected: missing module failures for the dashboard/session loader/logout UI.

- [ ] **Step 3: Add the request-bound server session loader**

```ts
// apps/web/src/lib/api/auth/server/load-server-auth-session.ts
import "server-only";

import { loadAuthSession } from "@/src/lib/api/auth/load-auth-session";
import type { AuthSessionResult } from "@/src/lib/api/result";
import { createServerApiClient } from "@/src/lib/api/server/client";
import { readForwardedApiHeaders } from "@/src/lib/api/server/request-headers";

export async function loadServerAuthSession(): Promise<AuthSessionResult> {
  const client = createServerApiClient(await readForwardedApiHeaders());
  if (!client.ok) {
    return { ok: false, failure: client.failure };
  }

  return loadAuthSession(client.client);
}
```

- [ ] **Step 4: Extend both auth bundles with identical dashboard/logout keys**

Add to English:

```json
"dashboard": {
  "eyebrow": "Iteration 3 session proof",
  "title": "Authenticated dashboard",
  "description": "This temporary page proves the PostgreSQL-backed browser session. Product dashboard work remains in iteration 9.",
  "name": "Name",
  "email": "Email",
  "emailVerified": "Email verified",
  "sessionId": "Session ID",
  "expiresAt": "Expires",
  "yes": "Yes",
  "no": "No",
  "loading": "Loading authenticated session"
},
"logout": {
  "button": "Log out",
  "pending": "Logging out",
  "failure": "Could not log out safely.",
  "traceId": "Trace ID: {traceId}"
}
```

Add to Russian:

```json
"dashboard": {
  "eyebrow": "Проверка сессии итерации 3",
  "title": "Защищённый dashboard",
  "description": "Эта временная страница подтверждает браузерную сессию в PostgreSQL. Продуктовый dashboard остаётся в итерации 9.",
  "name": "Имя",
  "email": "Email",
  "emailVerified": "Email подтверждён",
  "sessionId": "Идентификатор сессии",
  "expiresAt": "Истекает",
  "yes": "Да",
  "no": "Нет",
  "loading": "Загружаем аутентифицированную сессию"
},
"logout": {
  "button": "Выйти",
  "pending": "Выходим",
  "failure": "Не удалось безопасно завершить сессию.",
  "traceId": "Идентификатор трассировки: {traceId}"
}
```

- [ ] **Step 5: Implement the Client Component logout**

```tsx
// apps/web/src/components/authentication/logout-button.tsx
"use client";

import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { useState } from "react";

import { Button } from "@/src/components/ui/button";
import { authenticationRoutes } from "@/src/features/authentication/authentication-routes";
import { logoutBrowserSession } from "@/src/lib/api/auth/browser/logout-browser-session";
import { createBrowserApiClient } from "@/src/lib/api/browser/client";
import type { ApiFailure } from "@/src/lib/api/result";

export function LogoutButton() {
  const router = useRouter();
  const t = useTranslations("auth.logout");
  const [pending, setPending] = useState(false);
  const [failure, setFailure] = useState<ApiFailure | null>(null);

  async function executeLogout() {
    setPending(true);
    setFailure(null);
    const result = await logoutBrowserSession(createBrowserApiClient());
    if (!result.ok) {
      setFailure(result.failure);
      setPending(false);
      return;
    }

    router.refresh();
    router.replace(authenticationRoutes.login);
  }

  return (
    <div className="space-y-2">
      {failure ? (
        <div className="text-sm text-destructive" role="alert">
          <p>{t("failure")}</p>
          {failure.kind === "problem" && failure.traceId ? (
            <p className="font-mono text-xs">
              {t("traceId", { traceId: failure.traceId })}
            </p>
          ) : null}
        </div>
      ) : null}
      <Button
        disabled={pending}
        onClick={() => void executeLogout()}
        type="button"
        variant="outline"
      >
        {pending ? t("pending") : t("button")}
      </Button>
    </div>
  );
}
```

- [ ] **Step 6: Implement protected runtime composition**

```tsx
// apps/web/src/components/authentication/dashboard-runtime.tsx
import { redirect } from "next/navigation";
import { connection } from "next/server";
import { getTranslations } from "next-intl/server";

import { AuthApiFailure } from "@/src/components/authentication/auth-api-failure";
import { LogoutButton } from "@/src/components/authentication/logout-button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/src/components/ui/card";
import { authLoginUrl } from "@/src/features/authentication/sanitize-auth-redirect";
import { authenticationRoutes } from "@/src/features/authentication/authentication-routes";
import { loadServerAuthSession } from "@/src/lib/api/auth/server/load-server-auth-session";

export async function DashboardRuntime() {
  await connection();
  const result = await loadServerAuthSession();
  const t = await getTranslations("auth.dashboard");

  if (!result.ok) {
    return <AuthApiFailure failure={result.failure} />;
  }
  if (!result.data.authenticated) {
    redirect(authLoginUrl(authenticationRoutes.dashboard));
  }
  if (!result.data.user || !result.data.session) {
    return (
      <AuthApiFailure failure={{ kind: "network", code: "api_unavailable" }} />
    );
  }

  return (
    <section className="mx-auto w-full max-w-3xl space-y-6 px-4 py-12">
      <div className="space-y-2">
        <p className="text-xs font-medium tracking-[0.2em] text-muted-foreground uppercase">
          {t("eyebrow")}
        </p>
        <h1 className="text-3xl font-semibold tracking-tight">{t("title")}</h1>
        <p className="text-sm text-muted-foreground">{t("description")}</p>
      </div>
      <Card>
        <CardHeader>
          <CardTitle>{result.data.user.name}</CardTitle>
          <CardDescription>{result.data.user.email}</CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-2 text-sm">
            <dt className="text-muted-foreground">{t("name")}</dt>
            <dd>{result.data.user.name}</dd>
            <dt className="text-muted-foreground">{t("email")}</dt>
            <dd>{result.data.user.email}</dd>
            <dt className="text-muted-foreground">{t("emailVerified")}</dt>
            <dd>{result.data.user.emailVerified ? t("yes") : t("no")}</dd>
            <dt className="text-muted-foreground">{t("sessionId")}</dt>
            <dd className="font-mono" data-testid="session-id">
              {result.data.session.id}
            </dd>
            <dt className="text-muted-foreground">{t("expiresAt")}</dt>
            <dd>
              <time dateTime={result.data.session.expiresAt}>
                {result.data.session.expiresAt}
              </time>
            </dd>
          </dl>
          <LogoutButton />
        </CardContent>
      </Card>
    </section>
  );
}
```

```tsx
// apps/web/src/app/(site)/dashboard/page.tsx
import { getTranslations } from "next-intl/server";
import { Suspense } from "react";

import { DashboardRuntime } from "@/src/components/authentication/dashboard-runtime";

export default async function DashboardPage() {
  const t = await getTranslations("auth.dashboard");
  return (
    <Suspense fallback={<p role="status">{t("loading")}</p>}>
      <DashboardRuntime />
    </Suspense>
  );
}
```

```tsx
// apps/web/src/app/(site)/dashboard/loading.tsx
import { getTranslations } from "next-intl/server";

export default async function DashboardLoading() {
  const t = await getTranslations("auth.dashboard");
  return (
    <main className="mx-auto max-w-3xl px-4 py-12">
      <p role="status">{t("loading")}</p>
    </main>
  );
}
```

- [ ] **Step 7: Run all web checks and standalone build**

Run:

```bash
cd apps/web
npm test -- --runInBand
npm run format:check
npm run lint
npm run typecheck
npm run boundaries:check
npm run build
test -f .next/standalone/server.js
```

Expected: dashboard/logout tests and all iteration-2 regressions pass; build
does not contact the API; standalone server exists.

- [ ] **Step 8: Commit**

```bash
cd ../..
git add \
  apps/web/src/app/'(site)'/dashboard \
  apps/web/src/components/authentication \
  apps/web/src/lib/api/auth/server \
  apps/web/src/messages \
  apps/web/test/components
git commit -m "Add protected session dashboard and logout"
```

## Task 14: Run the full multi-session Playwright scenario on Testcontainers

**Files:**

- Create: `apps/api/src/Template.Api/ApiHost.cs`
- Reduce: `apps/api/src/Template.Api/Program.cs` to the entry point calling `ApiHost.Build`
- Create: `apps/api/tests/Template.E2EHost/Template.E2EHost.csproj`
- Create: `apps/api/tests/Template.E2EHost/Program.cs`
- Modify: `Template.sln`
- Modify: `apps/web/playwright.config.ts`
- Create: `apps/web/e2e/support/generated-auth-api.ts`
- Create: `apps/web/e2e/authentication.spec.ts`
- Preserve: `apps/web/e2e/system-status.spec.ts`

**Interfaces:**

- Consumes: the production API composition, exact `postgres:18.4`, EF migrations, generated SDK operations/types, and Playwright shared cookie storage.
- Produces: a test-only network host that owns PostgreSQL lifecycle and a browser acceptance scenario proving persistence, distinct sessions, isolated logout, and all-session cleanup.

- [ ] **Step 1: Write the failing Playwright acceptance scenario**

```ts
// apps/web/e2e/authentication.spec.ts
import { expect, test } from "@playwright/test";

import type { ApiResponseOfLocalAutomationScenarioResponse } from "@/src/lib/api/generated";
import {
  cleanupLocalAutomationUser,
  getGeneratedAuthSession,
  signInLocalAutomationUser,
} from "./support/generated-auth-api";

test("local credentials create persistent independent sessions and cleanup all access", async ({
  browser,
  page,
}) => {
  await page.goto("/");
  await page.getByRole("link", { name: "Get Started" }).click();
  await expect(page).toHaveURL(/\/auth\/login\?redirect=%2Fdashboard$/);
  await expect(
    page.getByRole("button", { name: "Create local automation user" }),
  ).toBeVisible();

  const scenarioResponse = page.waitForResponse((response) => {
    const request = response.request();
    return (
      request.method() === "POST" &&
      new URL(response.url()).pathname === "/api/local-auth/scenario"
    );
  });
  await page
    .getByRole("button", { name: "Create local automation user" })
    .click();
  const scenario = (await (
    await scenarioResponse
  ).json()) as ApiResponseOfLocalAutomationScenarioResponse;

  await expect(page).toHaveURL(/\/dashboard$/);
  const firstSessionId = await page.getByTestId("session-id").textContent();
  expect(firstSessionId).toBeTruthy();
  await expect(page.locator("body")).not.toContainText(scenario.data.password);

  await page.reload();
  await expect(page).toHaveURL(/\/dashboard$/);
  await expect(page.getByTestId("session-id")).toHaveText(firstSessionId!);

  const secondContext = await browser.newContext();
  const secondPage = await secondContext.newPage();
  await signInLocalAutomationUser(
    secondContext.request,
    scenario.data.email,
    scenario.data.password,
  );
  await secondPage.goto("/dashboard");
  const secondSessionId = await secondPage
    .getByTestId("session-id")
    .textContent();
  expect(secondSessionId).toBeTruthy();
  expect(secondSessionId).not.toBe(firstSessionId);

  await page.getByRole("button", { name: "Log out" }).click();
  await expect(page).toHaveURL(/\/auth\/login$/);
  await secondPage.reload();
  await expect(secondPage).toHaveURL(/\/dashboard$/);
  await expect(secondPage.getByTestId("session-id")).toHaveText(
    secondSessionId!,
  );

  await cleanupLocalAutomationUser(secondContext.request);
  expect(
    (await getGeneratedAuthSession(secondContext.request)).authenticated,
  ).toBe(false);
  expect(
    (await getGeneratedAuthSession(page.context().request)).authenticated,
  ).toBe(false);

  await secondPage.goto("/dashboard");
  await expect(secondPage).toHaveURL(/\/auth\/login\?redirect=%2Fdashboard$/);
  await secondContext.close();
});
```

- [ ] **Step 2: Run the new spec and verify the harness is red**

Run:

```bash
cd apps/web
npm run e2e -- authentication.spec.ts
```

Expected: the current API web server never becomes ready because iteration 3
now requires migrated PostgreSQL and the current Playwright harness provides
neither.

- [ ] **Step 3: Extract reusable API construction without changing runtime behavior**

```csharp
// apps/api/src/Template.Api/ApiHost.cs
using Template.Api.Authentication;
using Template.Api.Endpoints;
using Template.Api.Errors;
using Template.Api.Observability;
using Template.Api.OpenApi;
using Template.Application.Authentication;
using Template.Infrastructure.Health;
using Template.Infrastructure.Persistence;

namespace Template.Api;

public static class ApiHost
{
    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddValidation();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddAuthInfrastructure(builder.Configuration);
        builder.Services.AddScoped<LocalAutomationAuthService>();
        builder.Services.AddScoped<BrowserAuthenticationService>();
        builder.Services
            .AddHealthChecks()
            .AddCheck<AuthDatabaseHealthCheck>(
                "postgres-auth-schema",
                tags: ["ready"],
                timeout: TimeSpan.FromSeconds(2));
        builder.Services.AddApiAuthentication();
        builder.Services.AddApiAuthSecurity(builder.Configuration);
        builder.Services.AddApiErrorHandling();
        builder.Services.AddApiOpenApi();
        builder.Services.AddEndpointModules();

        var app = builder.Build();

        app.UseWhen(
            context => context.Request.Path.StartsWithSegments("/api"),
            api =>
            {
                api.UseMiddleware<CorrelationIdMiddleware>();
                api.UseExceptionHandler();
                api.UseStatusCodePages();
                api.UseMiddleware<RequestLoggingMiddleware>();
                api.UseMiddleware<AuthResponseCacheMiddleware>();
                api.UseMiddleware<LocalAutomationAvailabilityMiddleware>();
            });

        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.MapEndpointModules();

        if (app.Environment.IsDevelopment() ||
            app.Environment.IsEnvironment("Test"))
        {
            app.MapOpenApi("/api/openapi/{documentName}.json").AllowAnonymous();
        }

        return app;
    }
}
```

Replace `Program.cs` with:

```csharp
var app = Template.Api.ApiHost.Build(args);
app.Run();

public partial class Program;
```

Run:

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj --no-restore
```

Expected: all API tests still pass, proving the extraction changed no behavior.

- [ ] **Step 4: Add the test-only PostgreSQL/API network host**

```xml
<!-- apps/api/tests/Template.E2EHost/Template.E2EHost.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Testcontainers.PostgreSql" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Template.Api\Template.Api.csproj" />
    <ProjectReference Include="..\..\src\Template.Infrastructure\Template.Infrastructure.csproj" />
  </ItemGroup>
</Project>
```

```csharp
// apps/api/tests/Template.E2EHost/Program.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Template.Api;
using Template.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

await using var postgres = new PostgreSqlBuilder()
    .WithImage("postgres:18.4")
    .WithDatabase("template_e2e")
    .WithUsername("postgres")
    .WithPassword("postgres")
    .Build();
await postgres.StartAsync();

Environment.SetEnvironmentVariable(
    "ConnectionStrings__Postgres",
    postgres.GetConnectionString());
Environment.SetEnvironmentVariable(
    "LocalAutomationAuth__Enabled",
    "true");

await using var app = ApiHost.Build(args);
await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<AuthDbContext>()
        .Database.MigrateAsync();
}

await app.RunAsync();
```

Add the project:

```bash
dotnet sln Template.sln add \
  apps/api/tests/Template.E2EHost/Template.E2EHost.csproj \
  --solution-folder tests
```

This host is test-only. It is the only executable that migrates automatically;
`Template.Api` itself remains migration-free.

- [ ] **Step 5: Point Playwright at the test host**

Replace the API `webServer` command in `playwright.config.ts`:

```ts
{
  command:
    "dotnet run --no-launch-profile --project ../api/tests/Template.E2EHost/Template.E2EHost.csproj",
  env: {
    ASPNETCORE_ENVIRONMENT: "Test",
    ASPNETCORE_URLS: apiOrigin,
    LocalAutomationAuth__Enabled: "true",
  },
  reuseExistingServer: false,
  timeout: 180_000,
  url: `${apiOrigin}/api/health/ready`,
}
```

Keep the Next.js server configuration and same-origin rewrite unchanged. Never
reuse the API process for this database-destructive E2E suite.

- [ ] **Step 6: Implement a generated SDK client over Playwright request storage**

```ts
// apps/web/e2e/support/generated-auth-api.ts
import type { APIRequestContext } from "@playwright/test";

import {
  deleteLocalAutomationScenario,
  getAuthCsrf,
  getAuthSession,
  signInLocalAutomation,
} from "../../src/lib/api/generated";
import { createClient, type Client } from "../../src/lib/api/generated/client";

const webOrigin = "http://127.0.0.1:3127";

function createPlaywrightFetch(request: APIRequestContext): typeof fetch {
  return async (input, init) => {
    const source = input instanceof Request ? input : new Request(input, init);
    const headers: Record<string, string> = {};
    source.headers.forEach((value, name) => {
      headers[name] = value;
    });
    const body =
      source.method === "GET" || source.method === "HEAD"
        ? undefined
        : Buffer.from(await source.arrayBuffer());
    const response = await request.fetch(source.url, {
      data: body,
      failOnStatusCode: false,
      headers,
      method: source.method,
    });
    const responseHeaders = new Headers();
    for (const header of response.headersArray()) {
      responseHeaders.append(header.name, header.value);
    }
    return new Response(await response.body(), {
      headers: responseHeaders,
      status: response.status(),
    });
  };
}

function clientFor(request: APIRequestContext): Client {
  return createClient({
    baseUrl: webOrigin,
    fetch: createPlaywrightFetch(request),
  });
}

async function csrf(client: Client): Promise<string> {
  const result = await getAuthCsrf({ client });
  if (!result.data) {
    throw new Error(
      `CSRF request failed with ${result.response?.status ?? 0}.`,
    );
  }
  return result.data.data.requestToken;
}

export async function signInLocalAutomationUser(
  request: APIRequestContext,
  email: string,
  password: string,
) {
  const client = clientFor(request);
  const result = await signInLocalAutomation({
    client,
    body: { email, password },
    headers: { "X-CSRF-TOKEN": await csrf(client) },
  });
  if (!result.data) {
    throw new Error(
      `Local credential sign-in failed with ${result.response?.status ?? 0}.`,
    );
  }
  return result.data.data;
}

export async function cleanupLocalAutomationUser(request: APIRequestContext) {
  const client = clientFor(request);
  const result = await deleteLocalAutomationScenario({
    client,
    headers: { "X-CSRF-TOKEN": await csrf(client) },
  });
  if (!result.data) {
    throw new Error(
      `Local cleanup failed with ${result.response?.status ?? 0}.`,
    );
  }
  return result.data.data;
}

export async function getGeneratedAuthSession(request: APIRequestContext) {
  const result = await getAuthSession({ client: clientFor(request) });
  if (!result.data) {
    throw new Error(
      `Session lookup failed with ${result.response?.status ?? 0}.`,
    );
  }
  return result.data.data;
}
```

- [ ] **Step 7: Run the focused E2E scenario**

Run:

```bash
cd apps/web
npm run e2e:install
npm run e2e -- authentication.spec.ts
```

Expected: the complete ten-step scenario passes with distinct UUID session IDs,
one-context logout isolation, cleanup invalidation in both contexts, no visible
plaintext password, and protected redirect back to login.

- [ ] **Step 8: Run the full Playwright suite**

Run:

```bash
cd apps/web
npm run e2e
```

Expected: authentication and existing system-status/theme/error scenarios all
pass against the same Testcontainers-backed API and same-origin Next rewrite.

- [ ] **Step 9: Commit**

```bash
cd ../..
git add \
  Template.sln \
  apps/api/src/Template.Api/ApiHost.cs \
  apps/api/src/Template.Api/Program.cs \
  apps/api/tests/Template.E2EHost \
  apps/web/playwright.config.ts \
  apps/web/e2e
git commit -m "Add persistent authentication E2E coverage"
```

## Task 15: Document operations, record acceptance evidence, and verify the branch

**Files:**

- Create: `docs/authentication-persistence-operations.md`
- Modify: `docs/api-conventions.md`
- Modify: `docs/web-conventions.md`
- Modify: `docs/aspnetcore-migration-plan.md`

**Interfaces:**

- Consumes: the completed API/data/web/E2E behavior and literal command output from this task.
- Produces: durable operator/developer guidance, an iteration-3 register entry, exact acceptance evidence, intentional differences, and the gate for iteration 4.

- [ ] **Step 1: Add the persistence/auth operations guide**

Create `docs/authentication-persistence-operations.md` with these exact
sections and commands:

````markdown
# Authentication and persistence operations

## Scope

Iteration 3 owns the clean PostgreSQL `auth` schema, Identity Core credential
records, PostgreSQL-backed browser tickets, the secure session cookie,
antiforgery, local automation auth, and database readiness. It does not migrate
Prisma/Better Auth data and does not provide production password or social
login.

## Configuration

Runtime database configuration uses `ConnectionStrings:Postgres`; the
environment form is `ConnectionStrings__Postgres`. Keep passwords in
environment variables or .NET user-secrets, never committed appsettings.

Local automation requires both `Development`/`Test` and
`LocalAutomationAuth__Enabled=true`. Production returns
`404 local_auth_disabled` even if that flag is accidentally true.

The default local limits are:

- `LocalAutomationAuth__CreateRateLimitPerMinute=20`
- `LocalAutomationAuth__SignInRateLimitPerFiveMinutes=10`

## Apply and inspect migrations

The API never applies migrations automatically.

```bash
dotnet tool restore
dotnet ef database update \
  --project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --startup-project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --context AuthDbContext

dotnet ef migrations has-pending-model-changes \
  --project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --startup-project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --context AuthDbContext

dotnet ef migrations script --idempotent \
  --project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --startup-project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --context AuthDbContext
```

Integration tests and `Template.E2EHost` apply migrations only to disposable
PostgreSQL 18.4 databases created by Testcontainers.

## Local sign-in

1. Configure and migrate PostgreSQL.
2. Set `LocalAutomationAuth__Enabled=true`.
3. Start API and Next.js with the existing same-origin development rewrite.
4. Open `/auth/login` and choose **Create local automation user**.

Automation clients call `GET /api/v1/auth/csrf` before every unsafe request.
Scenario creation returns the generated password once. The visible UI discards
it after navigation; test helpers may use it for a second session.

## Session and CSRF safety

The browser session cookie is `__Host-template.session`: HttpOnly, Secure,
SameSite Lax, Path `/`, no Domain, persistent seven-day sliding expiration.
The cookie contains only a Data-Protection-protected opaque ticket-store key.
PostgreSQL stores only its SHA-256 hash and a separately protected ticket.

The antiforgery cookie is `__Host-template.antiforgery`: HttpOnly, Secure,
SameSite Strict, Path `/`, no Domain. Send its paired request token in
`X-CSRF-TOKEN` for scenario creation, credential sign-in, logout, and cleanup.

## Health and failure diagnosis

`/api/health/live` does not touch PostgreSQL. `/api/health` and
`/api/health/ready` require connectivity and a queryable `auth.users` relation.
Health responses never expose connection strings or schema errors.

Auth responses are never cached. Diagnose failures by stable Problem Details
`code` and `traceId`; do not log or display passwords, cookies, ticket data, or
backend `detail`.

## Rollback and production gate

The initial migration is additive relative to iterations 0–2. Rolling
application code back may leave schema `auth` in place. The generated `Down`
path is destructive and is restricted to disposable development/test
databases; production uses restore or forward-fix procedures.

Production deployment remains blocked until external OAuth behavior and
persistent/encrypted Data Protection key storage are designed. API-key
`x-api-key` support remains iteration 7, and no Bearer scheme is registered.
````

- [ ] **Step 2: Update API and web conventions with the implemented contract**

In `docs/api-conventions.md`:

- replace iteration-1/iteration-3 future tense with implemented
  `Api.BrowserSession`, PostgreSQL `ITicketStore`, seven-day sliding cookie,
  anonymous `200` session projection, CSRF cookie/header, local two-part gate,
  exact local rate limits, and no CORS;
- list the six new stable codes:
  `antiforgery_failed`, `local_auth_invalid_credentials`,
  `local_auth_user_required`, `local_auth_disabled`,
  `local_auth_user_exists`, `rate_limited`;
- state that local operations carry `local-only` and `x-local-only: true`;
- state that only `cookieAuth` is advertised and API-key/Bearer schemes are
  absent until their own iterations;
- add database readiness behavior and explicit migration ownership.

In `docs/web-conventions.md`:

- document request-time auth loading below `connection()`/Suspense;
- document that SSR forwards only Cookie/correlation ID and treats API failure
  differently from anonymous;
- document CSRF-first browser mutations through generated SDK operations;
- document redirect rejection for full URLs, `//`, `/api/**`, and `/auth/**`;
- document that `/auth/login` has no manual credential fields and the local
  button discards returned plaintext credentials;
- document temporary `/dashboard` as session proof, not product dashboard.

- [ ] **Step 3: Run the complete .NET and migration acceptance matrix**

Run from repository root:

```bash
dotnet tool restore
dotnet restore Template.sln
dotnet build Template.sln --no-restore
dotnet test Template.sln --no-restore

dotnet build apps/api/src/Template.Api/Template.Api.csproj \
  --no-restore \
  -p:OpenApiGenerateDocuments=true
git diff --exit-code -- contracts/openapi/v1.json

dotnet ef migrations has-pending-model-changes \
  --project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --startup-project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --context AuthDbContext

dotnet ef migrations script --idempotent \
  --project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --startup-project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --context AuthDbContext \
  --output /tmp/template-auth-idempotent.sql
test -s /tmp/template-auth-idempotent.sql
```

Expected: every command exits `0`, build has zero warnings/errors, all
Application/API tests pass against PostgreSQL 18.4, OpenAPI is unchanged after
the second export, model drift is empty, and idempotent SQL is non-empty.

- [ ] **Step 4: Run the complete web/contract/E2E acceptance matrix**

Run:

```bash
cd apps/web
npm ci
npm audit --json
npm run audit:prod
npm run api:check
npm run boundaries:check
npm run format:check
npm run lint
npm run typecheck
npm test -- --runInBand
rm -rf .next
env -u API_INTERNAL_BASE_URL -u API_PROXY_TARGET \
  PUBLIC_DEFAULT_LOCALE=en \
  npm run build
test -f .next/standalone/server.js
npm run e2e:install
npm run e2e
cd ../..
```

Expected: dependency audits have zero findings, SDK regeneration is byte-clean,
all source guards/tests/checks pass, production build succeeds without API/DB,
standalone output exists, and all Playwright cases pass.

- [ ] **Step 5: Run repository and immutable-reference guards**

Run:

```bash
git diff --check
git diff --exit-code -- template/
git diff --exit-code origin/main...HEAD -- template/
git status --short
```

Expected: no whitespace errors, both template diffs are empty, and status shows
only intentional iteration-3 files.

- [ ] **Step 6: Update the migration register and acceptance evidence**

Change the document header to:

```markdown
**Текущая итерация:** 3 — persistence, Identity и базовая аутентификация (завершена 2026-07-24).
```

In the iteration-3 section, replace its scope/outcome/deferred lines with:

```markdown
**Состав:** PostgreSQL 18.4, EF Core migration, ASP.NET Core Identity Core, чистая схема пользователя/сессии, persistent `ITicketStore`, current-session/logout REST, secure HttpOnly same-origin cookie, explicit CSRF, local-only automation scenario/sign-in/cleanup, rate limits/lockout, database readiness, OpenAPI/generated SDK и login/dashboard UI.

**Вход:** итерации 1–2; `ConnectionStrings:Postgres` и environment/user-secrets conventions.
**Выход:** в opted-in Development/Test одна кнопка создаёт local credential user и persistent browser session; credentials позволяют automation-вход во вторую независимую сессию; logout/cleanup/current-session работают только через REST; Production password auth недоступен.
**Отложено:** внешний OAuth и account/session management — итерация 4; API keys/`x-api-key` — итерация 7; реальный Bearer требует отдельного issuer/consumer contract.
```

Replace the combined `3–12` register row with:

```markdown
| 3 — persistence, Identity и базовая аутентификация | Завершена | PostgreSQL 18.4, EF migration, Identity Core, persistent cookie sessions, CSRF, local credential automation, login/dashboard/logout REST slice приняты. |
| 4–12 | Не начаты | Следующий dependency gate — внешний OAuth/accounts; API keys и `x-api-key` остаются итерацией 7. |
```

Append `## Acceptance evidence: итерация 3` containing:

1. The correspondence table from section 5 of the approved design.
2. Scope listing Domain/Application/Infrastructure/Api, OpenAPI/generated SDK,
   login/dashboard UI, Testcontainers/E2E, and documentation.
3. A command table containing every command from Steps 3–5, the literal
   observed PASS status, the numeric test totals printed by .NET/Jest/Playwright,
   PostgreSQL image `18.4`, and zero warning/error/audit counts.
4. The exact intentional differences:
   RFC Problem Details/envelope, no production password login, social login
   deferred to iteration 4, clean Identity schema, temporary dashboard,
   zero organizations on cleanup, no session JWT cache, no API-key/Bearer
   runtime.
5. The next gate: provider priority/credentials/callbacks, provider
   email-verification mapping, production Data Protection key persistence, and
   iteration-4 account/session-management scope.
6. Explicit evidence that both template diff commands were empty.

Do not mark iteration 3 complete in the document if any command in Steps 3–5
failed.

- [ ] **Step 7: Format documentation and rerun drift guards**

Run:

```bash
cd apps/web
npx prettier --check \
  ../../docs/authentication-persistence-operations.md \
  ../../docs/api-conventions.md \
  ../../docs/web-conventions.md \
  ../../docs/aspnetcore-migration-plan.md
cd ../..
git diff --check
git diff --exit-code -- template/
```

Expected: documentation formatting and both diff guards pass.

- [ ] **Step 8: Invoke verification-before-completion and review the final diff**

Use the `superpowers:verification-before-completion` skill. Then run:

```bash
git diff --stat origin/main...HEAD
git diff --name-status origin/main...HEAD
git log --oneline --decorate origin/main..HEAD
```

Verify every changed file belongs to iteration 3, no migration/reference
artifact is missing, generated code is not hand-edited, no OpenSpec change was
created, and no iteration-4/5/7 product domain was pulled forward.

- [ ] **Step 9: Commit the final documentation/evidence**

```bash
git add \
  docs/authentication-persistence-operations.md \
  docs/api-conventions.md \
  docs/web-conventions.md \
  docs/aspnetcore-migration-plan.md
git commit -m "Complete iteration 3 migration evidence"
```

## Final acceptance checklist

- [ ] All seven REST operations match the approved method/path/auth/CSRF/status contracts.
- [ ] Local email/password works in opted-in Development/Test and is impossible in Production.
- [ ] The visible login has one local automation button and no credential form; generated credentials support automation/second-session checks.
- [ ] PostgreSQL is the revocation source of truth and cookie logout removes only the current session.
- [ ] Cleanup deletes the local user and every session atomically.
- [ ] Cookie, antiforgery, rate-limit, lockout, validation, cache, and Problem Details assertions pass.
- [ ] OpenAPI exposes only implemented cookie security and local-only metadata; generated SDK is current.
- [ ] `/dashboard` distinguishes anonymous state from API failure and never renders secrets.
- [ ] .NET, contract, web, standalone, and Playwright matrices pass from clean installs/build output.
- [ ] Migration register/evidence and operations docs reflect observed results.
- [ ] `template/` has no changes in working-tree or branch-range diffs.
