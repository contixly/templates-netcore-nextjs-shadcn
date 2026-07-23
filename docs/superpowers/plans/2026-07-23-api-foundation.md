# API Foundation Iteration 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete migration iteration 1 by turning the .NET 10 host into a tested, documented REST foundation with stable success/error contracts, cookie authentication boundaries, observability, health probes, and a committed OpenAPI 3.1 contract.

**Architecture:** Keep `Program.cs` as the composition root and put HTTP-only concerns in focused `Template.Api` modules: `Authentication`, `Endpoints`, `Errors`, `Observability`, `OpenApi`, `Features/Health`, and `Features/System`. Production endpoint modules are explicitly registered through DI; the shared `WebApplicationFactory` replaces authentication and adds excluded test-only probes. `Template.Domain`, `Template.Application`, `Template.Infrastructure`, and `apps/web` remain unchanged because this iteration contains no business use cases, persistence, or UI.

**Tech Stack:** .NET SDK 10.0.302, ASP.NET Core 10.0.10 Minimal APIs, built-in validation and Problem Details, cookie authentication, `Microsoft.AspNetCore.OpenApi` 10.0.10, `Microsoft.Extensions.ApiDescription.Server` 10.0.10, `Microsoft.OpenApi` 2.11.0, xUnit v3 3.2.2, and `Microsoft.AspNetCore.Mvc.Testing` 10.0.10.

## Global Constraints

- Read `AGENTS.md` and `docs/aspnetcore-migration-plan.md` before each execution session.
- Treat `template/` as immutable: read and compare only; never edit, format, move, delete, or run migrations inside it.
- Preserve dependency direction `Domain → Application → Infrastructure → Api`; do not add HTTP foundation code to Domain, Application, or Infrastructure.
- ASP.NET Core owns `/api/**`; `apps/web` must not receive Prisma, Better Auth, Server Actions, or direct database access.
- Successful JSON uses `{ "data": ... }`; errors use RFC Problem Details with stable `code`, `traceId`, and field-level `errors`.
- Browser authentication uses secure HttpOnly same-origin cookie `__Host-template.session`; never store bearer tokens in browser storage.
- Consumer routes use URL version `/api/v1`; operational `/api/health*` routes remain unversioned.
- Validate and authorize at the HTTP boundary; this iteration adds no business rules, transactions, database schema, pagination, or filtering.
- Runtime OpenAPI is exposed only in `Development` and `Test`; the canonical OpenAPI 3.1 artifact is `contracts/openapi/v1.json`.
- Do not add Identity, EF Core, PostgreSQL, OAuth, API keys, antiforgery, YARP, Aspire, UI code, or an active OpenSpec change.
- Use test-first cycles: add a failing focused test, observe the expected failure, add minimum implementation, rerun the focused test, then run the broader suite.
- Record durable forward-looking decisions in the appropriate file under `docs/` in the same change.

---

## File Structure

### Production files

| Path | Responsibility |
| --- | --- |
| `Directory.Packages.props` | Central versions for first-party OpenAPI generation packages. |
| `apps/api/src/Template.Api/Template.Api.csproj` | Validation interceptor namespace, OpenAPI package references, deterministic build-time export target. |
| `apps/api/src/Template.Api/Program.cs` | Composition root and ordered `/api/**` middleware pipeline only. |
| `apps/api/src/Template.Api/Properties/AssemblyInfo.cs` | Grants the integration-test assembly access to internal HTTP composition types. |
| `apps/api/src/Template.Api/Contracts/ApiResponse.cs` | Generic `{ data }` success envelope. |
| `apps/api/src/Template.Api/Endpoints/IEndpointModule.cs` | Contract implemented by production and test-only endpoint modules. |
| `apps/api/src/Template.Api/Endpoints/EndpointModuleExtensions.cs` | Explicit production module registration and deterministic mapping. |
| `apps/api/src/Template.Api/Authentication/ApiAuthenticationDefaults.cs` | Cookie scheme and cookie name constants. |
| `apps/api/src/Template.Api/Authentication/ApiPolicies.cs` | Named authenticated-user policy. |
| `apps/api/src/Template.Api/Authentication/AuthenticationServiceCollectionExtensions.cs` | Secure cookie handler and 401/403 API behavior. |
| `apps/api/src/Template.Api/Errors/ApiProblemCodes.cs` | Stable application error codes. |
| `apps/api/src/Template.Api/Errors/ApiProblemDetailsDefaults.cs` | Status-to-contract mapping and Problem Details normalization. |
| `apps/api/src/Template.Api/Errors/ApiExceptionHandler.cs` | Safe conversion of binding/unhandled exceptions to Problem Details. |
| `apps/api/src/Template.Api/Errors/ErrorHandlingServiceCollectionExtensions.cs` | Registers Problem Details and the exception handler. |
| `apps/api/src/Template.Api/Observability/CorrelationIdMiddleware.cs` | Validates/creates one trace identifier, response header, and logging scope. |
| `apps/api/src/Template.Api/Observability/RequestLoggingMiddleware.cs` | Structured completion log without query/body/credential values. |
| `apps/api/src/Template.Api/Features/System/SystemEndpointModule.cs` | Anonymous status and protected authenticated probes under `/api/v1`. |
| `apps/api/src/Template.Api/Features/System/SystemResponses.cs` | Typed system response DTOs. |
| `apps/api/src/Template.Api/Features/Health/HealthEndpointModule.cs` | Compatibility, liveness, and readiness probes. |
| `apps/api/src/Template.Api/Features/Health/HealthResponse.cs` | Typed health response DTO. |
| `apps/api/src/Template.Api/OpenApi/OpenApiDefaults.cs` | Document/security scheme constants. |
| `apps/api/src/Template.Api/OpenApi/OpenApiServiceCollectionExtensions.cs` | OpenAPI 3.1 document and protected-operation security transformers. |
| `apps/api/src/Template.Api/OpenApi/OpenApiEndpointConventionExtensions.cs` | Reusable response metadata for standard Problem Details. |
| `contracts/openapi/v1.json` | Committed canonical OpenAPI 3.1 contract. |
| `docs/api-conventions.md` | Durable REST, validation, auth/session, health, observability, and export rules. |
| `docs/aspnetcore-migration-plan.md` | Iteration scope, status, acceptance evidence, gaps, and next gate. |

### Test files

| Path | Responsibility |
| --- | --- |
| `apps/api/tests/Template.Api.Tests/Infrastructure/ApiWebApplicationFactory.cs` | Shared `Test` host, test authentication, test-only modules, and captured logging. |
| `apps/api/tests/Template.Api.Tests/Infrastructure/TestAuthenticationHandler.cs` | Authenticates only requests carrying `X-Test-User`. |
| `apps/api/tests/Template.Api.Tests/Infrastructure/TestEndpointModule.cs` | Excluded fault and deny-policy endpoints that never exist in production. |
| `apps/api/tests/Template.Api.Tests/Infrastructure/CapturedLogProvider.cs` | Thread-safe log/scope capture for observability assertions. |
| `apps/api/tests/Template.Api.Tests/SystemEndpointTests.cs` | Public/protected success and validation scenarios. |
| `apps/api/tests/Template.Api.Tests/ProblemDetailsTests.cs` | 400/401/403/404/405/500 error contract scenarios. |
| `apps/api/tests/Template.Api.Tests/HealthEndpointTests.cs` | Healthy/unhealthy readiness and liveness semantics. |
| `apps/api/tests/Template.Api.Tests/ObservabilityTests.cs` | Correlation propagation, trace scope, levels, and sensitive-value exclusion. |
| `apps/api/tests/Template.Api.Tests/OpenApiContractTests.cs` | Runtime document shape, cookie security, and semantic drift gate. |

### Files that must remain untouched

- Every path under `template/`.
- Every source file under `apps/api/src/Template.Domain/`.
- Every source file under `apps/api/src/Template.Application/`.
- Every source file under `apps/api/src/Template.Infrastructure/`.
- `apps/web/`.

---

### Task 1: Explicit Endpoint Composition and Cookie Authentication Boundary

**Files:**
- Create: `apps/api/src/Template.Api/Properties/AssemblyInfo.cs`
- Create: `apps/api/src/Template.Api/Contracts/ApiResponse.cs`
- Create: `apps/api/src/Template.Api/Endpoints/IEndpointModule.cs`
- Create: `apps/api/src/Template.Api/Endpoints/EndpointModuleExtensions.cs`
- Create: `apps/api/src/Template.Api/Authentication/ApiAuthenticationDefaults.cs`
- Create: `apps/api/src/Template.Api/Authentication/ApiPolicies.cs`
- Create: `apps/api/src/Template.Api/Authentication/AuthenticationServiceCollectionExtensions.cs`
- Create: `apps/api/src/Template.Api/Features/System/SystemResponses.cs`
- Create: `apps/api/src/Template.Api/Features/System/SystemEndpointModule.cs`
- Create: `apps/api/tests/Template.Api.Tests/Infrastructure/ApiWebApplicationFactory.cs`
- Create: `apps/api/tests/Template.Api.Tests/Infrastructure/TestAuthenticationHandler.cs`
- Create: `apps/api/tests/Template.Api.Tests/Infrastructure/TestEndpointModule.cs`
- Create: `apps/api/tests/Template.Api.Tests/SystemEndpointTests.cs`
- Modify: `apps/api/src/Template.Api/Template.Api.csproj`
- Modify: `apps/api/src/Template.Api/Program.cs`

