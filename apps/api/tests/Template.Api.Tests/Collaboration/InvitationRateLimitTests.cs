using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Template.Api.Tests.Infrastructure;
using Template.Api.Tests.Organizations;

namespace Template.Api.Tests.Collaboration;

public sealed class InvitationRateLimitTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>, IAsyncLifetime
{
    private const string AuditCategory =
        "Template.Api.Features.Collaboration.InvitationEndpointModule";

    public async ValueTask InitializeAsync()
    {
        await factory.ResetAuthDataAsync(TestContext.Current.CancellationToken);
        factory.Services.GetRequiredService<CapturedLogProvider>().Clear();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task InvitationCreateAllowsTwentyPerUserWithNoQueueAndIsolatesUsers()
    {
        using var first = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            first,
            "Create Limited First",
            "local-agent+create-limited-first@local-agent.test");
        using var second = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            second,
            "Create Limited Second",
            "local-agent+create-limited-second@local-agent.test");
        var missingOrganizationId = Guid.NewGuid();
        var firstToken = await LocalAuthTestClient.GetCsrfAsync(first);
        var secondToken = await LocalAuthTestClient.GetCsrfAsync(second);
        var logs = factory.Services.GetRequiredService<CapturedLogProvider>();
        logs.Clear();

        for (var attempt = 0; attempt < 20; attempt++)
        {
            using var response = await SendCreateAsync(
                first,
                firstToken,
                missingOrganizationId,
                attempt);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        using var rejected = await SendCreateAsync(
            first,
            firstToken,
            missingOrganizationId,
            20);
        using var isolated = await SendCreateAsync(
            second,
            secondToken,
            missingOrganizationId,
            0);

        await AssertRateLimitedAsync(rejected);
        Assert.Equal(HttpStatusCode.NotFound, isolated.StatusCode);
        var rateAudit = Assert.Single(
            logs.Logs,
            log => log.Category == AuditCategory &&
                Equals(log.State["CollaborationOutcome"], "rate_limited"));
        Assert.Equal(
            "invitation_create",
            rateAudit.State["CollaborationOperation"]);
        Assert.IsType<Guid>(rateAudit.State["UserId"]);
        Assert.Equal(missingOrganizationId, rateAudit.State["OrganizationId"]);
    }

    [Fact]
    public async Task InvitationAcceptAndRejectShareThirtyPerUserAndDoNotLimitReads()
    {
        using var client = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            client,
            "Decision Limited",
            "local-agent+decision-limited@local-agent.test");
        var invitationId = Guid.NewGuid();
        var token = await LocalAuthTestClient.GetCsrfAsync(client);

        for (var attempt = 0; attempt < 15; attempt++)
        {
            using var read = await client.GetAsync(
                $"/api/v1/invitations/{invitationId:D}",
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
            using var accept = await SendDecisionAsync(
                client,
                token,
                invitationId,
                "accept");
            Assert.Equal(HttpStatusCode.NotFound, accept.StatusCode);
            using var reject = await SendDecisionAsync(
                client,
                token,
                invitationId,
                "reject");
            Assert.Equal(HttpStatusCode.NotFound, reject.StatusCode);
        }

        using var rejected = await SendDecisionAsync(
            client,
            token,
            invitationId,
            "accept");

        await AssertRateLimitedAsync(rejected);
        var rateAudit = Assert.Single(
            factory.Services.GetRequiredService<CapturedLogProvider>().Logs,
            log => log.Category == AuditCategory &&
                Equals(log.State["CollaborationOutcome"], "rate_limited"));
        Assert.Equal(
            "invitation_accept",
            rateAudit.State["CollaborationOperation"]);
        Assert.Equal(invitationId, rateAudit.State["InvitationId"]);
    }

    private static Task<HttpResponseMessage> SendCreateAsync(
        HttpClient client,
        string token,
        Guid organizationId,
        int attempt)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/organizations/{organizationId:D}/invitations");
        request.Headers.Add("X-CSRF-TOKEN", token);
        request.Content = JsonContent.Create(new
        {
            email = $"local-agent+limited-{attempt}@local-agent.test",
            role = "member"
        });
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static Task<HttpResponseMessage> SendDecisionAsync(
        HttpClient client,
        string token,
        Guid invitationId,
        string decision)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/invitations/{invitationId:D}/{decision}");
        request.Headers.Add("X-CSRF-TOKEN", token);
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task AssertRateLimitedAsync(
        HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.NotNull(response.Headers.RetryAfter);
        var problem = await response.Content.ReadFromJsonAsync<ApiProblem>(
            TestContext.Current.CancellationToken);
        Assert.Equal("rate_limited", problem!.Code);
    }

    private sealed record ApiProblem(string Code);
}
