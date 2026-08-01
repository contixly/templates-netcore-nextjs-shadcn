using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Template.Api.Tests.Infrastructure;

namespace Template.Api.Tests;

public sealed class OpenApiContractTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>
{
    private const string CanonicalUuidPattern =
        "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-" +
        "[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$";

    [Fact]
    public async Task TestHostPublishesVersionedOpenApi31Contract()
    {
        using var client = factory.CreateApiClient();

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
        Assert.NotNull(document["paths"]!["/api/v1/auth/capabilities"]);
        Assert.NotNull(document["paths"]!["/api/v1/auth/session"]);
        Assert.NotNull(document["paths"]!["/api/v1/auth/csrf"]);
        Assert.NotNull(document["paths"]!["/api/v1/auth/logout"]);
        Assert.NotNull(
            document["paths"]!["/api/v1/auth/external/{provider}/challenge"]);
        Assert.NotNull(document["paths"]!["/api/v1/account"]);
        Assert.NotNull(document["paths"]!["/api/v1/account/profile"]);
        Assert.NotNull(document["paths"]!["/api/v1/account/connections"]);
        Assert.NotNull(
            document["paths"]!["/api/v1/account/connections/{provider}"]);
        Assert.NotNull(document["paths"]!["/api/v1/account/sessions"]);
        Assert.NotNull(
            document["paths"]!["/api/v1/account/sessions/{sessionId}"]);
        Assert.NotNull(document["paths"]!["/api/local-auth/scenario"]);
        Assert.NotNull(document["paths"]!["/api/local-auth/sign-in"]);
        Assert.NotNull(
            document["paths"]!["/api/local-auth/scenario"]!["delete"]);
        Assert.DoesNotContain(
            document["paths"]!.AsObject().Select(path => path.Key),
            path => path.StartsWith(
                "/api/auth/callback/",
                StringComparison.Ordinal));
        Assert.Null(document["paths"]!["/api/auth/oauth2/callback/yandex"]);
        Assert.Null(document["paths"]!["/api/testing/fault"]);
        Assert.Null(document["paths"]!["/api/testing/forbidden"]);
        Assert.Null(document["paths"]!["/api/testing/nested-validation"]);
        Assert.Null(document["paths"]!["/api/v1/testing/consumer"]);
    }