**Interfaces:**
- Consumes: ASP.NET Core `IEndpointRouteBuilder`, cookie authentication, authorization policies, `TimeProvider`.
- Produces: `ApiResponse<T>(T Data)`, `IEndpointModule.MapEndpoints(IEndpointRouteBuilder)`, `AddApiAuthentication(IServiceCollection)`, `AddEndpointModules(IServiceCollection)`, `MapEndpointModules(IEndpointRouteBuilder)`, policy `ApiPolicies.Authenticated`, test header `X-Test-User`.

- [ ] **Step 1: Add failing HTTP-boundary and cookie configuration tests**

Create `SystemEndpointTests.cs` with these initial cases:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Template.Api.Authentication;
using Template.Api.Tests.Infrastructure;

namespace Template.Api.Tests;

public sealed class SystemEndpointTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task PublicStatusReturnsTypedDataEnvelope()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/api/v1/system/status?echo=hello",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SystemStatusEnvelope>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(payload);
        Assert.Equal("ok", payload.Data.Status);
        Assert.Equal("1", payload.Data.ApiVersion);
        Assert.Equal("hello", payload.Data.Echo);
        Assert.NotEqual(default, payload.Data.Timestamp);
    }

    [Fact]
    public async Task ProtectedProbeRejectsAnonymousRequest()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/api/v1/system/authenticated",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedProbeAcceptsTestAuthenticatedRequest()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.UserHeaderName, "user-1");

        using var response = await client.GetAsync(
            "/api/v1/system/authenticated",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AuthenticatedEnvelope>(
            TestContext.Current.CancellationToken);
        Assert.Equal("authenticated", payload!.Data.Status);
    }

    [Fact]
    public void ProductionCookieUsesHostPrefixSecurityRequirements()
    {
        using var scope = factory.Services.CreateScope();
        var options = scope.ServiceProvider
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(ApiAuthenticationDefaults.SchemeName);

        Assert.Equal("__Host-template.session", options.Cookie.Name);
        Assert.True(options.Cookie.HttpOnly);
        Assert.Equal(CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
        Assert.Equal(SameSiteMode.Lax, options.Cookie.SameSite);
        Assert.Equal("/", options.Cookie.Path);
        Assert.Null(options.Cookie.Domain);
    }

    private sealed record SystemStatusEnvelope(SystemStatusData Data);
    private sealed record SystemStatusData(
        string Status,
        string ApiVersion,
        DateTimeOffset Timestamp,
        string? Echo);
    private sealed record AuthenticatedEnvelope(AuthenticatedData Data);
    private sealed record AuthenticatedData(string Status);
}
```

Create the test infrastructure using these exact public test interfaces:

```csharp
// Infrastructure/TestAuthenticationHandler.cs
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Template.Api.Tests.Infrastructure;

internal sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    internal const string SchemeName = "Test";
    internal const string UserHeaderName = "X-Test-User";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeaderName, out var userId) ||
            string.IsNullOrWhiteSpace(userId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
            SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
```

```csharp
// Infrastructure/ApiWebApplicationFactory.cs
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Template.Api.Endpoints;

namespace Template.Api.Tests.Infrastructure;

public sealed class ApiWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
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
        });
    }
}
```

Create `TestEndpointModule.cs` initially with only the deny-policy route:

```csharp
using Template.Api.Endpoints;

namespace Template.Api.Tests.Infrastructure;

internal sealed class TestEndpointModule : IEndpointModule
{
    internal const string ForbiddenPolicy = "Test.Forbidden";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/testing/forbidden", () => Results.Ok())
            .RequireAuthorization(ForbiddenPolicy)
            .ExcludeFromDescription();
    }
}
```

- [ ] **Step 2: Run the focused tests and verify the red state**

Run:

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --filter FullyQualifiedName~SystemEndpointTests
```

Expected: build/test failure because `Template.Api.Authentication`, `Template.Api.Endpoints`, the system routes, and their contracts do not exist.

- [ ] **Step 3: Add the minimal endpoint, envelope, and authentication implementation**

Add internal visibility and the success envelope:

```csharp
// Properties/AssemblyInfo.cs
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Template.Api.Tests")]
```

```csharp
// Contracts/ApiResponse.cs
namespace Template.Api.Contracts;

internal sealed record ApiResponse<T>(T Data);
```

Add deterministic endpoint-module registration:

```csharp
// Endpoints/IEndpointModule.cs
namespace Template.Api.Endpoints;

internal interface IEndpointModule
{
    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
```

```csharp
// Endpoints/EndpointModuleExtensions.cs
using Template.Api.Features.System;

namespace Template.Api.Endpoints;

internal static class EndpointModuleExtensions
{
    internal static IServiceCollection AddEndpointModules(this IServiceCollection services)
    {
        services.AddSingleton<IEndpointModule, SystemEndpointModule>();
        return services;
    }

    internal static IEndpointRouteBuilder MapEndpointModules(
        this IEndpointRouteBuilder endpoints)
    {
        foreach (var module in endpoints.ServiceProvider
                     .GetRequiredService<IEnumerable<IEndpointModule>>())
        {
            module.MapEndpoints(endpoints);
        }

        return endpoints;
    }
}
```

Add the cookie scheme and policy:

```csharp
// Authentication/ApiAuthenticationDefaults.cs
namespace Template.Api.Authentication;

internal static class ApiAuthenticationDefaults
{
    internal const string SchemeName = "Template.Session";
    internal const string CookieName = "__Host-template.session";
}
```

```csharp
// Authentication/ApiPolicies.cs
namespace Template.Api.Authentication;

internal static class ApiPolicies
{
    internal const string Authenticated = "Api.Authenticated";
}
```

```csharp
// Authentication/AuthenticationServiceCollectionExtensions.cs
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Template.Api.Authentication;

internal static class AuthenticationServiceCollectionExtensions
{
    internal static IServiceCollection AddApiAuthentication(this IServiceCollection services)
    {
        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = ApiAuthenticationDefaults.SchemeName;
                options.DefaultChallengeScheme = ApiAuthenticationDefaults.SchemeName;
                options.DefaultForbidScheme = ApiAuthenticationDefaults.SchemeName;
            })
            .AddCookie(ApiAuthenticationDefaults.SchemeName, options =>
            {
                options.Cookie.Name = ApiAuthenticationDefaults.CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.Path = "/";
                options.Cookie.Domain = null;
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });

        services.AddAuthorization(options =>
            options.AddPolicy(
                ApiPolicies.Authenticated,
                policy => policy.RequireAuthenticatedUser()));

        return services;
    }
}
```

Add typed system responses and routes:

```csharp
// Features/System/SystemResponses.cs
namespace Template.Api.Features.System;

internal sealed record SystemStatusResponse(
    string Status,
    string ApiVersion,
    DateTimeOffset Timestamp,
    string? Echo);

internal sealed record AuthenticatedResponse(string Status);
```

```csharp
// Features/System/SystemEndpointModule.cs
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Template.Api.Authentication;
using Template.Api.Contracts;
using Template.Api.Endpoints;

namespace Template.Api.Features.System;

internal sealed class SystemEndpointModule : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/system")
            .RequireAuthorization(ApiPolicies.Authenticated);

        group.MapGet(
                "/status",
                ([FromQuery, StringLength(
                    64,
                    MinimumLength = 1,
                    ErrorMessage = "The field echo must be between 1 and 64 characters.")]
                    string? echo,
                    TimeProvider timeProvider) =>
                    TypedResults.Ok(new ApiResponse<SystemStatusResponse>(
                        new SystemStatusResponse(
                            "ok",
                            "1",
                            timeProvider.GetUtcNow(),
                            echo))))
            .AllowAnonymous()
            .WithName("GetSystemStatus");

        group.MapGet(
                "/authenticated",
                () => TypedResults.Ok(new ApiResponse<AuthenticatedResponse>(
                    new AuthenticatedResponse("authenticated"))))
            .WithName("GetAuthenticatedStatus");
    }
}
```

Enable generated Minimal API validation interceptors in `Template.Api.csproj`:

```xml
<PropertyGroup>
  <InterceptorsNamespaces>$(InterceptorsNamespaces);Microsoft.AspNetCore.Http.Validation.Generated</InterceptorsNamespaces>
</PropertyGroup>
```

