# VK ClientId-only Parity Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore reference-compatible VK OAuth configuration so ASP.NET Core accepts and registers VK with `ClientId` only.

**Architecture:** Keep the shared external-provider options object, but validate credentials by provider: VK requires one canonical client ID, while Google, GitHub, GitLab, and Yandex retain their client ID/secret pair. Register VK ID as an explicit public PKCE client and never attach a client secret.

**Tech Stack:** .NET 10.0.302, ASP.NET Core/Options, OpenIddict Client WebIntegration 7.6.0, xUnit v3, PostgreSQL-backed API test infrastructure.

## Global Constraints

- `template/` is immutable and is read-only reference evidence.
- Preserve `Domain → Application → Infrastructure → Api`; this fix stays in Infrastructure configuration plus API tests/docs.
- Do not change API/OpenAPI, EF models/migrations, database state, cookies, callbacks, REST behavior, or UI code.
- Write and observe a failing regression test before production changes.
- Never print or commit local OAuth credentials or `appsettings.Local.json`.
- Run the required solution restore, build, and test commands before completion.

## File and responsibility map

- `apps/api/tests/Template.Api.Tests/Accounts/ExternalProviderConfigurationTests.cs`: proves provider-specific validation and VK public registration.
- `apps/api/src/Template.Infrastructure/Authentication/ExternalAuthenticationOptions.cs`: owns provider-aware credential validation/lookups.
- `apps/api/src/Template.Infrastructure/Authentication/OpenIddictClientServiceCollectionExtensions.cs`: maps validated VK client ID to the OpenIddict public PKCE registration.
- `apps/api/src/Template.Api/appsettings.Local.example.json`: documents the safe local configuration shape.
- `docs/authentication-persistence-operations.md`: records the durable operational contract.

---

### Task 1: Accept and register VK with ClientId only

**Files:**
- Modify: `apps/api/tests/Template.Api.Tests/Accounts/ExternalProviderConfigurationTests.cs`
- Modify: `apps/api/src/Template.Infrastructure/Authentication/ExternalAuthenticationOptions.cs`
- Modify: `apps/api/src/Template.Infrastructure/Authentication/OpenIddictClientServiceCollectionExtensions.cs`
- Modify: `apps/api/src/Template.Api/appsettings.Local.example.json`
- Modify: `docs/authentication-persistence-operations.md`

**Interfaces:**
- Consumes: `ExternalProvider.Vk`, `ExternalProviderMetadata.TryFromConfigurationName`, `OpenIddictClientWebIntegrationBuilder.AddVkId`.
- Produces: `ExternalProviderCredentials.HasConfiguredClientId`, `ExternalProviderCredentials.IsValidFor(ExternalProvider)`, and `ExternalAuthenticationOptions.TryGetClientId(ExternalProvider, out string?)`.

- [ ] **Step 1: Write the failing VK regression**

Add this focused test to `ExternalProviderConfigurationTests`:

```csharp
[Fact]
public void VkClientIdOnlyRegistersPublicPkceClientWithoutSecret()
{
    using var services = BuildServices(new Dictionary<string, string?>
    {
        ["ExternalAuthentication:PublicOrigin"] =
            "https://accounts.example.test",
        ["ExternalAuthentication:Providers:Vk:ClientId"] = "vk-id",
        ["ExternalAuthentication:Providers:Vk:ClientSecret"] = ""
    });

    var configured = services
        .GetRequiredService<IOptions<ExternalAuthenticationOptions>>()
        .Value;
    var catalog = services.GetRequiredService<IExternalProviderCatalog>();
    var registration = Assert.Single(services
        .GetRequiredService<IOptions<OpenIddictClientOptions>>()
        .Value
        .Registrations);

    Assert.NotNull(configured);
    Assert.True(catalog.Known.Single(provider =>
        provider.Provider == ExternalProvider.Vk).Configured);
    Assert.Equal("vk", registration.ProviderName);
    Assert.Equal("vk-id", registration.ClientId);
    Assert.Null(registration.ClientSecret);
    Assert.Equal(ClientTypes.Public, registration.ClientType);
    Assert.Contains(
        CodeChallengeMethods.Sha256,
        registration.Configuration!.CodeChallengeMethodsSupported);
}
```

In `CompleteConfiguration`, remove the VK `ClientSecret` entry. In
`CompleteProviderBlocksRegisterExactCallbacksScopesAndSchemes`, replace the
unconditional client-secret assertion with:

```csharp
if (provider == "vk")
{
    Assert.Null(registration.ClientSecret);
    Assert.Equal(ClientTypes.Public, registration.ClientType);
}
else
{
    Assert.False(string.IsNullOrWhiteSpace(registration.ClientSecret));
}
```