    [Fact]
    public async Task CookieSchemeAppliesOnlyToProtectedOperation()
    {
        using var client = factory.CreateApiClient();
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
    public async Task CollaborationAndLocalConfirmationPublishExactBrowserContract()
    {
        using var client = factory.CreateApiClient();
        var document = JsonNode.Parse(await client.GetStringAsync(
            "/api/openapi/v1.json",
            TestContext.Current.CancellationToken))!;
        var paths = document["paths"]!;
        var expected = new Dictionary<(string Path, string Method), string>
        {
            [("/api/v1/organizations/{organizationId}/teams", "get")] = "GetTeams",
            [("/api/v1/organizations/{organizationId}/teams", "post")] = "CreateTeam",
            [("/api/v1/organizations/{organizationId}/teams/{teamId}", "patch")] = "UpdateTeam",
            [("/api/v1/organizations/{organizationId}/teams/{teamId}", "delete")] = "DeleteTeam",
            [("/api/v1/organizations/{organizationId}/teams/{teamId}/members", "get")] = "GetTeamMembers",
            [("/api/v1/organizations/{organizationId}/teams/{teamId}/members", "post")] = "AddTeamMember",
            [("/api/v1/organizations/{organizationId}/teams/{teamId}/members/{userId}", "delete")] = "RemoveTeamMember",
            [("/api/v1/organizations/{organizationId}/teams/{teamId}/member-candidates", "get")] = "GetTeamMemberCandidates",
            [("/api/v1/organizations/{organizationId}/invitations", "get")] = "GetOrganizationInvitations",
            [("/api/v1/organizations/{organizationId}/invitations", "post")] = "CreateInvitation",
            [("/api/v1/account/invitations", "get")] = "GetAccountInvitations",
            [("/api/v1/invitations/{invitationId}", "get")] = "GetInvitationDecision",
            [("/api/v1/invitations/{invitationId}/accept", "post")] = "AcceptInvitation",
            [("/api/v1/invitations/{invitationId}/reject", "post")] = "RejectInvitation",
            [("/api/local-auth/confirm-email", "post")] = "ConfirmLocalAutomationEmail"
        };

        foreach (var ((path, method), operationId) in expected)
        {
            var operation = AssertOperation(document, path, method, operationId);
            AssertCookieSecurity(operation);
            Assert.Equal("no-store", operation["x-cache-control"]!.GetValue<string>());
        }

        foreach (var ((path, method), _) in expected.Where(value =>
                     value.Key.Method is "post" or "patch" or "delete"))
        {
            AssertRequiredHeader(paths[path]![method]!, "X-CSRF-TOKEN");
        }

        var confirm = paths["/api/local-auth/confirm-email"]!["post"]!;
        Assert.True(confirm["x-local-only"]!.GetValue<bool>());
        Assert.Equal(new[] { 400, 401, 403, 404, 405, 500 },
            ProblemStatuses(confirm));
    }

    [Fact]
    public async Task CollaborationPublishesStrictFiltersEnumsAndErrorUnions()
    {
        using var client = factory.CreateApiClient();
        var document = JsonNode.Parse(await client.GetStringAsync(
            "/api/openapi/v1.json",
            TestContext.Current.CancellationToken))!;
        var paths = document["paths"]!;
        var schemas = document["components"]!["schemas"]!;

        foreach (var path in new[]
                 {
                     "/api/v1/organizations/{organizationId}/teams",
                     "/api/v1/organizations/{organizationId}/teams/{teamId}/members",
                     "/api/v1/organizations/{organizationId}/teams/{teamId}/member-candidates",
                     "/api/v1/organizations/{organizationId}/invitations",
                     "/api/v1/account/invitations"
                 })
        {
            AssertPagination(paths[path]!["get"]!, 1, 100, 50, "Opaque versioned cursor.");
        }

        var candidates = paths["/api/v1/organizations/{organizationId}/teams/{teamId}/member-candidates"]!["get"]!;
        var query = Assert.Single(candidates["parameters"]!.AsArray(), parameter =>
            parameter!["name"]!.GetValue<string>() == "q");
        Assert.Equal(100, query!["schema"]!["maxLength"]!.GetValue<int>());

        var invitations = paths["/api/v1/organizations/{organizationId}/invitations"]!["get"]!;
        var status = Assert.Single(invitations["parameters"]!.AsArray(), parameter =>
            parameter!["name"]!.GetValue<string>() == "status");
        AssertStringEnum(status!["schema"]!, "pending", "accepted", "rejected", "canceled", "expired");

        foreach (var schemaName in new[] { "TeamNameRequest", "AddTeamMemberRequest", "CreateInvitationRequest" })
        {
            Assert.False(schemas[schemaName]!["additionalProperties"]!.GetValue<bool>());
        }

        AssertRequiredNonNullProperties(schemas["TeamNameRequest"]!, "name");
        AssertRequiredNonNullProperties(schemas["AddTeamMemberRequest"]!, "userId");
        AssertRequiredNonNullProperties(schemas["CreateInvitationRequest"]!, "email", "role");
        AssertStringEnum(schemas["CreateInvitationRequest"]!["properties"]!["role"]!, "member", "admin", "owner");
        AssertStringEnum(schemas["InvitationResponse"]!["properties"]!["role"]!, "member", "admin", "owner");
        AssertStringEnum(schemas["InvitationResponse"]!["properties"]!["status"]!, "pending", "accepted", "rejected", "canceled");
        AssertStringEnum(schemas["InvitationResponse"]!["properties"]!["displayState"]!, "pending", "accepted", "rejected", "canceled", "expired");

        foreach (var (schemaName, propertyName) in new[]
                 {
                     ("AddTeamMemberRequest", "userId"),
                     ("CreateInvitationRequest", "teamId"),
                     ("TeamResponse", "id"),
                     ("TeamResponse", "organizationId"),
                     ("InvitationResponse", "id"),
                     ("InvitationResponse", "organizationId"),
                     ("InvitationResponse", "teamId"),
                     ("AcceptedInvitationResponse", "invitationId"),
                     ("AcceptedInvitationResponse", "organizationId")
                 })
        {
            var property = schemas[schemaName]!["properties"]![propertyName]!;
            Assert.Equal("uuid", property["format"]!.GetValue<string>());
            Assert.Equal(CanonicalUuidPattern, property["pattern"]!.GetValue<string>());
        }

        Assert.Equal(new[] { 400, 401, 403, 404, 405, 409, 500 },
            ProblemStatuses(paths["/api/v1/organizations/{organizationId}/teams"]!["post"]!));
        Assert.Equal(new[] { 400, 401, 403, 404, 405, 409, 429, 500 },
            ProblemStatuses(paths["/api/v1/organizations/{organizationId}/invitations"]!["post"]!));
        Assert.Equal(new[] { 400, 401, 403, 404, 405, 409, 429, 500 },
            ProblemStatuses(paths["/api/v1/invitations/{invitationId}/accept"]!["post"]!));
    }

    [Fact]
    public async Task CollaborationOperationsPublishTheExactBrowserSurface()
    {
        using var client = factory.CreateApiClient();
        var document = JsonNode.Parse(await client.GetStringAsync(
            "/api/openapi/v1.json",
            TestContext.Current.CancellationToken))!;
        var paths = document["paths"]!;
        var expected = new[]
        {
            ("/api/v1/organizations/{organizationId}/teams", "get", "GetTeams", "200", "ApiResponseOfTeamPageResponse", false, false),
            ("/api/v1/organizations/{organizationId}/teams", "post", "CreateTeam", "201", "ApiResponseOfTeamResponse", true, false),
            ("/api/v1/organizations/{organizationId}/teams/{teamId}", "patch", "UpdateTeam", "200", "ApiResponseOfTeamResponse", true, false),
            ("/api/v1/organizations/{organizationId}/teams/{teamId}", "delete", "DeleteTeam", "200", "ApiResponseOfTeamDeletionResponse", true, false),
            ("/api/v1/organizations/{organizationId}/teams/{teamId}/members", "get", "GetTeamMembers", "200", "ApiResponseOfTeamMemberPageResponse", false, false),
            ("/api/v1/organizations/{organizationId}/teams/{teamId}/members", "post", "AddTeamMember", "201", "ApiResponseOfTeamMemberResponse", true, false),
            ("/api/v1/organizations/{organizationId}/teams/{teamId}/members/{userId}", "delete", "RemoveTeamMember", "200", "ApiResponseOfTeamMemberRemovalResponse", true, false),
            ("/api/v1/organizations/{organizationId}/teams/{teamId}/member-candidates", "get", "GetTeamMemberCandidates", "200", "ApiResponseOfTeamCandidatePageResponse", false, false),
            ("/api/v1/organizations/{organizationId}/invitations", "get", "GetOrganizationInvitations", "200", "ApiResponseOfOrganizationInvitationPageResponse", false, false),
            ("/api/v1/organizations/{organizationId}/invitations", "post", "CreateInvitation", "201", "ApiResponseOfInvitationResponse", true, true),
            ("/api/v1/account/invitations", "get", "GetAccountInvitations", "200", "ApiResponseOfAccountInvitationPageResponse", false, false),
            ("/api/v1/invitations/{invitationId}", "get", "GetInvitationDecision", "200", "ApiResponseOfInvitationDecisionResponse", false, false),
            ("/api/v1/invitations/{invitationId}/accept", "post", "AcceptInvitation", "200", "ApiResponseOfAcceptedInvitationResponse", true, true),
            ("/api/v1/invitations/{invitationId}/reject", "post", "RejectInvitation", "200", "ApiResponseOfInvitationDecisionResponse", true, true),
            ("/api/local-auth/confirm-email", "post", "ConfirmLocalAutomationEmail", "200", "ApiResponseOfAuthSessionResponse", true, false)
        };
        var collaborationPaths = paths.AsObject()
            .Where(path => path.Key.Contains("teams", StringComparison.Ordinal) ||
                           path.Key.Contains("invitations", StringComparison.Ordinal) ||
                           path.Key == "/api/local-auth/confirm-email")
            .SelectMany(path => path.Value!.AsObject().Select(method =>
                (Path: path.Key, Method: method.Key)))
            .OrderBy(value => value.Path, StringComparer.Ordinal)
            .ThenBy(value => value.Method, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            expected.Select(value => (Path: value.Item1, Method: value.Item2))
                .OrderBy(value => value.Path, StringComparer.Ordinal)
                .ThenBy(value => value.Method, StringComparer.Ordinal),
            collaborationPaths);

        var operationIds = paths.AsObject().SelectMany(path => path.Value!.AsObject()
            .Where(method => method.Key is "get" or "post" or "patch" or "put" or "delete")
            .Select(method => method.Value!["operationId"]!.GetValue<string>())).ToArray();
        Assert.Equal(operationIds.Length, operationIds.Distinct(StringComparer.Ordinal).Count());

        foreach (var (path, method, operationId, success, envelope, mutation, rateLimited) in expected)
        {
            var operation = AssertOperation(document, path, method, operationId);
            AssertCookieSecurity(operation);
            foreach (var parameter in operation["parameters"]?.AsArray() ?? [])
            {
                if (parameter!["in"]?.GetValue<string>() == "path")
                {
                    Assert.Equal("uuid", parameter["schema"]!["format"]!.GetValue<string>());
                    Assert.Equal(CanonicalUuidPattern, parameter["schema"]!["pattern"]!.GetValue<string>());
                }
            }
            var responses = operation["responses"]!;
            Assert.Equal($"#/components/schemas/{envelope}",
                responses[success]!["content"]!["application/json"]!["schema"]!["$ref"]!.GetValue<string>());
            foreach (var response in responses.AsObject())
            {
                AssertNoStoreResponseHeader(response.Value!);
            }

            if (mutation)
            {
                AssertRequiredHeader(operation, "X-CSRF-TOKEN");
            }

            if (operationId is "CreateTeam" or "UpdateTeam" or "AddTeamMember" or
                "CreateInvitation")
            {
                Assert.True(operation["requestBody"]!["required"]!.GetValue<bool>());
            }
            else
            {
                Assert.Null(operation["requestBody"]);
            }

            if (success == "201")
            {
                Assert.Equal("uri-reference", responses[success]!["headers"]!["Location"]!["schema"]!["format"]!.GetValue<string>());
            }

            if (rateLimited)
            {
                var retryAfter = responses["429"]!["headers"]!["Retry-After"]!;
                Assert.True(retryAfter["required"]!.GetValue<bool>());
                Assert.Equal("string", retryAfter["schema"]!["type"]!.GetValue<string>());
                Assert.Equal("^[0-9]+$", retryAfter["schema"]!["pattern"]!.GetValue<string>());
                Assert.Equal("Decimal integer seconds until the caller may retry.", retryAfter["description"]!.GetValue<string>());
            }
        }

        var schemas = document["components"]!["schemas"]!;
        AssertRequiredNonNullProperties(schemas["CreateInvitationRequest"]!, "email", "role");
        Assert.Contains("null", EnumerateSchemaTypes(schemas["CreateInvitationRequest"]!["properties"]!["teamId"]!));
        Assert.Equal(100, paths["/api/v1/organizations/{organizationId}/teams/{teamId}/member-candidates"]!["get"]!["parameters"]!
            .AsArray().Single(parameter => parameter!["name"]!.GetValue<string>() == "q")!["schema"]!["maxLength"]!.GetValue<int>());
    }

    [Fact]
    public async Task LocalConfirmationDocumentsItsStrictlyLocalPurpose()
    {
        using var client = factory.CreateApiClient();
        var document = JsonNode.Parse(await client.GetStringAsync(
            "/api/openapi/v1.json",
            TestContext.Current.CancellationToken))!;
        var operation = document["paths"]!["/api/local-auth/confirm-email"]!["post"]!;

        Assert.True(operation["x-local-only"]!.GetValue<bool>());
        Assert.Equal(
            "Development/Test only; requires LocalAutomationAuth enabled. Production returns 404. This is not production account verification.",
            operation["description"]!.GetValue<string>());
    }

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

    [Fact]
    public async Task AccountCleanupOperationsPublishReachableConflictProblems()
    {
        using var client = factory.CreateApiClient();
        var document = JsonNode.Parse(await client.GetStringAsync(
            "/api/openapi/v1.json",
            TestContext.Current.CancellationToken))!;
        var paths = document["paths"]!;

        foreach (var operation in new[]
                 {
                     paths["/api/v1/account"]!["delete"]!,
                     paths["/api/local-auth/scenario"]!["delete"]!
                 })
        {
            var conflict = operation["responses"]!["409"];
            Assert.NotNull(conflict);
            AssertSchemaReference(
                conflict,
                "ProblemDetails");
        }
    }

    [Fact]
    public async Task ExternalAndAccountOperationsPublishStableIdsAndCookieBoundaries()
    {
        using var client = factory.CreateApiClient();
        var document = JsonNode.Parse(await client.GetStringAsync(
            "/api/openapi/v1.json",
            TestContext.Current.CancellationToken))!;
        var paths = document["paths"]!;

        var expectedOperations = new Dictionary<(string Path, string Method), string>
        {
            [("/api/v1/auth/external/{provider}/challenge", "post")] =
                "ChallengeExternalAuth",
            [("/api/v1/account", "get")] = "GetAccount",
            [("/api/v1/account/profile", "patch")] = "UpdateAccountProfile",
            [("/api/v1/account/connections", "get")] =
                "GetAccountConnections",
            [("/api/v1/account/connections/{provider}", "delete")] =
                "DisconnectAccountProvider",
            [("/api/v1/account/sessions", "get")] = "GetAccountSessions",
            [("/api/v1/account/sessions/{sessionId}", "delete")] =
                "RevokeAccountSession",
            [("/api/v1/account/sessions/others", "delete")] =
                "RevokeOtherAccountSessions",
            [("/api/v1/account", "delete")] = "DeleteAccount"
        };

        foreach (var ((path, method), operationId) in expectedOperations)
        {
            Assert.Equal(
                operationId,
                paths[path]![method]!["operationId"]!.GetValue<string>());
        }

        Assert.Null(
            paths["/api/v1/auth/capabilities"]!["get"]!["security"]);
        AssertConditionalCookieSecurity(
            paths["/api/v1/auth/external/{provider}/challenge"]!
                ["post"]!);

        foreach (var (path, method) in expectedOperations.Keys.Skip(1))
        {
            AssertCookieSecurity(paths[path]![method]!);
        }
    }

    [Fact]
    public async Task ExternalChallengePublishesStrictIntentProviderAndCsrfContract()
    {
        using var client = factory.CreateApiClient();
        var document = JsonNode.Parse(await client.GetStringAsync(
            "/api/openapi/v1.json",
            TestContext.Current.CancellationToken))!;
        var operation = document["paths"]!
            ["/api/v1/auth/external/{provider}/challenge"]!["post"]!;
        var request = document["components"]!["schemas"]!
            ["ExternalAuthChallengeRequest"]!;

        var description = operation["description"]!.GetValue<string>();
        Assert.Contains("signIn", description, StringComparison.Ordinal);
        Assert.Contains("anonymous", description, StringComparison.Ordinal);
        Assert.Contains("connect", description, StringComparison.Ordinal);
        Assert.Contains("cookieAuth", description, StringComparison.Ordinal);
        Assert.True(operation["requestBody"]!["required"]!.GetValue<bool>());
        Assert.Equal(
            ["application/json"],
            operation["requestBody"]!["content"]!.AsObject()
                .Select(content => content.Key)
                .ToArray());
        var csrf = Assert.Single(
            operation["parameters"]!.AsArray(),
            parameter => parameter!["name"]!.GetValue<string>() ==
                "X-CSRF-TOKEN");
        Assert.Equal("header", csrf!["in"]!.GetValue<string>());
        Assert.True(csrf["required"]!.GetValue<bool>());

        var provider = Assert.Single(
            operation["parameters"]!.AsArray(),
            parameter => parameter!["name"]!.GetValue<string>() == "provider");
        Assert.Equal("path", provider!["in"]!.GetValue<string>());
        Assert.True(provider["required"]!.GetValue<bool>());
        AssertStringEnum(
            provider["schema"]!,
            "google",
            "github",
            "gitlab",
            "vk",
            "yandex");

        Assert.False(request["additionalProperties"]!.GetValue<bool>());
        AssertRequiredNonNullProperties(request, "intent");
        AssertStringEnum(
            request["properties"]!["intent"]!,
            "signIn",
            "connect");
        AssertStringEnum(
            document["components"]!["schemas"]!["ExternalAuthIntent"]!,
            "signIn",
            "connect");
        Assert.DoesNotContain(
            "returnUrl",
            request["required"]?.AsArray()
                .Select(item => item!.GetValue<string>())
                .ToArray() ?? []);
        var returnUrl = request["properties"]!["returnUrl"]!;
        Assert.Contains(
            "null",
            EnumerateSchemaTypes(returnUrl));
        Assert.Equal(4096, returnUrl["maxLength"]!.GetValue<int>());
        Assert.Equal("^/(?!/)", returnUrl["pattern"]!.GetValue<string>());
        Assert.True(returnUrl["x-safe-return-path"]!.GetValue<bool>());

        var authorizationUrl = document["components"]!["schemas"]!
            ["ExternalAuthChallengeResponse"]!["properties"]!
            ["authorizationUrl"]!;
        Assert.Equal("uri", authorizationUrl["format"]!.GetValue<string>());
        Assert.Equal(
            "https",
            authorizationUrl["x-uri-scheme"]!.GetValue<string>());
    }

    [Fact]
    public async Task AccountMutationsPublishStrictRequestAndCsrfContracts()
    {
        using var client = factory.CreateApiClient();
        var document = JsonNode.Parse(await client.GetStringAsync(
            "/api/openapi/v1.json",
            TestContext.Current.CancellationToken))!;
        var paths = document["paths"]!;
        var schemas = document["components"]!["schemas"]!;

        var profile = schemas["UpdateProfileRequest"]!;
        Assert.False(profile["additionalProperties"]!.GetValue<bool>());
        AssertRequiredNonNullProperties(profile, "displayName");
        var displayName = profile["properties"]!["displayName"]!;
        Assert.Null(displayName["minLength"]);
        Assert.Null(displayName["maxLength"]);
        Assert.Equal(
            2,
            displayName["x-trimmed-min-length"]!.GetValue<int>());
        Assert.Equal(
            50,
            displayName["x-trimmed-max-length"]!.GetValue<int>());
        Assert.Contains(
            "control",
            displayName["description"]!.GetValue<string>(),
            StringComparison.OrdinalIgnoreCase);

        var deletion = schemas["DeleteAccountRequest"]!;
        Assert.False(deletion["additionalProperties"]!.GetValue<bool>());
        AssertRequiredNonNullProperties(deletion, "confirmationEmail");
        var confirmationEmail = deletion["properties"]!["confirmationEmail"]!;
        Assert.Null(confirmationEmail["maxLength"]);
        Assert.Null(confirmationEmail["format"]);
        Assert.Equal(
            254,
            confirmationEmail["x-trimmed-max-length"]!.GetValue<int>());
        Assert.Equal(
            "email",
            confirmationEmail["x-trimmed-format"]!.GetValue<string>());

        foreach (var (path, method) in new[]
                 {
                     ("/api/v1/account/profile", "patch"),
                     ("/api/v1/account/connections/{provider}", "delete"),
                     ("/api/v1/account/sessions/{sessionId}", "delete"),
                     ("/api/v1/account/sessions/others", "delete"),
                     ("/api/v1/account", "delete")
                 })
        {
            var csrf = Assert.Single(
                paths[path]![method]!["parameters"]!.AsArray(),
                parameter => parameter!["name"]!.GetValue<string>() ==
                    "X-CSRF-TOKEN");
            Assert.Equal("header", csrf!["in"]!.GetValue<string>());
            Assert.True(csrf["required"]!.GetValue<bool>());
        }

        Assert.True(
            paths["/api/v1/account/profile"]!["patch"]!
                ["requestBody"]!["required"]!.GetValue<bool>());
        Assert.True(
            paths["/api/v1/account"]!["delete"]!
                ["requestBody"]!["required"]!.GetValue<bool>());
    }

    [Fact]
    public async Task AccountPathAndPaginationParametersPublishClosedLimits()
    {
        using var client = factory.CreateApiClient();
        var document = JsonNode.Parse(await client.GetStringAsync(
            "/api/openapi/v1.json",
            TestContext.Current.CancellationToken))!;
        var paths = document["paths"]!;

        var disconnectProvider = Assert.Single(
            paths["/api/v1/account/connections/{provider}"]!
                ["delete"]!["parameters"]!.AsArray(),
            parameter => parameter!["name"]!.GetValue<string>() == "provider");
        AssertStringEnum(
            disconnectProvider!["schema"]!,
            "google",
            "github",
            "gitlab",
            "vk",
            "yandex");

        var parameters = paths["/api/v1/account/sessions"]!
            ["get"]!["parameters"]!.AsArray();
        var limit = Assert.Single(
            parameters,
            parameter => parameter!["name"]!.GetValue<string>() == "limit");
        Assert.Equal(1, limit!["schema"]!["minimum"]!.GetValue<int>());
        Assert.Equal(100, limit["schema"]!["maximum"]!.GetValue<int>());
        Assert.Equal(20, limit["schema"]!["default"]!.GetValue<int>());

        var cursor = Assert.Single(
            parameters,
            parameter => parameter!["name"]!.GetValue<string>() == "cursor");
        Assert.NotEqual(true, cursor!["required"]?.GetValue<bool>());

        var sessionId = Assert.Single(
            paths["/api/v1/account/sessions/{sessionId}"]!
                ["delete"]!["parameters"]!.AsArray(),
            parameter => parameter!["name"]!.GetValue<string>() == "sessionId");
        Assert.Equal(
            "uuid",
            sessionId!["schema"]!["format"]!.GetValue<string>());
    }

    [Fact]
    public async Task AccountSuccessModelsPublishRequiredFieldsAndClosedEnums()
    {
        using var client = factory.CreateApiClient();
        var document = JsonNode.Parse(await client.GetStringAsync(
            "/api/openapi/v1.json",
            TestContext.Current.CancellationToken))!;
        var schemas = document["components"]!["schemas"]!;

        AssertRequiredNonNullProperties(
            schemas["AuthProviderResponse"]!,
            "id",
            "displayName");
        Assert.Equal(
            ["id", "displayName"],
            schemas["AuthProviderResponse"]!["properties"]!.AsObject()
                .Select(property => property.Key)
                .ToArray());
        AssertStringEnum(
            schemas["AuthProviderResponse"]!["properties"]!["id"]!,
            "google",
            "github",
            "gitlab",
            "vk",
            "yandex");
        AssertRequiredNonNullProperties(
            schemas["ExternalAuthChallengeResponse"]!,
            "authorizationUrl");
        AssertRequiredNonNullProperties(
            schemas["AccountEmailResponse"]!,
            "email",
            "isPrimary",
            "providers");
        AssertStringEnum(
            schemas["AccountEmailResponse"]!["properties"]!
                ["providers"]!["items"]!,
            "google",
            "github",
            "gitlab",
            "vk",
            "yandex");
        AssertRequiredProperties(
            schemas["AccountResponse"]!,
            "id",
            "displayName",
            "primaryEmail",
            "imageUrl",
            "createdAt",
            "verifiedEmails");
        AssertRequiredNonNullProperties(
            schemas["AccountResponse"]!,
            "id",
            "displayName",
            "primaryEmail",
            "createdAt",
            "verifiedEmails");
        Assert.Contains(
            "null",
            EnumerateSchemaTypes(
                schemas["AccountResponse"]!["properties"]!["imageUrl"]!));
        var imageUrl = schemas["AccountResponse"]!["properties"]!["imageUrl"]!;
        Assert.Equal("uri", imageUrl["format"]!.GetValue<string>());
        Assert.Equal("https", imageUrl["x-uri-scheme"]!.GetValue<string>());
        AssertRequiredProperties(
            schemas["AccountConnectionResponse"]!,
            "provider",
            "displayName",
            "configured",
            "connected",
            "email",
            "connectedAt",
            "lastUsedAt",
            "isCurrentAuthenticationMethod",
            "canConnect",
            "canDisconnect",
            "disabledReason");
        AssertRequiredNonNullProperties(
            schemas["AccountConnectionResponse"]!,
            "provider",
            "displayName",
            "configured",
            "connected",
            "isCurrentAuthenticationMethod",
            "canConnect",
            "canDisconnect");
        AssertStringEnum(
            schemas["AccountConnectionResponse"]!["properties"]!["provider"]!,
            "google",
            "github",
            "gitlab",
            "vk",
            "yandex");
        var disabledReason = schemas["AccountConnectionResponse"]!
            ["properties"]!["disabledReason"]!;
        Assert.Contains("null", EnumerateSchemaTypes(disabledReason));
        var disabledReasons = disabledReason["enum"]!.AsArray();
        Assert.Equal("external_connection_required", disabledReasons[0]!.GetValue<string>());
        Assert.Null(disabledReasons[1]);
        AssertRequiredProperties(
            schemas["AccountSessionsResponse"]!,
            "items",
            "nextCursor");
        AssertRequiredNonNullProperties(
            schemas["AccountSessionsResponse"]!,
            "items");
        Assert.Contains(
            "null",
            EnumerateSchemaTypes(
                schemas["AccountSessionsResponse"]!["properties"]!
                    ["nextCursor"]!));
        AssertRequiredProperties(
            schemas["AccountSessionResponse"]!,
            "id",
            "createdAt",
            "lastSeenAt",
            "expiresAt",
            "isCurrent",
            "authenticationMethod",
            "ipAddress",
            "userAgent");
        AssertRequiredNonNullProperties(
            schemas["AccountSessionResponse"]!,
            "id",
            "createdAt",
            "lastSeenAt",
            "expiresAt",
            "isCurrent",
            "authenticationMethod");
        AssertStringEnum(
            schemas["AccountSessionResponse"]!["properties"]!
                ["authenticationMethod"]!,
            "local",
            "google",
            "github",
            "gitlab",
            "vk",
            "yandex");
        Assert.Equal(
            512,
            schemas["AccountSessionResponse"]!["properties"]!
                ["userAgent"]!["maxLength"]!.GetValue<int>());
        AssertRequiredNonNullProperties(
            schemas["AccountDisconnectionResponse"]!,
            "provider");
        AssertRequiredNonNullProperties(
            schemas["AccountSessionRevocationResponse"]!,
            "sessionId");
        AssertRequiredNonNullProperties(
            schemas["AccountSessionsRevocationResponse"]!,
            "revokedCount");
        var revokedCount = schemas["AccountSessionsRevocationResponse"]!
            ["properties"]!["revokedCount"]!;
        Assert.Equal("integer", revokedCount["type"]!.GetValue<string>());
        Assert.Equal("int32", revokedCount["format"]!.GetValue<string>());
        Assert.Equal(0, revokedCount["minimum"]!.GetValue<int>());
        AssertRequiredNonNullProperties(
            schemas["AccountDeletionResponse"]!,
            "deleted");
    }

    [Fact]
    public async Task ExternalAndAccountOperationsPublishSuccessEnvelopesAndProblemResponses()
    {
        using var client = factory.CreateApiClient();
        var document = JsonNode.Parse(await client.GetStringAsync(
            "/api/openapi/v1.json",
            TestContext.Current.CancellationToken))!;
        var paths = document["paths"]!;

        var operations = new[]
        {
            new
            {
                Path = "/api/v1/auth/external/{provider}/challenge",
                Method = "post",
                Envelope = "ApiResponseOfExternalAuthChallengeResponse",
                ProblemStatuses = new[] { "400", "401", "403", "404", "405", "409", "429", "500" },
                BadRequestIsUnion = true
            },
            new
            {
                Path = "/api/v1/account",
                Method = "get",
                Envelope = "ApiResponseOfAccountResponse",
                ProblemStatuses = new[] { "401", "403", "404", "405", "500" },
                BadRequestIsUnion = false
            },
            new
            {
                Path = "/api/v1/account/profile",
                Method = "patch",
                Envelope = "ApiResponseOfAccountResponse",
                ProblemStatuses = new[] { "400", "401", "403", "404", "405", "500" },
                BadRequestIsUnion = true
            },
            new
            {
                Path = "/api/v1/account/connections",
                Method = "get",
                Envelope = "ApiResponseOfAccountConnectionsResponse",
                ProblemStatuses = new[] { "401", "403", "404", "405", "500" },
                BadRequestIsUnion = false
            },
            new
            {
                Path = "/api/v1/account/connections/{provider}",
                Method = "delete",
                Envelope = "ApiResponseOfAccountDisconnectionResponse",
                ProblemStatuses = new[] { "400", "401", "403", "404", "405", "409", "500" },
                BadRequestIsUnion = false
            },
            new
            {
                Path = "/api/v1/account/sessions",
                Method = "get",
                Envelope = "ApiResponseOfAccountSessionsResponse",
                ProblemStatuses = new[] { "400", "401", "403", "404", "405", "500" },
                BadRequestIsUnion = true
            },
            new
            {
                Path = "/api/v1/account/sessions/{sessionId}",
                Method = "delete",
                Envelope = "ApiResponseOfAccountSessionRevocationResponse",
                ProblemStatuses = new[] { "400", "401", "403", "404", "405", "409", "500" },
                BadRequestIsUnion = false
            },
            new
            {
                Path = "/api/v1/account/sessions/others",
                Method = "delete",
                Envelope = "ApiResponseOfAccountSessionsRevocationResponse",
                ProblemStatuses = new[] { "400", "401", "403", "404", "405", "500" },
                BadRequestIsUnion = false
            },
            new
            {
                Path = "/api/v1/account",
                Method = "delete",
                Envelope = "ApiResponseOfAccountDeletionResponse",
                ProblemStatuses = new[] { "400", "401", "403", "404", "405", "500" },
                BadRequestIsUnion = true
            }
        };

        foreach (var expected in operations)
        {
            var responses = paths[expected.Path]![expected.Method]!["responses"]!;
            Assert.Equal(
                $"#/components/schemas/{expected.Envelope}",
                responses["200"]!["content"]!["application/json"]!
                    ["schema"]!["$ref"]!.GetValue<string>());

            foreach (var status in expected.ProblemStatuses)
            {
                Assert.NotNull(responses[status]);
                if (status == "400" && expected.BadRequestIsUnion)
                {
                    AssertSchemaUnion(
                        responses[status]!,
                        "ProblemDetails",
                        "HttpValidationProblemDetails");
                }
                else
                {
                    AssertSchemaReference(
                        responses[status]!,
                        "ProblemDetails");
                }
            }
        }
    }

    [Fact]
    public async Task OrganizationOperationsPublishStableIdsCookieCsrfAndExactResponses()
    {
        using var client = factory.CreateApiClient();
        var document = JsonNode.Parse(await client.GetStringAsync(
            "/api/openapi/v1.json",
            TestContext.Current.CancellationToken))!;

        var operations = new[]
        {
            new
            {
                Path = "/api/v1/organizations",
                Method = "get",
                OperationId = "GetOrganizations",
                SuccessStatus = "200",
                Envelope = "ApiResponseOfOrganizationPageResponse",
                ProblemStatuses = new[] { "400", "401", "405", "500" },
                Mutation = false,
                BadRequestIsUnion = true
            },
            new
            {
                Path = "/api/v1/organizations",
                Method = "post",
                OperationId = "CreateOrganization",
                SuccessStatus = "201",
                Envelope = "ApiResponseOfOrganizationDetailResponse",
                ProblemStatuses = new[] { "400", "401", "405", "409", "500" },
                Mutation = true,
                BadRequestIsUnion = true
            },
            new
            {
                Path = "/api/v1/organizations/by-key/{organizationKey}",
                Method = "get",
                OperationId = "GetOrganizationByKey",
                SuccessStatus = "200",
                Envelope = "ApiResponseOfOrganizationDetailResponse",
                ProblemStatuses = new[] { "401", "404", "405", "409", "500" },
                Mutation = false,
                BadRequestIsUnion = false
            },
            new
            {
                Path = "/api/v1/organizations/{organizationId}",
                Method = "patch",
                OperationId = "UpdateOrganization",
                SuccessStatus = "200",
                Envelope = "ApiResponseOfOrganizationDetailResponse",
                ProblemStatuses = new[] { "400", "401", "403", "404", "405", "409", "500" },
                Mutation = true,
                BadRequestIsUnion = true
            },
            new
            {
                Path = "/api/v1/organizations/{organizationId}",
                Method = "delete",
                OperationId = "DeleteOrganization",
                SuccessStatus = "200",
                Envelope = "ApiResponseOfOrganizationDeletionResponse",
                ProblemStatuses = new[] { "400", "401", "403", "404", "405", "409", "500" },
                Mutation = true,
                BadRequestIsUnion = true
            },
            new
            {
                Path = "/api/v1/auth/session/active-organization",
                Method = "put",
                OperationId = "SetActiveOrganization",
                SuccessStatus = "200",
                Envelope = "ApiResponseOfActiveOrganizationResponse",
                ProblemStatuses = new[] { "400", "401", "404", "405", "409", "500" },
                Mutation = true,
                BadRequestIsUnion = true
            },
            new
            {
                Path = "/api/v1/organizations/{organizationId}/members",
                Method = "get",
                OperationId = "GetOrganizationMembers",
                SuccessStatus = "200",
                Envelope = "ApiResponseOfOrganizationMemberPageResponse",
                ProblemStatuses = new[] { "400", "401", "404", "405", "409", "500" },
                Mutation = false,
                BadRequestIsUnion = true
            },
            new
            {
                Path = "/api/v1/organizations/{organizationId}/members",
                Method = "post",
                OperationId = "AddOrganizationMember",
                SuccessStatus = "201",
                Envelope = "ApiResponseOfOrganizationMemberResponse",
                ProblemStatuses = new[] { "400", "401", "403", "404", "405", "409", "500" },
                Mutation = true,
                BadRequestIsUnion = true
            },
            new
            {
                Path = "/api/v1/organizations/{organizationId}/members/{memberId}",
                Method = "patch",
                OperationId = "UpdateOrganizationMemberRole",
                SuccessStatus = "200",
                Envelope = "ApiResponseOfOrganizationMemberResponse",
                ProblemStatuses = new[] { "400", "401", "403", "404", "405", "409", "500" },
                Mutation = true,
                BadRequestIsUnion = true
            }
        };

        foreach (var expected in operations)
        {
            var operation = AssertOperation(
                document,
                expected.Path,
                expected.Method,
                expected.OperationId);
            AssertCookieSecurity(operation);
            if (expected.Mutation)
            {
                AssertRequiredHeader(operation, "X-CSRF-TOKEN");
                Assert.True(
                    operation["requestBody"]!["required"]!.GetValue<bool>());
                Assert.Equal(
                    ["application/json"],
                    operation["requestBody"]!["content"]!.AsObject()
                        .Select(content => content.Key)
                        .ToArray());
            }

            var responses = operation["responses"]!;
            Assert.Equal(
                new[] { expected.SuccessStatus }
                    .Concat(expected.ProblemStatuses)
                    .Order(StringComparer.Ordinal),
                responses.AsObject().Select(response => response.Key)
                    .Order(StringComparer.Ordinal));
            Assert.Equal(
                $"#/components/schemas/{expected.Envelope}",
                responses[expected.SuccessStatus]!["content"]!["application/json"]!
                    ["schema"]!["$ref"]!.GetValue<string>());

            foreach (var status in expected.ProblemStatuses)
            {
                if (status == "400" && expected.BadRequestIsUnion)
                {
                    AssertSchemaUnion(
                        responses[status]!,
                        "ProblemDetails",
                        "HttpValidationProblemDetails");
                }
                else
                {
                    AssertSchemaReference(
                        responses[status]!,
                        "ProblemDetails");
                }
            }
        }
    }

    [Fact]
    public async Task OrganizationMemberListPublishesItsConcurrencyConflictButNoForbiddenResponse()
    {
        using var client = factory.CreateApiClient();
        var document = JsonNode.Parse(await client.GetStringAsync(
            "/api/openapi/v1.json",
            TestContext.Current.CancellationToken))!;
        var responses = AssertOperation(
            document,
            "/api/v1/organizations/{organizationId}/members",
            "get",
            "GetOrganizationMembers")["responses"]!;

        Assert.NotNull(responses["409"]);
        AssertSchemaReference(responses["409"]!, "ProblemDetails");
        Assert.Null(responses["403"]);
    }

    [Fact]
    public async Task OrganizationCreatedResponsesPublishRequiredLocationHeaders()
    {
        using var client = factory.CreateApiClient();
        var document = JsonNode.Parse(await client.GetStringAsync(
            "/api/openapi/v1.json",
            TestContext.Current.CancellationToken))!;

        foreach (var operation in new[]
                 {
                     AssertOperation(
                         document,
                         "/api/v1/organizations",
                         "post",
                         "CreateOrganization"),
                     AssertOperation(
                         document,
                         "/api/v1/organizations/{organizationId}/members",
                         "post",
                         "AddOrganizationMember")
                 })
        {
            var location = operation["responses"]!["201"]!["headers"]!["Location"]!;
            Assert.True(location["required"]!.GetValue<bool>());
            Assert.Equal(
                "string",
                location["schema"]!["type"]!.GetValue<string>());
            Assert.Equal(
                "uri-reference",
                location["schema"]!["format"]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task OrganizationKeyPublishesUuidOrCanonicalSlugAlternatives()
    {
        using var client = factory.CreateApiClient();
        var document = JsonNode.Parse(await client.GetStringAsync(
            "/api/openapi/v1.json",
            TestContext.Current.CancellationToken))!;
        var operation = AssertOperation(
            document,
            "/api/v1/organizations/by-key/{organizationKey}",
            "get",
            "GetOrganizationByKey");
        var parameter = Assert.Single(
            operation["parameters"]!.AsArray(),
            value => value!["name"]!.GetValue<string>() == "organizationKey");
        var alternatives = parameter!["schema"]!["oneOf"]!.AsArray();
        Assert.Equal(2, alternatives.Count);

        var uuid = alternatives[0]!;
        Assert.Equal("string", uuid["type"]!.GetValue<string>());
        Assert.Equal("uuid", uuid["format"]!.GetValue<string>());
        const string canonicalUuidPattern =
            "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-" +
            "[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$";
        Assert.Equal(
            canonicalUuidPattern,
            uuid["pattern"]!.GetValue<string>());

        var slug = alternatives[1]!;
        Assert.Equal("string", slug["type"]!.GetValue<string>());
        Assert.Equal(1, slug["minLength"]!.GetValue<int>());
        Assert.Equal(64, slug["maxLength"]!.GetValue<int>());
        Assert.Equal(
            "^[a-z0-9]+(?:-[a-z0-9]+)*$",
            slug["pattern"]!.GetValue<string>());
        Assert.Equal(
            "string",
            slug["not"]!["type"]!.GetValue<string>());
        Assert.Equal(
            canonicalUuidPattern,
            slug["not"]!["pattern"]!.GetValue<string>());
        const string uuidShapedKey = "01900000-0000-7000-8000-000000000001";
        Assert.Matches(canonicalUuidPattern, uuidShapedKey);
        Assert.Matches(
            slug["pattern"]!.GetValue<string>(),
            uuidShapedKey);
        Assert.Matches(
            slug["not"]!["pattern"]!.GetValue<string>(),
            uuidShapedKey);
        Assert.Equal(
            "Canonical organization UUID or lowercase non-UUID-shaped slug. " +
            "UUID keys resolve only by organization ID. The response canonicalKey " +
            "is always the preferred slug.",
            parameter["schema"]!["description"]!.GetValue<string>());
    }

    [Fact]
    public async Task OrganizationRequestsPublishStrictValidationAndClosedRoles()
    {
        using var client = factory.CreateApiClient();
        var document = JsonNode.Parse(await client.GetStringAsync(
            "/api/openapi/v1.json",
            TestContext.Current.CancellationToken))!;
        var schemas = document["components"]!["schemas"]!;

        foreach (var requestName in new[]
                 {
                     "CreateOrganizationRequest",
                     "UpdateOrganizationRequest",
                     "DeleteOrganizationRequest",
                     "SetActiveOrganizationRequest",
                     "AddOrganizationMemberRequest",
                     "UpdateOrganizationMemberRoleRequest"
                 })
        {
            Assert.False(
                schemas[requestName]!["additionalProperties"]!.GetValue<bool>());
        }

        var create = schemas["CreateOrganizationRequest"]!;
        AssertRequiredNonNullProperties(create, "name");
        AssertTrimmedString(
            create["properties"]!["name"]!,
            minimum: 1,
            maximum: 50);
        var organizationNamePattern = create["properties"]!["name"]!
            ["x-trimmed-pattern"]!.GetValue<string>();
        Assert.Equal(
            """^[\p{L}\p{Nd} _-]+$""",
            organizationNamePattern);
        Assert.Matches(organizationNamePattern, "Workspace ٣");
        Assert.DoesNotMatch(organizationNamePattern, "Workspace Ⅻ");
        Assert.DoesNotMatch(organizationNamePattern, "Workspace ²");

        var update = schemas["UpdateOrganizationRequest"]!;
        Assert.Equal(1, update["minProperties"]!.GetValue<int>());
        var nonNullAlternatives = update["anyOf"]!.AsArray();
        Assert.Equal(
            ["name", "slug", "allowedEmailDomains"],
            nonNullAlternatives.Select(
                alternative => Assert.Single(
                    alternative!["required"]!.AsArray())!.GetValue<string>()));
        foreach (var alternative in nonNullAlternatives)
        {
            var propertyName = Assert.Single(
                alternative!["required"]!.AsArray())!.GetValue<string>();
            Assert.DoesNotContain(
                "null",
                EnumerateSchemaTypes(
                    alternative["properties"]![propertyName]!));
        }
        AssertTrimmedString(
            update["properties"]!["name"]!,
            minimum: 1,
            maximum: 50);
        AssertTrimmedString(
            update["properties"]!["slug"]!,
            minimum: 1,
            maximum: 64);
        Assert.Equal(
            "^[a-z0-9]+(?:-[a-z0-9]+)*$",
            update["properties"]!["slug"]!["x-trimmed-pattern"]!
                .GetValue<string>());
        Assert.Equal(
            CanonicalUuidPattern,
            update["properties"]!["slug"]!["x-trimmed-not-pattern"]!
                .GetValue<string>());
        var allowedEmailDomains =
            update["properties"]!["allowedEmailDomains"]!;
        Assert.Equal(
            100,
            allowedEmailDomains["maxItems"]!.GetValue<int>());
        var domain = allowedEmailDomains["items"]!;
        Assert.Null(domain["maxLength"]);
        Assert.Equal(
            253,
            domain["x-trimmed-max-length"]!.GetValue<int>());
        Assert.Equal(
            "email-domain",
            domain["x-trimmed-format"]!.GetValue<string>());

        var delete = schemas["DeleteOrganizationRequest"]!;
        AssertRequiredNonNullProperties(delete, "confirmationName");
        Assert.Equal(
            1,
            delete["properties"]!["confirmationName"]!["minLength"]!
                .GetValue<int>());
        Assert.Equal(
            50,
            delete["properties"]!["confirmationName"]!["maxLength"]!
                .GetValue<int>());

        AssertRequiredNonNullProperties(
            schemas["SetActiveOrganizationRequest"]!,
            "organizationId");
        Assert.Equal(
            "uuid",
            schemas["SetActiveOrganizationRequest"]!["properties"]!
                ["organizationId"]!["format"]!.GetValue<string>());

        var addMember = schemas["AddOrganizationMemberRequest"]!;
        AssertRequiredNonNullProperties(addMember, "userId", "role");
        Assert.Equal(
            "uuid",
            addMember["properties"]!["userId"]!["format"]!.GetValue<string>());
        AssertStringEnum(
            addMember["properties"]!["role"]!,
            "member",
            "admin",
            "owner");
        AssertStringEnum(
            schemas["UpdateOrganizationMemberRoleRequest"]!["properties"]!["role"]!,
            "member",
            "admin",
            "owner");
        AssertRequiredNonNullProperties(
            schemas["UpdateOrganizationMemberRoleRequest"]!,
            "role");
    }

    [Fact]
    public async Task OrganizationProjectionsPaginationAndPathsPublishClosedSchemas()
    {
        using var client = factory.CreateApiClient();
        var document = JsonNode.Parse(await client.GetStringAsync(
            "/api/openapi/v1.json",
            TestContext.Current.CancellationToken))!;
        var schemas = document["components"]!["schemas"]!;

        foreach (var (schemaName, roleProperty) in new[]
                 {
                     ("OrganizationSummaryResponse", "currentRole"),
                     ("OrganizationDetailResponse", "currentRole"),
                     ("OrganizationMemberResponse", "role")
                 })
        {
            AssertStringEnum(
                schemas[schemaName]!["properties"]![roleProperty]!,
                "member",
                "admin",
                "owner");
        }

        foreach (var schemaName in new[]
                 {
                     "OrganizationSummaryResponse",
                     "OrganizationDetailResponse"
                 })
        {
            var properties = schemas[schemaName]!["properties"]!;
            Assert.Equal("uuid", properties["id"]!["format"]!.GetValue<string>());
            Assert.Equal(
                "^[a-z0-9]+(?:-[a-z0-9]+)*$",
                properties["slug"]!["pattern"]!.GetValue<string>());
            Assert.Equal(64, properties["slug"]!["maxLength"]!.GetValue<int>());
            Assert.Equal(
                CanonicalUuidPattern,
                properties["slug"]!["not"]!["pattern"]!.GetValue<string>());
            Assert.Equal(
                "^[a-z0-9]+(?:-[a-z0-9]+)*$",
                properties["canonicalKey"]!["pattern"]!.GetValue<string>());
            Assert.Equal(
                CanonicalUuidPattern,
                properties["canonicalKey"]!["not"]!["pattern"]!
                    .GetValue<string>());
        }

        var member = schemas["OrganizationMemberResponse"]!["properties"]!;
        Assert.Equal("uuid", member["id"]!["format"]!.GetValue<string>());
        Assert.Equal("uuid", member["userId"]!["format"]!.GetValue<string>());
        Assert.Equal("email", member["email"]!["format"]!.GetValue<string>());
        Assert.Equal("uri", member["imageUrl"]!["format"]!.GetValue<string>());
        Assert.Equal("https", member["imageUrl"]!["x-uri-scheme"]!.GetValue<string>());

        AssertPagination(
            AssertOperation(
                document,
                "/api/v1/organizations",
                "get",
                "GetOrganizations"),
            minimum: 1,
            maximum: 100,
            defaultValue: 50,
            cursorDescription:
                "Opaque versioned cursor returned by the preceding page. " +
                "Organizations are ordered by the actor membership's immutable " +
                "joinedAt and membership id.");
        AssertPagination(
            AssertOperation(
                document,
                "/api/v1/organizations/{organizationId}/members",
                "get",
                "GetOrganizationMembers"),
            minimum: 1,
            maximum: 100,
            defaultValue: 50,
            cursorDescription:
                "Opaque versioned cursor returned by the preceding page. " +
                "Members are ordered by immutable joinedAt and member id.");

        foreach (var (path, method, parameterName) in new[]
                 {
                     ("/api/v1/organizations/{organizationId}", "patch", "organizationId"),
                     ("/api/v1/organizations/{organizationId}", "delete", "organizationId"),
                     ("/api/v1/organizations/{organizationId}/members", "get", "organizationId"),
                     ("/api/v1/organizations/{organizationId}/members", "post", "organizationId"),
                     ("/api/v1/organizations/{organizationId}/members/{memberId}", "patch", "organizationId"),
                     ("/api/v1/organizations/{organizationId}/members/{memberId}", "patch", "memberId")
                 })
        {
            var parameter = Assert.Single(
                document["paths"]![path]![method]!["parameters"]!.AsArray(),
                value => value!["name"]!.GetValue<string>() == parameterName);
            Assert.Equal(
                "uuid",
                parameter!["schema"]!["format"]!.GetValue<string>());
        }

        var problem = schemas["ProblemDetails"]!["properties"]!;
        Assert.Contains("null", EnumerateSchemaTypes(problem["email"]!));
        Assert.Equal("email", problem["email"]!["format"]!.GetValue<string>());
        Assert.Contains("null", EnumerateSchemaTypes(problem["emailDomain"]!));
        Assert.Contains(
            "null",
            EnumerateSchemaTypes(problem["allowedEmailDomains"]!));
        Assert.Equal(
            "string",
            problem["allowedEmailDomains"]!["items"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task ProblemDetailsPublishesEveryStablePublicCode()
    {
        using var client = factory.CreateApiClient();
        var document = JsonNode.Parse(await client.GetStringAsync(
            "/api/openapi/v1.json",
            TestContext.Current.CancellationToken))!;
        var code = document["components"]!["schemas"]!["ProblemDetails"]!
            ["properties"]!["code"]!;

        AssertStringEnum(
            code,
            "invalid_request",
            "validation_failed",
            "unauthorized",
            "forbidden",
            "not_found",
            "method_not_allowed",
            "internal_error",
            "antiforgery_failed",
            "local_auth_invalid_credentials",
            "local_auth_user_required",
            "local_auth_disabled",
            "local_auth_user_exists",
            "rate_limited",
            "invalid_return_url",
            "external_provider_not_configured",
            "already_authenticated",
            "external_auth_failed",
            "external_email_required",
            "external_email_unverified",
            "external_identity_conflict",
            "external_email_conflict",
            "oauth_flow_context_changed",
            "invalid_cursor",
            "external_connection_required",
            "external_connection_not_found",
            "account_session_not_found",
            "current_session_cannot_be_revoked",
            "concurrency_conflict",
            "organization_ownership_transfer_required",
            "organization_not_found",
            "organization_permission_denied",
            "organization_name_conflict",
            "organization_slug_conflict",
            "last_organization_required",
            "organization_confirmation_mismatch",
            "member_not_found",
            "target_user_not_found",
            "member_already_exists",
            "member_role_unchanged",
            "role_assignment_forbidden",
            "member_domain_acknowledgement_required",
            "team_not_found",
            "team_permission_denied",
            "team_name_conflict",
            "team_name_unchanged",
            "team_member_not_found",
            "team_member_already_exists",
            "invitation_not_found",
            "invitation_permission_denied",
            "invitation_already_exists",
            "invitation_recipient_already_member",
            "invitation_team_invalid",
            "invitation_domain_restricted",
            "invitation_recipient_mismatch",
            "invitation_email_verification_required",
            "invitation_expired",
            "invitation_not_pending",
            "invitation_membership_conflict",
            "invitation_limit_reached");
    }

    [Fact]
    public async Task PublishedProductContractContainsNoProviderSecretsOrProtocolCallbacks()
    {
        using var client = factory.CreateApiClient();
        var json = await client.GetStringAsync(
            "/api/openapi/v1.json",
            TestContext.Current.CancellationToken);

        Assert.DoesNotContain(
            "/api/auth/callback/",
            json,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "/api/auth/oauth2/callback/",
            json,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "clientSecret",
            json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "clientId",
            json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "accessToken",
            json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "refreshToken",
            json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "providerSubject",
            json,
            StringComparison.OrdinalIgnoreCase);
        foreach (var providerEndpoint in new[]
                 {
                     "accounts.google.com",
                     "github.com/login/oauth",
                     "gitlab.com/oauth",
                     "id.vk.com",
                     "oauth.yandex"
                 })
        {
            Assert.DoesNotContain(
                providerEndpoint,
                json,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task SuccessEnvelopeSchemasRequireNonNullData()
    {
        using var client = factory.CreateApiClient();
        var document = JsonNode.Parse(await client.GetStringAsync(
            "/api/openapi/v1.json",
            TestContext.Current.CancellationToken))!;

        var schemas = document["components"]!["schemas"]!.AsObject();
        var envelopes = schemas
            .Where(pair => pair.Key.StartsWith("ApiResponseOf", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(envelopes);
        foreach (var (name, schema) in envelopes)
        {
            Assert.Contains(
                "data",
                schema!["required"]!.AsArray().Select(item => item!.GetValue<string>()));
            Assert.DoesNotContain(
                "null",
                EnumerateSchemaTypes(schema["properties"]!["data"]!));
        }
    }

    [Fact]
    public async Task StandardProblemSchemaRequiresWireInvariantFields()
    {
        using var client = factory.CreateApiClient();
        var document = JsonNode.Parse(await client.GetStringAsync(
            "/api/openapi/v1.json",
            TestContext.Current.CancellationToken))!;

        AssertRequiredNonNullProperties(
            document["components"]!["schemas"]!["ProblemDetails"]!,
            "type",
            "title",
            "status",
            "detail",
            "instance",
            "code",
            "traceId");
    }

    [Fact]
    public async Task ValidationProblemSchemaRequiresWireInvariantFieldsAndErrors()
    {
        using var client = factory.CreateApiClient();
        var document = JsonNode.Parse(await client.GetStringAsync(
            "/api/openapi/v1.json",
            TestContext.Current.CancellationToken))!;

        AssertRequiredNonNullProperties(
            document["components"]!["schemas"]!["HttpValidationProblemDetails"]!,
            "type",
            "title",
            "status",
            "detail",
            "instance",
            "code",
            "traceId",
            "errors");
    }

    [Fact]
    public async Task LocalSignInCredentialsAreRequiredAndNonNull()
    {
        using var client = factory.CreateApiClient();
        var document = JsonNode.Parse(await client.GetStringAsync(
            "/api/openapi/v1.json",
            TestContext.Current.CancellationToken))!;

        AssertRequiredNonNullProperties(
            document["components"]!["schemas"]!["LocalAutomationSignInRequest"]!,
            "email",
            "password");
    }

    [Fact]
    public async Task ScenarioSchemaPublishesNormalizedInputConstraints()
    {
        using var client = factory.CreateApiClient();
        var document = JsonNode.Parse(await client.GetStringAsync(
            "/api/openapi/v1.json",
            TestContext.Current.CancellationToken))!;
        var properties = document["components"]!["schemas"]!
            ["CreateLocalAutomationScenarioRequest"]!["properties"]!;
        var name = properties["name"]!;
        var email = properties["email"]!;

        Assert.Null(name["minLength"]);
        Assert.Null(name["maxLength"]);
        Assert.Equal(2, name["x-trimmed-min-length"]!.GetValue<int>());
        Assert.Equal(50, name["x-trimmed-max-length"]!.GetValue<int>());
        Assert.Contains(
            "trim",
            name["description"]!.GetValue<string>(),
            StringComparison.OrdinalIgnoreCase);

        Assert.Null(email["maxLength"]);
        Assert.Null(email["format"]);
        Assert.Equal(254, email["x-trimmed-max-length"]!.GetValue<int>());
        Assert.Equal("email", email["x-trimmed-format"]!.GetValue<string>());
        Assert.Contains(
            "trim",
            email["description"]!.GetValue<string>(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "lower",
            email["description"]!.GetValue<string>(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Null(email["pattern"]);
        var localEmailPattern = email["x-trimmed-pattern"]!.GetValue<string>();
        Assert.Matches(
            localEmailPattern,
            "LOCAL-AGENT+SCENARIO@LOCAL-AGENT.TEST");
        Assert.DoesNotMatch(
            localEmailPattern,
            "evil-local-agent+scenario@local-agent.test");
        Assert.DoesNotMatch(
            localEmailPattern,
            "local-agent+scenario@local-agent.test.example");
        Assert.DoesNotMatch(
            localEmailPattern,
            "local-agent+not valid@local-agent.test");
    }

    [Fact]
    public async Task ScenarioRequestBodyIsOptionalButSignInRequestBodyIsRequired()
    {
        using var client = factory.CreateApiClient();
        var document = JsonNode.Parse(await client.GetStringAsync(
            "/api/openapi/v1.json",
            TestContext.Current.CancellationToken))!;
        var paths = document["paths"]!;

        Assert.NotEqual(
            true,
            paths["/api/local-auth/scenario"]!["post"]!["requestBody"]!["required"]
                ?.GetValue<bool>());
        Assert.True(
            paths["/api/local-auth/sign-in"]!["post"]!["requestBody"]!["required"]!
                .GetValue<bool>());
    }

    [Fact]
    public async Task UnsafeAuthOperationsPublishTheirActualBadRequestShapes()
    {
        using var client = factory.CreateApiClient();
        var document = JsonNode.Parse(await client.GetStringAsync(
            "/api/openapi/v1.json",
            TestContext.Current.CancellationToken))!;
        var paths = document["paths"]!;

        AssertSchemaReference(
            paths["/api/v1/auth/logout"]!["post"]!["responses"]!["400"]!,
            "ProblemDetails");
        AssertSchemaReference(
            paths["/api/local-auth/scenario"]!["delete"]!["responses"]!["400"]!,
            "ProblemDetails");
        AssertSchemaUnion(
            paths["/api/local-auth/scenario"]!["post"]!["responses"]!["400"]!,
            "ProblemDetails",
            "HttpValidationProblemDetails");
        AssertSchemaUnion(
            paths["/api/local-auth/sign-in"]!["post"]!["responses"]!["400"]!,
            "ProblemDetails",
            "HttpValidationProblemDetails");
    }

    [Fact]
    public async Task ProductionHostDoesNotPublishRuntimeOpenApi()
    {
        using var certificate = TestDataProtectionCertificate.CreateRsa();
        await using var productionFactory = factory.WithWebHostBuilder(
            certificate.ConfigureProductionHost);
        using var client = productionFactory.CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync(
            "/api/openapi/v1.json",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RuntimeDocumentSemanticallyMatchesCommittedContract()
    {
        using var client = factory.CreateApiClient();
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

    private static void AssertRequiredNonNullProperties(
        JsonNode schema,
        params string[] propertyNames)
    {
        AssertRequiredProperties(schema, propertyNames);

        foreach (var propertyName in propertyNames)
        {
            var property = schema["properties"]![propertyName];
            Assert.NotNull(property);
            Assert.DoesNotContain("null", EnumerateSchemaTypes(property));
        }
    }

    private static void AssertRequiredProperties(
        JsonNode schema,
        params string[] propertyNames)
    {
        var requiredNode = schema["required"];
        Assert.NotNull(requiredNode);
        var required = requiredNode.AsArray()
            .Select(item => item!.GetValue<string>())
            .ToArray();

        foreach (var propertyName in propertyNames)
        {
            Assert.Contains(propertyName, required);
        }
    }

    private static void AssertSchemaReference(
        JsonNode response,
        string schemaName)
    {
        var schema = response["content"]!["application/problem+json"]!["schema"]!;
        Assert.Equal(
            $"#/components/schemas/{schemaName}",
            schema["$ref"]!.GetValue<string>());
    }

    private static void AssertSchemaUnion(
        JsonNode response,
        params string[] schemaNames)
    {
        var schema = response["content"]!["application/problem+json"]!["schema"]!;
        var references = schema["oneOf"]!
            .AsArray()
            .Select(value => value!["$ref"]!.GetValue<string>())
            .ToArray();

        Assert.Equal(
            schemaNames.Select(name => $"#/components/schemas/{name}"),
            references);
    }

    private static void AssertCookieSecurity(JsonNode operation)
    {
        var security = Assert.Single(operation["security"]!.AsArray());
        var requirement = security!.AsObject();
        var scheme = Assert.Single(requirement);
        Assert.Equal("cookieAuth", scheme.Key);
        Assert.Empty(scheme.Value!.AsArray());
    }

    private static void AssertConditionalCookieSecurity(JsonNode operation)
    {
        var requirements = operation["security"]!.AsArray();
        Assert.Equal(2, requirements.Count);
        Assert.Empty(requirements[0]!.AsObject());
        var cookie = Assert.Single(requirements[1]!.AsObject());
        Assert.Equal("cookieAuth", cookie.Key);
        Assert.Empty(cookie.Value!.AsArray());
    }

    private static JsonNode AssertOperation(
        JsonNode document,
        string path,
        string method,
        string operationId)
    {
        var operation = document["paths"]?[path]?[method];
        Assert.True(operation is not null, $"Missing {method.ToUpperInvariant()} {path}.");
        Assert.Equal(
            operationId,
            operation!["operationId"]!.GetValue<string>());
        return operation!;
    }

    private static void AssertRequiredHeader(
        JsonNode operation,
        string headerName)
    {
        var header = Assert.Single(
            operation["parameters"]!.AsArray(),
            parameter => parameter!["name"]!.GetValue<string>() == headerName);
        Assert.Equal("header", header!["in"]!.GetValue<string>());
        Assert.True(header["required"]!.GetValue<bool>());
        Assert.Equal("string", header["schema"]!["type"]!.GetValue<string>());
    }

    private static void AssertNoStoreResponseHeader(JsonNode response)
    {
        var header = response["headers"]!["Cache-Control"]!;
        Assert.True(header["required"]!.GetValue<bool>());
        Assert.Equal("string", header["schema"]!["type"]!.GetValue<string>());
        Assert.Equal("no-store", header["schema"]!["enum"]![0]!.GetValue<string>());
    }

    private static void AssertPagination(
        JsonNode operation,
        int minimum,
        int maximum,
        int defaultValue,
        string cursorDescription)
    {
        var parameters = operation["parameters"]!.AsArray();
        var limit = Assert.Single(
            parameters,
            parameter => parameter!["name"]!.GetValue<string>() == "limit");
        Assert.Equal("integer", limit!["schema"]!["type"]!.GetValue<string>());
        Assert.Equal("int32", limit["schema"]!["format"]!.GetValue<string>());
        Assert.Equal(minimum, limit["schema"]!["minimum"]!.GetValue<int>());
        Assert.Equal(maximum, limit["schema"]!["maximum"]!.GetValue<int>());
        Assert.Equal(defaultValue, limit["schema"]!["default"]!.GetValue<int>());

        var cursor = Assert.Single(
            parameters,
            parameter => parameter!["name"]!.GetValue<string>() == "cursor");
        Assert.NotEqual(true, cursor!["required"]?.GetValue<bool>());
        Assert.Equal("string", cursor["schema"]!["type"]!.GetValue<string>());
        Assert.Equal(
            cursorDescription,
            cursor["schema"]!["description"]!.GetValue<string>());
    }

    private static void AssertTrimmedString(
        JsonNode schema,
        int minimum,
        int maximum)
    {
        Assert.Null(schema["minLength"]);
        Assert.Null(schema["maxLength"]);
        Assert.Equal(
            minimum,
            schema["x-trimmed-min-length"]!.GetValue<int>());
        Assert.Equal(
            maximum,
            schema["x-trimmed-max-length"]!.GetValue<int>());
    }

    private static void AssertStringEnum(
        JsonNode schema,
        params string[] expected)
    {
        Assert.Equal("string", schema["type"]!.GetValue<string>());
        Assert.Equal(
            expected,
            schema["enum"]!.AsArray()
                .Select(value => value!.GetValue<string>()));
    }

    private static int[] ProblemStatuses(JsonNode operation) =>
        operation["responses"]!.AsObject()
            .Select(response => response.Key)
            .Where(status => int.TryParse(status, out var value) && value >= 400)
            .Select(int.Parse)
            .Order()
            .ToArray();

    private static IEnumerable<string> EnumerateSchemaTypes(JsonNode schema)
    {
        if (schema["type"] is JsonValue singleType)
        {
            yield return singleType.GetValue<string>();
        }
        else if (schema["type"] is JsonArray types)
        {
            foreach (var type in types)
            {
                yield return type!.GetValue<string>();
            }
        }

        if (schema["oneOf"] is JsonArray alternatives)
        {
            foreach (var alternative in alternatives)
            {
                foreach (var type in EnumerateSchemaTypes(alternative!))
                {
                    yield return type;
                }
            }
        }
    }
}