Replace `Program.cs` with the composition root:

```csharp
using Template.Api.Authentication;
using Template.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddValidation();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHealthChecks();
builder.Services.AddApiAuthentication();
builder.Services.AddEndpointModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/api/health").AllowAnonymous();
app.MapEndpointModules();

app.Run();

public partial class Program;
```

- [ ] **Step 4: Run focused and solution tests**

Run:

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --filter FullyQualifiedName~SystemEndpointTests
dotnet test Template.sln
```

Expected: all Task 1 tests and the bootstrap health test pass. The temporary
`MapHealthChecks("/api/health")` compatibility mapping remains until Task 3
replaces it with the typed health module.

- [ ] **Step 5: Commit the HTTP composition boundary**

```bash
git add \
  apps/api/src/Template.Api \
  apps/api/tests/Template.Api.Tests/Infrastructure \
  apps/api/tests/Template.Api.Tests/SystemEndpointTests.cs
git commit -m "feat(api): add endpoint modules and cookie auth boundary"
```

---

### Task 2: Stable Problem Details, Validation, and Failure Contracts

**Files:**
- Create: `apps/api/src/Template.Api/Errors/ApiProblemCodes.cs`
- Create: `apps/api/src/Template.Api/Errors/ApiProblemDetailsDefaults.cs`
- Create: `apps/api/src/Template.Api/Errors/ApiExceptionHandler.cs`
- Create: `apps/api/src/Template.Api/Errors/ErrorHandlingServiceCollectionExtensions.cs`
- Create: `apps/api/src/Template.Api/Observability/CorrelationIdMiddleware.cs`
- Create: `apps/api/tests/Template.Api.Tests/ProblemDetailsTests.cs`
- Modify: `apps/api/tests/Template.Api.Tests/Infrastructure/TestEndpointModule.cs`
- Modify: `apps/api/src/Template.Api/Program.cs`

**Interfaces:**
- Consumes: Task 1 routes, test authentication/policy, built-in `IProblemDetailsService`.
- Produces: `ApiProblemCodes`, `AddApiErrorHandling(IServiceCollection)`, `CorrelationIdMiddleware.HeaderName`, RFC Problem Details for 400/401/403/404/405/500, safe test-only `/api/testing/fault`.

- [ ] **Step 1: Add failing error contract tests**

Create `ProblemDetailsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Template.Api.Tests.Infrastructure;

namespace Template.Api.Tests;

public sealed class ProblemDetailsTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>
{
    public static TheoryData<HttpMethod, string, HttpStatusCode, string> ErrorCases =>
        new()
        {
            {
                HttpMethod.Get,
                "/api/v1/system/status?echo=" + new string('x', 65),
                HttpStatusCode.BadRequest,
                "validation_failed"
            },
            { HttpMethod.Get, "/api/v1/system/authenticated", HttpStatusCode.Unauthorized, "unauthorized" },
            { HttpMethod.Get, "/api/does-not-exist", HttpStatusCode.NotFound, "not_found" },
            { HttpMethod.Post, "/api/v1/system/status", HttpStatusCode.MethodNotAllowed, "method_not_allowed" },
            { HttpMethod.Get, "/api/testing/fault", HttpStatusCode.InternalServerError, "internal_error" },
        };

    [Theory]
    [MemberData(nameof(ErrorCases))]
    public async Task ApiFailuresUseStableProblemDetails(
        HttpMethod method,
        string uri,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(method, uri);

        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ApiProblem>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(problem);
        Assert.Equal((int)expectedStatus, problem.Status);
        Assert.Equal(expectedCode, problem.Code);
        Assert.Equal($"urn:template:problem:{expectedCode}", problem.Type);
        Assert.Equal(uri.Split('?', 2)[0], problem.Instance);
        Assert.False(string.IsNullOrWhiteSpace(problem.TraceId));
    }

    [Fact]
    public async Task ValidationFailureUsesCamelCaseFieldErrors()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/api/v1/system/status?echo=" + new string('x', 65),
            TestContext.Current.CancellationToken);

        var problem = await response.Content.ReadFromJsonAsync<ValidationApiProblem>(
            TestContext.Current.CancellationToken);
        Assert.Equal("validation_failed", problem!.Code);
        Assert.True(problem.Errors.TryGetValue("echo", out var messages));
        Assert.Contains(
            "The field echo must be between 1 and 64 characters.",
            messages);
    }

    [Fact]
    public async Task AuthenticatedPrincipalWithoutRequiredClaimGetsForbiddenProblem()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.UserHeaderName, "user-1");

        using var response = await client.GetAsync(
            "/api/testing/forbidden",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ApiProblem>(
            TestContext.Current.CancellationToken);
        Assert.Equal("forbidden", problem!.Code);
    }

    [Fact]
    public async Task UnhandledExceptionDoesNotExposeExceptionMessage()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/api/testing/fault",
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("sensitive-database-message", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.OrdinalIgnoreCase);
    }

    private record ApiProblem(
        string Type,
        string Title,
        int Status,
        string Detail,
        string Instance,
        string Code,
        string TraceId);

    private sealed record ValidationApiProblem(
        string Type,
        string Title,
        int Status,
        string Detail,
        string Instance,
        string Code,
        string TraceId,
        Dictionary<string, string[]> Errors)
        : ApiProblem(Type, Title, Status, Detail, Instance, Code, TraceId);
}
```

Replace `TestEndpointModule.cs` with:

```csharp
using Template.Api.Endpoints;

namespace Template.Api.Tests.Infrastructure;

internal sealed class TestEndpointModule : IEndpointModule
{
    internal const string ForbiddenPolicy = "Test.Forbidden";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/testing/forbidden", () => Results.Ok())
            .RequireAuthorization(ForbiddenPolicy)
            .ExcludeFromDescription();

        endpoints.MapGet("/api/testing/fault", ThrowFault)
            .ExcludeFromDescription();
    }

    private static IResult ThrowFault() =>
        throw new InvalidOperationException("sensitive-database-message");
}
```

- [ ] **Step 2: Run the focused test and verify contract failures**

Run:

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --filter FullyQualifiedName~ProblemDetailsTests
```

Expected: failures because responses are empty/default bodies, the fault escapes the handler, and no stable `code` or `traceId` exists.

- [ ] **Step 3: Implement stable problem definitions and safe exception handling**

Create stable codes:

```csharp
// Errors/ApiProblemCodes.cs
namespace Template.Api.Errors;

internal static class ApiProblemCodes
{
    internal const string InvalidRequest = "invalid_request";
    internal const string ValidationFailed = "validation_failed";
    internal const string Unauthorized = "unauthorized";
    internal const string Forbidden = "forbidden";
    internal const string NotFound = "not_found";
    internal const string MethodNotAllowed = "method_not_allowed";
    internal const string InternalError = "internal_error";
}
```

Create the contract normalizer:

```csharp
// Errors/ApiProblemDetailsDefaults.cs
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Template.Api.Observability;

namespace Template.Api.Errors;

internal static class ApiProblemDetailsDefaults
{
    internal static void Customize(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;
        var status = problem.Status ?? context.HttpContext.Response.StatusCode;
        var isValidation = problem is HttpValidationProblemDetails;
        var definition = Resolve(status, isValidation);

        problem.Status = status;
        problem.Type = $"urn:template:problem:{definition.Code}";
        problem.Title = definition.Title;
        problem.Detail = definition.Detail;
        problem.Instance = context.HttpContext.Request.Path.Value ?? "/";
        problem.Extensions["code"] = definition.Code;
        problem.Extensions["traceId"] =
            CorrelationIdMiddleware.GetTraceId(context.HttpContext);

        if (problem is HttpValidationProblemDetails validation)
        {
            var normalized = validation.Errors
                .Select(pair => new KeyValuePair<string, string[]>(
                    JsonNamingPolicy.CamelCase.ConvertName(pair.Key),
                    pair.Value))
                .ToArray();
            validation.Errors.Clear();
            foreach (var pair in normalized)
            {
                validation.Errors[pair.Key] = pair.Value;
            }
        }
    }

    private static ProblemDefinition Resolve(int status, bool isValidation) =>
        (status, isValidation) switch
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

    private sealed record ProblemDefinition(string Code, string Title, string Detail);
}
```

Create exception handling and registration:

```csharp
// Errors/ApiExceptionHandler.cs
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Template.Api.Errors;

internal sealed class ApiExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var status = exception is BadHttpRequestException badRequest
            ? badRequest.StatusCode
            : StatusCodes.Status500InternalServerError;

        if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled API exception");
        }
        else
        {
            logger.LogWarning(exception, "Rejected malformed API request");
        }

        httpContext.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails { Status = status }
        });
    }
}
```

