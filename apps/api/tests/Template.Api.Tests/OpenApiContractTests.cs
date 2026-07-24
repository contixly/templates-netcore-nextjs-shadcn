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
        Assert.NotNull(document["paths"]!["/api/local-auth/scenario"]);
        Assert.NotNull(document["paths"]!["/api/local-auth/sign-in"]);
        Assert.NotNull(
            document["paths"]!["/api/local-auth/scenario"]!["delete"]);
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
        await using var productionFactory = factory.WithWebHostBuilder(
            builder => builder.UseEnvironment("Production"));
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
        var requiredNode = schema["required"];
        Assert.NotNull(requiredNode);
        var required = requiredNode.AsArray()
            .Select(item => item!.GetValue<string>())
            .ToArray();

        foreach (var propertyName in propertyNames)
        {
            Assert.Contains(propertyName, required);
            var property = schema["properties"]![propertyName];
            Assert.NotNull(property);
            Assert.DoesNotContain("null", EnumerateSchemaTypes(property));
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