Add a fail-closed test for a VK block without a client ID:

```csharp
[Fact]
public void VkProviderBlockWithoutClientIdFailsClosed()
{
    using var services = BuildServices(new Dictionary<string, string?>
    {
        ["ExternalAuthentication:PublicOrigin"] =
            "https://accounts.example.test",
        ["ExternalAuthentication:Providers:Vk:ClientSecret"] = "unused"
    });

    Assert.Throws<OptionsValidationException>(() => services
        .GetRequiredService<IOptions<ExternalAuthenticationOptions>>()
        .Value);
}
```

- [ ] **Step 2: Run the focused test and observe RED**

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore \
  --filter FullyQualifiedName~ExternalProviderConfigurationTests
```

Expected: the client-id-only test fails with the existing non-disclosing
provider-credentials validation error because VK still requires a secret.

- [ ] **Step 3: Implement provider-specific validation**

In `ExternalProviderCredentials`, add:

```csharp
internal bool HasConfiguredClientId => IsConfiguredValue(ClientId);

internal bool IsValidFor(ExternalProvider provider) =>
    provider == ExternalProvider.Vk ? HasConfiguredClientId : IsComplete;
```

In `ExternalAuthenticationOptions`, add:

```csharp
internal bool TryGetClientId(
    ExternalProvider provider,
    out string? clientId)
{
    var credentials = FindCredentials(provider);
    clientId = credentials?.ClientId;
    return credentials is not null && credentials.HasConfiguredClientId;
}
```

Change the validator loop to preserve the known-provider guard and call the
provider-specific rule:

```csharp
if (!ExternalProviderMetadata.TryFromConfigurationName(
        configurationName,
        out var provider)
    || credentials is null
    || !credentials.IsValidFor(provider!))
{
    return ValidateOptionsResult.Fail(InvalidProviderMessage);
}
```

Do not weaken `IsComplete`; all secret-bearing providers continue using it.

- [ ] **Step 4: Register VK as a public client without a secret**

Change `AddVk` to use `TryGetClientId` and omit `SetClientSecret`:

```csharp
if (!options.TryGetClientId(
        Template.Domain.Accounts.ExternalProvider.Vk,
        out var clientId))
{
    return;
}

providers.AddVkId(registration =>
    registration
        .SetRegistrationId("vk")
        .SetProviderName("vk")
        .SetProviderDisplayName("VK")
        .SetClientId(clientId!)
        .SetClientType(ClientTypes.Public)
        .SetRedirectUri(CallbackUri(
            publicOrigin,
            Template.Domain.Accounts.ExternalProvider.Vk))
        .AddScopes(Scopes.Email));
```

- [ ] **Step 5: Run focused GREEN**

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore \
  --filter FullyQualifiedName~ExternalProviderConfigurationTests
```

Expected: all `ExternalProviderConfigurationTests` pass, including the existing
Google incomplete-pair rejection and both new VK cases.

- [ ] **Step 6: Update the public configuration contract**

Change the VK example to:

```json
"Vk": {
  "ClientId": "replace-with-vk-client-id"
}
```

Change the operations guide provider bullets to state that Google, GitHub,
GitLab, and Yandex require a client ID/secret pair, while VK requires only
`ExternalAuthentication__Providers__Vk__ClientId`. State that VK is active with
a canonical non-empty client ID and its OpenIddict registration never sends a
client secret; partial pairs for secret-bearing providers still fail closed.

- [ ] **Step 7: Run full verification**

```bash
dotnet restore Template.sln
dotnet build Template.sln --no-restore
dotnet test Template.sln --no-restore

dotnet build apps/api/src/Template.Api/Template.Api.csproj \
  --no-restore -p:OpenApiGenerateDocuments=true
git diff --exit-code -- contracts/openapi/v1.json

git diff --check
test -z "$(git diff --name-only -- template/)"
test -z "$(git status --porcelain -- template/)"
```

Expected: restore/build/tests pass; OpenAPI remains deterministic and unchanged;
no file in `template/` changes.

- [ ] **Step 8: Commit the verified fix**

```bash
git add \
  apps/api/tests/Template.Api.Tests/Accounts/ExternalProviderConfigurationTests.cs \
  apps/api/src/Template.Infrastructure/Authentication/ExternalAuthenticationOptions.cs \
  apps/api/src/Template.Infrastructure/Authentication/OpenIddictClientServiceCollectionExtensions.cs \
  apps/api/src/Template.Api/appsettings.Local.example.json \
  docs/authentication-persistence-operations.md
git commit -m "fix: support VK client-id-only authentication"
```