```csharp
// Errors/ErrorHandlingServiceCollectionExtensions.cs
namespace Template.Api.Errors;

internal static class ErrorHandlingServiceCollectionExtensions
{
    internal static IServiceCollection AddApiErrorHandling(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = ApiProblemDetailsDefaults.Customize);
        services.AddExceptionHandler<ApiExceptionHandler>();
        return services;
    }
}
```

Create correlation handling before wiring the error pipeline:

```csharp
// Observability/CorrelationIdMiddleware.cs
using System.Diagnostics;

namespace Template.Api.Observability;

internal sealed class CorrelationIdMiddleware(
    RequestDelegate next,
    ILogger<CorrelationIdMiddleware> logger)
{
    internal const string HeaderName = "X-Correlation-ID";
    private const string ItemKey = "Template.Api.TraceId";

    public async Task InvokeAsync(HttpContext context)
    {
        var traceId = TryGetAcceptedHeader(context.Request, out var accepted)
            ? accepted
            : Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;

        context.Items[ItemKey] = traceId;
        context.Response.Headers[HeaderName] = traceId;

        using (logger.BeginScope(new Dictionary<string, object?> { ["TraceId"] = traceId }))
        {
            await next(context);
        }
    }

    internal static string GetTraceId(HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out var value) && value is string traceId
            ? traceId
            : context.TraceIdentifier;

    private static bool TryGetAcceptedHeader(HttpRequest request, out string value)
    {
        value = string.Empty;
        if (!request.Headers.TryGetValue(HeaderName, out var values) || values.Count != 1)
        {
            return false;
        }

        var candidate = values.ToString();
        if (candidate.Length is < 1 or > 64 ||
            candidate.Any(character =>
                !char.IsAsciiLetterOrDigit(character) &&
                character is not ('.' or '_' or '-')))
        {
            return false;
        }

        value = candidate;
        return true;
    }
}
```

Wire only `/api/**` through correlation, exception, and status-code Problem Details in `Program.cs`:

```csharp
using Template.Api.Authentication;
using Template.Api.Endpoints;
using Template.Api.Errors;
using Template.Api.Observability;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddValidation();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHealthChecks();
builder.Services.AddApiAuthentication();
builder.Services.AddApiErrorHandling();
builder.Services.AddEndpointModules();

var app = builder.Build();

app.UseWhen(
    context => context.Request.Path.StartsWithSegments("/api"),
    api =>
    {
        api.UseMiddleware<CorrelationIdMiddleware>();
        api.UseExceptionHandler();
        api.UseStatusCodePages();
    });

app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/api/health").AllowAnonymous();
app.MapEndpointModules();

app.Run();

public partial class Program;
```

- [ ] **Step 4: Run focused tests and the system endpoint tests**

Run:

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --filter "FullyQualifiedName~ProblemDetailsTests|FullyQualifiedName~SystemEndpointTests"
```

Expected: all error and system endpoint tests pass; 500 response contains neither the injected message nor an exception type.

- [ ] **Step 5: Commit the error contract**

```bash
git add \
  apps/api/src/Template.Api/Errors \
  apps/api/src/Template.Api/Observability/CorrelationIdMiddleware.cs \
  apps/api/src/Template.Api/Program.cs \
  apps/api/tests/Template.Api.Tests/ProblemDetailsTests.cs \
  apps/api/tests/Template.Api.Tests/Infrastructure/TestEndpointModule.cs
git commit -m "feat(api): standardize problem details"
```

---

### Task 3: Compatibility, Liveness, and Readiness Health Probes

**Files:**
- Create: `apps/api/src/Template.Api/Features/Health/HealthResponse.cs`
- Create: `apps/api/src/Template.Api/Features/Health/HealthEndpointModule.cs`
- Modify: `apps/api/src/Template.Api/Endpoints/EndpointModuleExtensions.cs`
- Modify: `apps/api/src/Template.Api/Program.cs`
- Replace: `apps/api/tests/Template.Api.Tests/HealthEndpointTests.cs`

**Interfaces:**
- Consumes: `ApiResponse<T>`, `IEndpointModule`, registered `HealthCheckService`, `TimeProvider`.
- Produces: anonymous `GET /api/health`, `GET /api/health/live`, `GET /api/health/ready`; `200`/`503` typed envelopes with `Cache-Control: no-store`.

- [ ] **Step 1: Replace the bootstrap test with failing health contract tests**

Replace `HealthEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Template.Api.Tests.Infrastructure;

namespace Template.Api.Tests;

public sealed class HealthEndpointTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>
{
    [Theory]
    [InlineData("/api/health")]
    [InlineData("/api/health/live")]
    [InlineData("/api/health/ready")]
    public async Task HealthyProbeReturnsEnvelopeAndDisablesCaching(string uri)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(uri, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        var payload = await response.Content.ReadFromJsonAsync<HealthEnvelope>(
            TestContext.Current.CancellationToken);
        Assert.Equal("healthy", payload!.Data.Status);
        Assert.NotEqual(default, payload.Data.Timestamp);
    }

    [Fact]
    public async Task FailedReadyCheckReturnsTyped503WhileLivenessRemainsHealthy()
    {
        await using var unhealthyFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddHealthChecks().AddCheck(
                    "test-ready",
                    () => HealthCheckResult.Unhealthy(),
                    tags: ["ready"])));
        using var client = unhealthyFactory.CreateClient();

        using var ready = await client.GetAsync(
            "/api/health/ready",
            TestContext.Current.CancellationToken);
        using var live = await client.GetAsync(
            "/api/health/live",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal("application/json", ready.Content.Headers.ContentType?.MediaType);
        var readyPayload = await ready.Content.ReadFromJsonAsync<HealthEnvelope>(
            TestContext.Current.CancellationToken);
        Assert.Equal("unhealthy", readyPayload!.Data.Status);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    private sealed record HealthEnvelope(HealthData Data);
    private sealed record HealthData(string Status, DateTimeOffset Timestamp);
}
```

- [ ] **Step 2: Run the health tests and verify the red state**

Run:

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --filter FullyQualifiedName~HealthEndpointTests
```

Expected: `/api/health/live` and `/api/health/ready` return `404`; the
compatibility alias fails its envelope/cache assertions because it still uses
the bootstrap health-check writer.

- [ ] **Step 3: Implement one shared health evaluator with explicit predicates**

Create the response and module:

```csharp
// Features/Health/HealthResponse.cs
namespace Template.Api.Features.Health;

internal sealed record HealthResponse(string Status, DateTimeOffset Timestamp);
```

```csharp
// Features/Health/HealthEndpointModule.cs
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Template.Api.Contracts;
using Template.Api.Endpoints;

namespace Template.Api.Features.Health;

internal sealed class HealthEndpointModule : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        MapReady(endpoints, "/api/health", "GetHealth");
        MapLive(endpoints);
        MapReady(endpoints, "/api/health/ready", "GetReadiness");
    }

    private static void MapLive(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/health/live",
                (HealthCheckService checks, TimeProvider timeProvider,
                    HttpContext context, CancellationToken cancellationToken) =>
                    Evaluate(
                        checks,
                        timeProvider,
                        context,
                        _ => false,
                        cancellationToken))
            .AllowAnonymous()
            .WithName("GetLiveness")
            .Produces<ApiResponse<HealthResponse>>()
            .Produces<ApiResponse<HealthResponse>>(StatusCodes.Status503ServiceUnavailable);
    }

    private static void MapReady(
        IEndpointRouteBuilder endpoints,
        string route,
        string operationName)
    {
        endpoints.MapGet(
                route,
                (HealthCheckService checks, TimeProvider timeProvider,
                    HttpContext context, CancellationToken cancellationToken) =>
                    Evaluate(
                        checks,
                        timeProvider,
                        context,
                        registration => registration.Tags.Contains("ready"),
                        cancellationToken))
            .AllowAnonymous()
            .WithName(operationName)
            .Produces<ApiResponse<HealthResponse>>()
            .Produces<ApiResponse<HealthResponse>>(StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<IResult> Evaluate(
        HealthCheckService checks,
        TimeProvider timeProvider,
        HttpContext context,
        Func<HealthCheckRegistration, bool> predicate,
        CancellationToken cancellationToken)
    {
        var report = await checks.CheckHealthAsync(predicate, cancellationToken);
        var statusCode = report.Status == HealthStatus.Unhealthy
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status200OK;
        context.Response.Headers.CacheControl = "no-store";

        return Results.Json(
            new ApiResponse<HealthResponse>(
                new HealthResponse(
                    report.Status.ToString().ToLowerInvariant(),
                    timeProvider.GetUtcNow())),
            statusCode: statusCode);
    }
}
```

Register `HealthEndpointModule` before `SystemEndpointModule`:

```csharp
internal static IServiceCollection AddEndpointModules(this IServiceCollection services)
{
    services.AddSingleton<IEndpointModule, HealthEndpointModule>();
    services.AddSingleton<IEndpointModule, SystemEndpointModule>();
    return services;
}
```

