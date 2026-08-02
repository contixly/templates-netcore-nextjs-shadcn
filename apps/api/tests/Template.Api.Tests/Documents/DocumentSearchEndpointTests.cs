using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Template.Api.Contracts;
using Template.Api.Tests.ApiKeys;
using Template.Api.Tests.Infrastructure;

namespace Template.Api.Tests.Documents;

public sealed class DocumentSearchEndpointTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task AnonymousEmptySearchReturnsEnglishPagesWithoutHeadings()
    {
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(
            "/api/v1/documents-system/search",
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<
            ApiResponse<TestDocumentSearchResponse>>(
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.NotNull(body);
        Assert.Equal(32, body.Data.Pages.Count);
        Assert.Empty(body.Data.Headings);
        Assert.All(body.Data.Pages, page => Assert.StartsWith("/docs", page.Href));
    }

    [Theory]
    [InlineData("authentication", "en")]
    [InlineData("аутентификация", "ru")]
    [InlineData("фзш м1", "ru")]
    public async Task TypedSearchSupportsEnglishRussianAndKeyboardLayout(
        string query,
        string locale)
    {
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(
            $"/api/v1/documents-system/search?q={Uri.EscapeDataString(query)}&locale={locale}",
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<
            ApiResponse<TestDocumentSearchResponse>>(
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.NotNull(body);
        Assert.True(body.Data.Pages.Count <= 8);
        Assert.True(body.Data.Headings.Count <= 8);
        Assert.NotEmpty(body.Data.Pages.Concat<object>(body.Data.Headings));
    }

    [Fact]
    public async Task SearchTrimsQueryBeforeApplyingUtf16LengthLimit()
    {
        using var client = factory.CreateApiClient();
        var allowed = new string('a', 120);

        using var allowedResponse = await client.GetAsync(
            $"/api/v1/documents-system/search?q=%20%20{allowed}%20%20&locale=en",
            TestContext.Current.CancellationToken);
        using var overlongResponse = await client.GetAsync(
            $"/api/v1/documents-system/search?q={allowed}a&locale=en",
            TestContext.Current.CancellationToken);
        var problem = await overlongResponse.Content.ReadFromJsonAsync<
            HttpValidationProblemDetails>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, allowedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, overlongResponse.StatusCode);
        Assert.Equal("no-store", overlongResponse.Headers.CacheControl?.ToString());
        Assert.Equal("validation_failed", problem!.Extensions["code"]!.ToString());
        Assert.Contains("q", problem.Errors.Keys);
    }

    [Fact]
    public async Task QueryLimitCountsUtf16CodeUnits()
    {
        using var client = factory.CreateApiClient();
        var sixtyEmoji = string.Concat(Enumerable.Repeat("😀", 60));
        var sixtyOneEmoji = sixtyEmoji + "😀";

        using var allowed = await client.GetAsync(
            $"/api/v1/documents-system/search?q={Uri.EscapeDataString(sixtyEmoji)}&locale=en",
            TestContext.Current.CancellationToken);
        using var rejected = await client.GetAsync(
            $"/api/v1/documents-system/search?q={Uri.EscapeDataString(sixtyOneEmoji)}&locale=en",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("de")]
    [InlineData("EN")]
    public async Task ExplicitBlankOrUnknownLocaleIsRejected(string locale)
    {
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(
            $"/api/v1/documents-system/search?q=test&locale={Uri.EscapeDataString(locale)}",
            TestContext.Current.CancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<
            HttpValidationProblemDetails>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("validation_failed", problem!.Extensions["code"]!.ToString());
        Assert.Contains("locale", problem.Errors.Keys);
    }

    [Fact]
    public async Task CookieAndApiKeyHeadersDoNotChangePublicSearchResults()
    {
        using var anonymousClient = CreateStatelessClient(factory);
        using var anonymous = await anonymousClient.GetAsync(
            "/api/v1/documents-system/search?q=authentication&locale=en",
            TestContext.Current.CancellationToken);
        var expected = await anonymous.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        foreach (var headers in new[]
                 {
                     new Dictionary<string, string>
                     {
                         ["Cookie"] = "__Host-template.session=invalid"
                     },
                     new Dictionary<string, string>
                     {
                         ["x-api-key"] = "tpl_invalid"
                     },
                     new Dictionary<string, string>
                     {
                         ["Cookie"] = "unrelated=value",
                         ["x-api-key"] = "also-invalid"
                     }
                 })
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                "/api/v1/documents-system/search?q=authentication&locale=en");
            foreach (var (name, value) in headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }

            using var actual = await anonymousClient.SendAsync(
                request,
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, actual.StatusCode);
            Assert.Equal("no-store", actual.Headers.CacheControl?.ToString());
            Assert.Equal(
                expected,
                await actual.Content.ReadAsStringAsync(
                    TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task ValidSessionAndValidApiKeyDoNotChangePublicSearchResults()
    {
        await factory.ResetAuthDataAsync(TestContext.Current.CancellationToken);
        using var anonymousClient = CreateStatelessClient(factory);
        var path = "/api/v1/documents-system/search?q=authentication&locale=en";
        var expected = await anonymousClient.GetStringAsync(
            path,
            TestContext.Current.CancellationToken);

        using var sessionClient = factory.CreateApiClient();
        using var scenario = await LocalAuthTestClient.CreateScenarioAsync(sessionClient);
        Assert.Equal(HttpStatusCode.Created, scenario.StatusCode);
        using var cookieResponse = await sessionClient.GetAsync(
            path,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            expected,
            await cookieResponse.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken));

        using var createKey = await ApiKeyEndpointTests.SendJsonWithCsrfAsync(
            sessionClient,
            HttpMethod.Post,
            "/api/v1/account/api-keys",
            ApiKeyEndpointTests.ValidCreate("Document search proof", "basic-read"));
        Assert.Equal(HttpStatusCode.Created, createKey.StatusCode);
        var credential = (await ApiKeyEndpointTests.ReadDataAsync(createKey))
            .GetProperty("key")
            .GetString();
        using var keyRequest = new HttpRequestMessage(HttpMethod.Get, path);
        keyRequest.Headers.Add("x-api-key", credential);
        using var keyResponse = await anonymousClient.SendAsync(
            keyRequest,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, keyResponse.StatusCode);
        Assert.Equal(
            expected,
            await keyResponse.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("ru", "Документация шаблона")]
    [InlineData("invalid", "Template documentation")]
    [InlineData("", "Template documentation")]
    public async Task MissingLocaleUsesConfiguredDefaultOrFallsBackToEnglish(
        string configuredLocale,
        string expectedFirstTitle)
    {
        await using var configuredFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Documents:DefaultLocale"] = configuredLocale
                    })));
        using var client = CreateStatelessClient(configuredFactory);

        var body = await client.GetFromJsonAsync<ApiResponse<TestDocumentSearchResponse>>(
            "/api/v1/documents-system/search",
            TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        Assert.Equal(32, body.Data.Pages.Count);
        Assert.Equal(expectedFirstTitle, body.Data.Pages[0].Title);
    }

    private static HttpClient CreateStatelessClient(
        WebApplicationFactory<Program> application) =>
        application.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false
        });

    private sealed record TestDocumentSearchResponse(
        IReadOnlyList<TestDocumentSearchPageResponse> Pages,
        IReadOnlyList<TestDocumentSearchHeadingResponse> Headings);

    private sealed record TestDocumentSearchPageResponse(
        string Type,
        string Title,
        string Description,
        string Href,
        string Group,
        string ParentItem);

    private sealed record TestDocumentSearchHeadingResponse(
        string Type,
        string Title,
        string Href,
        string PageTitle,
        string Group,
        string ParentItem);
}