Add `using Template.Api.Features.Health;` to `EndpointModuleExtensions.cs`.

Remove the temporary bootstrap mapping from `Program.cs`:

```csharp
// Delete this line after HealthEndpointModule is registered:
app.MapHealthChecks("/api/health").AllowAnonymous();
```

- [ ] **Step 4: Run health and full integration tests**

Run:

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --filter FullyQualifiedName~HealthEndpointTests
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj
```

Expected: all integration tests pass, including typed `503` readiness and unaffected liveness.

- [ ] **Step 5: Commit health semantics**

```bash
git add \
  apps/api/src/Template.Api/Features/Health \
  apps/api/src/Template.Api/Endpoints/EndpointModuleExtensions.cs \
  apps/api/src/Template.Api/Program.cs \
  apps/api/tests/Template.Api.Tests/HealthEndpointTests.cs
git commit -m "feat(api): add live and ready health probes"
```

---

### Task 4: Correlation and Structured Request Completion Logging

**Files:**
- Create: `apps/api/src/Template.Api/Observability/RequestLoggingMiddleware.cs`
- Create: `apps/api/tests/Template.Api.Tests/Infrastructure/CapturedLogProvider.cs`
- Create: `apps/api/tests/Template.Api.Tests/ObservabilityTests.cs`
- Modify: `apps/api/tests/Template.Api.Tests/Infrastructure/ApiWebApplicationFactory.cs`
- Modify: `apps/api/src/Template.Api/Program.cs`
- Modify: `apps/api/src/Template.Api/appsettings.json`
- Modify: `apps/api/src/Template.Api/appsettings.Development.json`

**Interfaces:**
- Consumes: Task 2 correlation scope and `/api/**` middleware branch.
- Produces: completion event `ApiRequestCompleted`, scope property `TraceId`, safe method/path/status/elapsed fields, Debug health logs, JSON console scopes in production, readable console scopes in development.

- [ ] **Step 1: Add failing correlation and logging assertions**

Create `CapturedLogProvider.cs`:

```csharp
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Template.Api.Tests.Infrastructure;

internal sealed record CapturedLog(
    LogLevel Level,
    string Category,
    string Message,
    IReadOnlyDictionary<string, object?> State,
    IReadOnlyDictionary<string, object?> Scope);

internal sealed class CapturedLogProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly ConcurrentQueue<CapturedLog> logs = new();
    private IExternalScopeProvider scopeProvider = new LoggerExternalScopeProvider();

    internal IReadOnlyCollection<CapturedLog> Logs => logs.ToArray();

    internal void Clear()
    {
        while (logs.TryDequeue(out _))
        {
        }
    }

    public ILogger CreateLogger(string categoryName) =>
        new CapturedLogger(categoryName, logs, () => scopeProvider);

    public void SetScopeProvider(IExternalScopeProvider provider) =>
        scopeProvider = provider;

    public void Dispose()
    {
    }

    private sealed class CapturedLogger(
        string category,
        ConcurrentQueue<CapturedLog> target,
        Func<IExternalScopeProvider> getScopeProvider)
        : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
            getScopeProvider().Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var stateValues = ToDictionary(state);
            var scopeValues = new Dictionary<string, object?>(StringComparer.Ordinal);
            getScopeProvider().ForEachScope(
                (scope, values) =>
                {
                    foreach (var pair in ToDictionary(scope))
                    {
                        values[pair.Key] = pair.Value;
                    }
                },
                scopeValues);
            target.Enqueue(new CapturedLog(
                logLevel,
                category,
                formatter(state, exception),
                stateValues,
                scopeValues));
        }

        private static IReadOnlyDictionary<string, object?> ToDictionary<TState>(TState state)
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> values)
            {
                return values.ToDictionary(pair => pair.Key, pair => pair.Value);
            }

            return new Dictionary<string, object?>();
        }
    }
}
```

Create `ObservabilityTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Template.Api.Observability;
using Template.Api.Tests.Infrastructure;

namespace Template.Api.Tests;

public sealed class ObservabilityTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task AcceptedCorrelationIdMatchesHeaderProblemAndLogScope()
    {
        var logs = factory.Services.GetRequiredService<CapturedLogProvider>();
        logs.Clear();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(CorrelationIdMiddleware.HeaderName, "client.trace-123");

        using var response = await client.GetAsync(
            "/api/does-not-exist?secret=query-value",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            "client.trace-123",
            response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single());
        var problem = await response.Content.ReadFromJsonAsync<ProblemTrace>(
            TestContext.Current.CancellationToken);
        Assert.Equal("client.trace-123", problem!.TraceId);
        var completion = Assert.Single(
            logs.Logs.Where(log => log.State.TryGetValue(
                "{OriginalFormat}",
                out var format) &&
                Equals(format, "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMilliseconds} ms")));
        Assert.Equal("client.trace-123", completion.Scope["TraceId"]);
        Assert.Equal("/api/does-not-exist", completion.State["Path"]);
        Assert.DoesNotContain("query-value", completion.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidCorrelationIdIsIgnoredWithoutRejectingRequest()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            CorrelationIdMiddleware.HeaderName,
            "invalid value with spaces");

        using var response = await client.GetAsync(
            "/api/v1/system/status",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var actual = response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();
        Assert.NotEqual("invalid value with spaces", actual);
        Assert.NotEmpty(actual);
    }

    [Fact]
    public async Task HealthCompletionIsLoggedAtDebug()
    {
        var logs = factory.Services.GetRequiredService<CapturedLogProvider>();
        logs.Clear();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/api/health/live",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            logs.Logs,
            log => log.Category.EndsWith(nameof(RequestLoggingMiddleware), StringComparison.Ordinal) &&
                   log.Level == LogLevel.Debug);
    }

    private sealed record ProblemTrace(string TraceId);
}
```

- [ ] **Step 2: Register the capture provider in the fixture and observe the red test**

Replace `ApiWebApplicationFactory.cs` with the complete fixture:

```csharp
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Template.Api.Endpoints;

namespace Template.Api.Tests.Infrastructure;

public sealed class ApiWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Debug));
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
}
```

Run:

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --filter FullyQualifiedName~ObservabilityTests
```

Expected: correlation header/problem assertions pass from Task 2, but no completion record exists and the Debug health assertion fails.

- [ ] **Step 3: Add structured request completion middleware**

Create `RequestLoggingMiddleware.cs`:

```csharp
using System.Diagnostics;

namespace Template.Api.Observability;

internal sealed class RequestLoggingMiddleware(
    RequestDelegate next,
    ILogger<RequestLoggingMiddleware> logger)
{
    private static readonly EventId CompletionEvent = new(1000, "ApiRequestCompleted");

    public async Task InvokeAsync(HttpContext context)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var statusCode = StatusCodes.Status200OK;

        try
        {
            await next(context);
            statusCode = context.Response.StatusCode;
        }
        catch
        {
            statusCode = StatusCodes.Status500InternalServerError;
            throw;
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            var level = ResolveLevel(context.Request.Path, statusCode);
            logger.Log(
                level,
                CompletionEvent,
                "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMilliseconds} ms",
                context.Request.Method,
                context.Request.Path.Value ?? "/",
                statusCode,
                Math.Round(elapsed, 3));
        }
    }

    private static LogLevel ResolveLevel(PathString path, int statusCode)
    {
        if (path.StartsWithSegments("/api/health"))
        {
            return LogLevel.Debug;
        }

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            return LogLevel.Error;
        }

        return statusCode >= StatusCodes.Status400BadRequest
            ? LogLevel.Warning
            : LogLevel.Information;
    }
}
```

Place it after `UseStatusCodePages()` inside the `/api/**` branch:

```csharp
api.UseMiddleware<CorrelationIdMiddleware>();
api.UseExceptionHandler();
api.UseStatusCodePages();
api.UseMiddleware<RequestLoggingMiddleware>();
```

This order keeps the completion event inside the correlation scope. The middleware records `500` before rethrowing; the outer exception handler writes the safe response.

- [ ] **Step 4: Configure console formatters with scopes**

Use JSON console by default in `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    },
    "Console": {
      "FormatterName": "json",
      "FormatterOptions": {
        "IncludeScopes": true,
        "UseUtcTimestamp": true,
        "TimestampFormat": "yyyy-MM-ddTHH:mm:ss.fffZ"
      }
    }
  },
  "AllowedHosts": "*"
}
```

Use readable simple console in `appsettings.Development.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Information"
    },
    "Console": {
      "FormatterName": "simple",
      "FormatterOptions": {
        "IncludeScopes": true,
        "SingleLine": true,
        "TimestampFormat": "HH:mm:ss "
      }
    }
  }
}
```

- [ ] **Step 5: Run focused and complete integration tests**

Run:

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --filter FullyQualifiedName~ObservabilityTests
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj
```

Expected: all tests pass; captured completion state contains path without query values and scope contains the same trace ID returned to the client.

- [ ] **Step 6: Commit observability**

```bash
git add \
  apps/api/src/Template.Api/Observability \
  apps/api/src/Template.Api/Program.cs \
  apps/api/src/Template.Api/appsettings.json \
  apps/api/src/Template.Api/appsettings.Development.json \
  apps/api/tests/Template.Api.Tests/Infrastructure \
  apps/api/tests/Template.Api.Tests/ObservabilityTests.cs
git commit -m "feat(api): add correlation and request logging"
```

---

### Task 5: OpenAPI 3.1 Runtime Document, Build-Time Export, and Drift Gate

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `apps/api/src/Template.Api/Template.Api.csproj`
- Create: `apps/api/src/Template.Api/OpenApi/OpenApiDefaults.cs`
- Create: `apps/api/src/Template.Api/OpenApi/OpenApiServiceCollectionExtensions.cs`
- Create: `apps/api/src/Template.Api/OpenApi/OpenApiEndpointConventionExtensions.cs`
- Modify: `apps/api/src/Template.Api/Features/System/SystemEndpointModule.cs`
- Modify: `apps/api/src/Template.Api/Features/Health/HealthEndpointModule.cs`
- Modify: `apps/api/src/Template.Api/Program.cs`
- Create: `apps/api/tests/Template.Api.Tests/OpenApiContractTests.cs`
- Create: `contracts/openapi/v1.json` by the documented build-time exporter
- Delete: `contracts/openapi/.gitkeep`

**Interfaces:**
- Consumes: endpoint authorization metadata and typed response DTOs from Tasks 1–3.
- Produces: `AddApiOpenApi(IServiceCollection)`, document `v1`, development/test route `/api/openapi/v1.json`, cookie security scheme `cookieAuth`, build property `OpenApiGenerateDocuments=true`, canonical `contracts/openapi/v1.json`, semantic contract gate.

- [ ] **Step 1: Add failing runtime OpenAPI tests**

Create `OpenApiContractTests.cs`:

```csharp
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Template.Api.Tests.Infrastructure;

namespace Template.Api.Tests;

public sealed class OpenApiContractTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task TestHostPublishesVersionedOpenApi31Contract()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/api/openapi/v1.json",
            TestContext.Current.CancellationToken);
        var document = JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("3.1.", document["openapi"]!.GetValue<string>());
        Assert.NotNull(document["paths"]!["/api/health"]);
        Assert.NotNull(document["paths"]!["/api/health/live"]);
        Assert.NotNull(document["paths"]!["/api/health/ready"]);
        Assert.NotNull(document["paths"]!["/api/v1/system/status"]);
        Assert.NotNull(document["paths"]!["/api/v1/system/authenticated"]);
        Assert.Null(document["paths"]!["/api/testing/fault"]);
        Assert.Null(document["paths"]!["/api/testing/forbidden"]);
    }

    [Fact]
    public async Task CookieSchemeAppliesOnlyToProtectedOperation()
    {
        using var client = factory.CreateClient();
        var document = JsonNode.Parse(await client.GetStringAsync(
            "/api/openapi/v1.json",
            TestContext.Current.CancellationToken))!;

        var scheme = document["components"]!["securitySchemes"]!["cookieAuth"]!;
        Assert.Equal("apiKey", scheme["type"]!.GetValue<string>());
        Assert.Equal("cookie", scheme["in"]!.GetValue<string>());
        Assert.Equal("__Host-template.session", scheme["name"]!.GetValue<string>());
        Assert.Null(document["paths"]!["/api/v1/system/status"]!["get"]!["security"]);
        Assert.NotNull(document["paths"]!["/api/v1/system/authenticated"]!["get"]!["security"]);
    }

    [Fact]
    public async Task ProductionHostDoesNotPublishRuntimeOpenApi()
    {
        await using var productionFactory = factory.WithWebHostBuilder(
            builder => builder.UseEnvironment("Production"));
        using var client = productionFactory.CreateClient();

        using var response = await client.GetAsync(
            "/api/openapi/v1.json",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RuntimeDocumentSemanticallyMatchesCommittedContract()
    {
        using var client = factory.CreateClient();
        var runtime = JsonNode.Parse(await client.GetStringAsync(
            "/api/openapi/v1.json",
            TestContext.Current.CancellationToken));
        var repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        var committed = JsonNode.Parse(await File.ReadAllTextAsync(
            Path.Combine(repositoryRoot, "contracts", "openapi", "v1.json"),
            TestContext.Current.CancellationToken));

        Assert.True(
            JsonNode.DeepEquals(committed, runtime),
            "Run the documented OpenAPI export command and commit contracts/openapi/v1.json.");
    }

    private static string FindRepositoryRoot(string start)
    {
        for (var directory = new DirectoryInfo(start);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Template.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Template.sln was not found above the test output directory.");
    }
}
```

- [ ] **Step 2: Run the focused tests and verify the runtime route is missing**

Run:

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --filter FullyQualifiedName~OpenApiContractTests
```

Expected: `404` from `/api/openapi/v1.json` and a missing committed `v1.json`.

- [ ] **Step 3: Add pinned first-party OpenAPI dependencies and deterministic export**

Extend `Directory.Packages.props`:

```xml
<PackageVersion Include="Microsoft.AspNetCore.OpenApi" Version="10.0.10" />
<PackageVersion Include="Microsoft.Extensions.ApiDescription.Server" Version="10.0.10" />
<PackageVersion Include="Microsoft.OpenApi" Version="2.11.0" />
```

Add these references and build properties to `Template.Api.csproj`:

```xml
<PropertyGroup>
  <InterceptorsNamespaces>$(InterceptorsNamespaces);Microsoft.AspNetCore.Http.Validation.Generated</InterceptorsNamespaces>
  <OpenApiGenerateDocuments>false</OpenApiGenerateDocuments>
  <OpenApiDocumentsDirectory>$(MSBuildProjectDirectory)/../../../../contracts/openapi</OpenApiDocumentsDirectory>
  <OpenApiGenerateDocumentsOptions>--openapi-version OpenApi3_1</OpenApiGenerateDocumentsOptions>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="Microsoft.AspNetCore.OpenApi" />
  <PackageReference Include="Microsoft.Extensions.ApiDescription.Server" PrivateAssets="all" />
  <PackageReference Include="Microsoft.OpenApi" />
</ItemGroup>

<Target Name="NormalizeOpenApiDocumentName"
        AfterTargets="GenerateOpenApiDocuments"
        Condition="'$(OpenApiGenerateDocuments)' == 'true'">
  <Move SourceFiles="$(OpenApiDocumentsDirectory)/$(MSBuildProjectName).json"
        DestinationFiles="$(OpenApiDocumentsDirectory)/v1.json"
        OverwriteReadOnlyFiles="true" />
</Target>
```

- [ ] **Step 4: Add document and protected-operation transformers**

Create constants:

```csharp
// OpenApi/OpenApiDefaults.cs
namespace Template.Api.OpenApi;

internal static class OpenApiDefaults
{
    internal const string DocumentName = "v1";
    internal const string CookieSecurityScheme = "cookieAuth";
    internal const string ProblemContentType = "application/problem+json";
}
```

Create OpenAPI service registration:

```csharp
// OpenApi/OpenApiServiceCollectionExtensions.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Template.Api.Authentication;

namespace Template.Api.OpenApi;

internal static class OpenApiServiceCollectionExtensions
{
    internal static IServiceCollection AddApiOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(OpenApiDefaults.DocumentName, options =>
        {
            options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_1;
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "Template API";
                document.Info.Version = "v1";
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??=
                    new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes[OpenApiDefaults.CookieSecurityScheme] =
                    new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.ApiKey,
                        In = ParameterLocation.Cookie,
                        Name = ApiAuthenticationDefaults.CookieName,
                        Description = "Secure HttpOnly same-origin session cookie."
                    };
                return Task.CompletedTask;
            });
            options.AddOperationTransformer((operation, context, _) =>
            {
                var metadata = context.Description.ActionDescriptor.EndpointMetadata;
                if (metadata.OfType<IAllowAnonymous>().Any() ||
                    !metadata.OfType<IAuthorizeData>().Any())
                {
                    return Task.CompletedTask;
                }

                operation.Security ??= [];
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(
                        OpenApiDefaults.CookieSecurityScheme,
                        context.Document,
                        null)] = []
                });
                return Task.CompletedTask;
            });
        });

        return services;
    }
}
```

Create reusable Problem Details metadata:

```csharp
// OpenApi/OpenApiEndpointConventionExtensions.cs
using Microsoft.AspNetCore.Mvc;

namespace Template.Api.OpenApi;

internal static class OpenApiEndpointConventionExtensions
{
    internal static RouteHandlerBuilder ProducesValidationProblem(
        this RouteHandlerBuilder builder) =>
        builder.Produces<HttpValidationProblemDetails>(
            StatusCodes.Status400BadRequest,
            OpenApiDefaults.ProblemContentType);

    internal static RouteHandlerBuilder ProducesPublicApiProblems(
        this RouteHandlerBuilder builder) =>
        builder
            .Produces<ProblemDetails>(
                StatusCodes.Status404NotFound,
                OpenApiDefaults.ProblemContentType)
            .Produces<ProblemDetails>(
                StatusCodes.Status405MethodNotAllowed,
                OpenApiDefaults.ProblemContentType)
            .Produces<ProblemDetails>(
                StatusCodes.Status500InternalServerError,
                OpenApiDefaults.ProblemContentType);

    internal static RouteHandlerBuilder ProducesProtectedApiProblems(
        this RouteHandlerBuilder builder) =>
        builder
            .Produces<ProblemDetails>(
                StatusCodes.Status401Unauthorized,
                OpenApiDefaults.ProblemContentType)
            .Produces<ProblemDetails>(
                StatusCodes.Status403Forbidden,
                OpenApiDefaults.ProblemContentType)
            .ProducesPublicApiProblems();
}
```

Replace `SystemEndpointModule.cs` with the fully annotated endpoint module:

```csharp
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Template.Api.Authentication;
using Template.Api.Contracts;
using Template.Api.Endpoints;
using Template.Api.OpenApi;

namespace Template.Api.Features.System;

internal sealed class SystemEndpointModule : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/system")
            .RequireAuthorization(ApiPolicies.Authenticated);

        group.MapGet(
                "/status",
                ([FromQuery, StringLength(
                    64,
                    MinimumLength = 1,
                    ErrorMessage = "The field echo must be between 1 and 64 characters.")]
                    string? echo,
                    TimeProvider timeProvider) =>
                    TypedResults.Ok(new ApiResponse<SystemStatusResponse>(
                        new SystemStatusResponse(
                            "ok",
                            "1",
                            timeProvider.GetUtcNow(),
                            echo))))
            .AllowAnonymous()
            .WithName("GetSystemStatus")
            .WithSummary("Returns API status and echoes a validated optional value.")
            .Produces<ApiResponse<SystemStatusResponse>>()
            .ProducesValidationProblem()
            .ProducesPublicApiProblems();

        group.MapGet(
                "/authenticated",
                () => TypedResults.Ok(new ApiResponse<AuthenticatedResponse>(
                    new AuthenticatedResponse("authenticated"))))
            .WithName("GetAuthenticatedStatus")
            .WithSummary("Confirms that cookie authentication and authorization succeeded.")
            .Produces<ApiResponse<AuthenticatedResponse>>()
            .ProducesProtectedApiProblems();
    }
}
```

Replace `HealthEndpointModule.cs` with the module whose three operations expose
their typed health and standard failure metadata:

```csharp
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Template.Api.Contracts;
using Template.Api.Endpoints;
using Template.Api.OpenApi;

namespace Template.Api.Features.Health;

internal sealed class HealthEndpointModule : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        MapReady(endpoints, "/api/health", "GetHealth");
        MapLive(endpoints);
        MapReady(endpoints, "/api/health/ready", "GetReadiness");
    }

    private static void MapLive(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/health/live",
                (HealthCheckService checks, TimeProvider timeProvider,
                    HttpContext context, CancellationToken cancellationToken) =>
                    Evaluate(
                        checks,
                        timeProvider,
                        context,
                        _ => false,
                        cancellationToken))
            .AllowAnonymous()
            .WithName("GetLiveness")
            .WithSummary("Reports whether the API process can answer requests.")
            .Produces<ApiResponse<HealthResponse>>()
            .Produces<ApiResponse<HealthResponse>>(StatusCodes.Status503ServiceUnavailable)
            .ProducesPublicApiProblems();
    }

    private static void MapReady(
        IEndpointRouteBuilder endpoints,
        string route,
        string operationName)
    {
        endpoints.MapGet(
                route,
                (HealthCheckService checks, TimeProvider timeProvider,
                    HttpContext context, CancellationToken cancellationToken) =>
                    Evaluate(
                        checks,
                        timeProvider,
                        context,
                        registration => registration.Tags.Contains("ready"),
                        cancellationToken))
            .AllowAnonymous()
            .WithName(operationName)
            .WithSummary("Reports whether the API is ready to receive traffic.")
            .Produces<ApiResponse<HealthResponse>>()
            .Produces<ApiResponse<HealthResponse>>(StatusCodes.Status503ServiceUnavailable)
            .ProducesPublicApiProblems();
    }

    private static async Task<IResult> Evaluate(
        HealthCheckService checks,
        TimeProvider timeProvider,
        HttpContext context,
        Func<HealthCheckRegistration, bool> predicate,
        CancellationToken cancellationToken)
    {
        var report = await checks.CheckHealthAsync(predicate, cancellationToken);
        var statusCode = report.Status == HealthStatus.Unhealthy
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status200OK;
        context.Response.Headers.CacheControl = "no-store";

        return Results.Json(
            new ApiResponse<HealthResponse>(
                new HealthResponse(
                    report.Status.ToString().ToLowerInvariant(),
                    timeProvider.GetUtcNow())),
            statusCode: statusCode);
    }
}
```

Replace `Program.cs` with the complete composition root:

```csharp
using Template.Api.Authentication;
using Template.Api.Endpoints;
using Template.Api.Errors;
using Template.Api.Observability;
using Template.Api.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddValidation();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHealthChecks();
builder.Services.AddApiAuthentication();
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
    });

app.UseAuthentication();
app.UseAuthorization();
app.MapEndpointModules();

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Test"))
{
    app.MapOpenApi("/api/openapi/{documentName}.json").AllowAnonymous();
}

app.Run();

public partial class Program;
```

- [ ] **Step 5: Restore, run shape tests, and export the canonical contract**

Run:

```bash
dotnet restore Template.sln
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore \
  --filter "FullyQualifiedName~TestHostPublishesVersionedOpenApi31Contract|FullyQualifiedName~CookieSchemeAppliesOnlyToProtectedOperation"
dotnet build apps/api/src/Template.Api/Template.Api.csproj \
  --no-restore \
  -p:OpenApiGenerateDocuments=true
test -f contracts/openapi/v1.json
```

Expected: restore succeeds without vulnerability or downgrade warnings; the two runtime shape tests pass; the exporter creates `contracts/openapi/v1.json`.

Delete `contracts/openapi/.gitkeep` after the contract exists.

- [ ] **Step 6: Run the semantic drift gate twice**

Run:

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore \
  --filter FullyQualifiedName~OpenApiContractTests
dotnet build apps/api/src/Template.Api/Template.Api.csproj \
  --no-restore \
  -p:OpenApiGenerateDocuments=true
git diff --exit-code -- contracts/openapi/v1.json
```

Expected: all OpenAPI tests pass; a second export produces no diff.

- [ ] **Step 7: Commit the API contract discipline**

```bash
git add \
  Directory.Packages.props \
  apps/api/src/Template.Api/Template.Api.csproj \
  apps/api/src/Template.Api/OpenApi \
  apps/api/src/Template.Api/Features \
  apps/api/src/Template.Api/Program.cs \
  apps/api/tests/Template.Api.Tests/OpenApiContractTests.cs \
  contracts/openapi
git commit -m "feat(api): publish deterministic OpenAPI contract"
```

---

### Task 6: Durable Conventions, Migration Register, and Acceptance Evidence

**Files:**
- Create: `docs/api-conventions.md`
- Modify: `docs/aspnetcore-migration-plan.md`
- Verify only: `AGENTS.md`
- Verify only: `docs/superpowers/specs/2026-07-23-api-foundation-design.md`

**Interfaces:**
- Consumes: actual API behavior and command results from Tasks 1–5.
- Produces: durable iteration-1 conventions, completed iteration register, exact verification evidence, documented known differences and next-iteration boundary.

- [ ] **Step 1: Create the durable API conventions document**

Create `docs/api-conventions.md` with this content:

````markdown
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

API failures use `application/problem+json` with RFC Problem Details fields
`type`, `title`, `status`, `detail`, and `instance`, plus stable `code` and
`traceId`. Validation responses also contain a camelCase `errors` dictionary.
The initial codes are `invalid_request`, `validation_failed`, `unauthorized`,
`forbidden`, `not_found`, `method_not_allowed`, and `internal_error`.

`type` is always `urn:template:problem:{code}`. Client code branches on `code`,
not on invariant-English `title`, `detail`, or validation messages. API responses
never expose stack traces, exception messages, SQL, secrets, cookies, or
authorization headers.

Health `503` is a typed health result rather than a Problem Details failure.

## Validation and authorization

Minimal API binding and Data Annotations validate request DTOs and parameters at
the HTTP boundary. Domain and application rules remain independent of HTTP
validation. The `/api/v1` consumer group requires the named authenticated-user
policy by default; public operations explicitly opt out with `AllowAnonymous`.

The iteration-1 cookie handler uses scheme `Template.Session` and cookie
`__Host-template.session` with `HttpOnly`, `Secure`, `SameSite=Lax`, `Path=/`,
and no `Domain`. API challenge/forbid returns `401`/`403` and never redirects to
HTML.

The browser never reads the HttpOnly cookie and never stores a bearer token.
Iteration 3 introduces `GET /api/v1/auth/session`: both anonymous and
authenticated projections return `200 { "data": ... }` with
`Cache-Control: no-store`. Browser requests send the same-origin cookie
automatically; Next.js SSR forwards the incoming `Cookie` header to the API.
Antiforgery is required before the first cookie-authenticated mutation.
The target deployment is same-origin, so CORS is not enabled.

## Health

- `GET /api/health` is the compatibility alias for readiness.
- `GET /api/health/live` excludes dependency checks.
- `GET /api/health/ready` runs checks tagged `ready`.
- Health responses expose only `status` and UTC `timestamp`.
- Healthy responses use `200`; unhealthy readiness uses `503`.
- Every health response uses `Cache-Control: no-store`.

Future database/cache checks must opt into readiness with tag `ready` and must
not participate in liveness.

## Correlation and logging

`X-Correlation-ID` is accepted only when it is 1–64 characters and contains
ASCII letters, digits, `.`, `_`, or `-`. Invalid input is ignored. The canonical
value appears in the response header, Problem Details `traceId`, and the
`TraceId` logging scope.

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
````

- [ ] **Step 2: Update current iteration and completed register**

Change the migration-plan header to:

```markdown
**Статус:** активная дорожная карта.
**Текущая итерация:** 1 — API foundation и контрактная дисциплина (завершена 2026-07-23).
```

Replace the iteration register with:

```markdown
| Итерация | Состояние | Примечание |
| --- | --- | --- |
| 0 — bootstrap | Завершена | Reference перенесён, .NET 10 solution и health probe созданы; продуктовый код не переносился. |
| 1 — API foundation | Завершена | Problem Details, validation, cookie auth boundary, correlation/logging, live/ready health, OpenAPI 3.1 export и integration contract tests приняты. |
| 2–12 | Не начаты | Следующий dependency gate — чистый Next.js UI foundation, generated REST client и browser smoke без переноса auth/product domain. |
```

Append this iteration-1 evidence section before the document update rule:

```markdown
## Acceptance evidence: итерация 1

**Scope:** только `Template.Api`, `Template.Api.Tests`, `contracts/openapi` и
документация. `Template.Domain`, `Template.Application`,
`Template.Infrastructure`, `apps/web` и persistent schema не менялись.

| Reference | Новый API | Новый UI | Test/evidence |
| --- | --- | --- | --- |
| `template/src/app/api/health/route.ts`, `template/e2e/support/config.ts` | `/api/health`, `/api/health/live`, `/api/health/ready` | N/A до итерации 2 | `HealthEndpointTests` |
| `template/src/features/routes.ts`, `template/src/proxy.ts` | public status и protected authenticated probe | N/A | `SystemEndpointTests`, `ProblemDetailsTests` |
| `template/src/lib/actions.ts`, `template/src/types/actions.ts`, API-key errors | `{ data }`, validation и RFC Problem Details | N/A | 400/401/403/404/405/500 contract cases |
| `template/src/lib/logger.ts` | `ILogger`, correlation scope, completion events | N/A | `ObservabilityTests` |
| reference API auth tests | cookie/policy extension points без API-key domain | N/A | test-only authentication and deny policy |
| `template/prisma/schema.prisma` | schema отсутствует в scope | N/A | нет EF packages/migrations |

**Проверки 2026-07-23:**

| Команда | Результат |
| --- | --- |
| `dotnet restore Template.sln` | PASS |
| `dotnet build Template.sln --no-restore` | PASS |
| `dotnet test Template.sln --no-restore` | PASS |
| OpenAPI export with `-p:OpenApiGenerateDocuments=true` | PASS; deterministic `contracts/openapi/v1.json` |
| OpenAPI semantic drift test | PASS |
| `git diff --exit-code -- contracts/openapi/v1.json` after second export | PASS |
| `git diff -- template/` | empty |
| UI build / Playwright E2E | N/A: `apps/web` starts in iteration 2 |

**Известные расхождения с reference:** ошибки используют RFC Problem Details
вместо `{ "error": ... }`; health использует `{ "data": ... }`; live/ready,
system probes, correlation ID и OpenAPI являются новой foundation surface.
Product routes, user session projection and UI parity intentionally remain
outside iteration 1.

**Следующий gate:** iteration 2 may consume `/api/v1/system/status` and the
committed OpenAPI document. Identity, issuing the cookie, and
`GET /api/v1/auth/session` remain blocked on iteration 3; no iteration-2 code
may simulate them with browser bearer storage or direct database access.
```

- [ ] **Step 3: Verify the durable-decision rule is present**

Run:

```bash
grep -F \
  'record it in the appropriate file under `docs/` in the same change' \
  AGENTS.md
```

Expected: exactly one matching instruction. Do not edit `AGENTS.md` if the committed rule is already present.

- [ ] **Step 4: Run documentation and scope guards**

Run:

```bash
test -f docs/api-conventions.md
test -f docs/superpowers/specs/2026-07-23-api-foundation-design.md
grep -F 'GET /api/v1/auth/session' docs/api-conventions.md
grep -F '1 — API foundation | Завершена' docs/aspnetcore-migration-plan.md
git diff --exit-code -- template/
git diff --name-only -- \
  apps/api/src/Template.Domain \
  apps/api/src/Template.Application \
  apps/api/src/Template.Infrastructure \
  apps/web
```

Expected: all `test`/`grep` commands succeed; both `git diff` commands print nothing.

- [ ] **Step 5: Commit durable documentation**

```bash
git add docs/api-conventions.md docs/aspnetcore-migration-plan.md
git commit -m "docs: close API foundation iteration"
```

---

### Task 7: Final Restore, Build, Test, Contract, and Reference Verification

**Files:**
- Modify only if a verification failure proves a defect: files owned by Tasks 1–6.
- Verify only: `Template.sln`, `contracts/openapi/v1.json`, `template/`, and git status.

**Interfaces:**
- Consumes: the complete iteration-1 implementation.
- Produces: fresh acceptance output suitable for the final report; no new surface area.

- [ ] **Step 1: Run required .NET commands from a fresh restore**

Run exactly:

```bash
dotnet restore Template.sln
dotnet build Template.sln --no-restore
dotnet test Template.sln --no-restore
```

Expected: all three commands exit `0`; build has `0 Warning(s)` and `0 Error(s)`; the full xUnit suite has zero failed tests.

- [ ] **Step 2: Re-export and prove deterministic OpenAPI**

Run:

```bash
dotnet build apps/api/src/Template.Api/Template.Api.csproj \
  --no-restore \
  -p:OpenApiGenerateDocuments=true
git diff --exit-code -- contracts/openapi/v1.json
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore \
  --filter FullyQualifiedName~OpenApiContractTests
```

Expected: exporter exits `0`, contract diff is empty, all OpenAPI tests pass.

- [ ] **Step 3: Prove the immutable reference and scope stayed untouched**

Run:

```bash
git diff --exit-code -- template/
git status --short
git diff --check
```

Expected: no `template/` diff; `git diff --check` exits `0`; status contains only intentional iteration-1 files if a final evidence edit has not yet been committed.

- [ ] **Step 4: Inspect the final change surface**

Run:

```bash
git diff --stat HEAD~6..HEAD
git log --oneline --decorate -8
```

Expected: changes are limited to `Template.Api`, `Template.Api.Tests`, OpenAPI contract, and documentation; no Domain/Application/Infrastructure/UI/reference implementation appears.

- [ ] **Step 5: Commit any evidence correction caused by actual command output**

If Task 7 reveals that a documented count or command result differs, change only the factual evidence row, rerun the affected check, and commit:

```bash
git add docs/aspnetcore-migration-plan.md
git commit -m "docs: record API foundation acceptance evidence"
```

If the existing evidence already matches, do not create an empty commit.

- [ ] **Step 6: Prepare the final user report**

Report four concise sections:

1. Implemented: contracts, endpoints, cookie boundary, validation/errors, health, observability, OpenAPI, tests, docs.
2. Verified: public/protected/validation/forbidden/not-found/method/fault/correlation/logging/health/OpenAPI scenarios and exact command results.
3. Reference differences: RFC Problem Details, health envelope, new technical probes/OpenAPI/correlation, no UI parity in this iteration.
4. Outside scope or next blockers: UI/client/E2E in iteration 2; Identity/cookie issuance/session projection/antiforgery in iteration 3; persistence, product domains, API keys, YARP, and Aspire in their registered iterations.
