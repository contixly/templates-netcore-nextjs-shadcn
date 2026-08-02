using Microsoft.AspNetCore.Http;
using Template.Api.Errors;
using Template.Api.Features.ApiKeys;
using Template.Api.Tests.Infrastructure;
using Template.Application.ApiKeys;
using Template.Domain.ApiKeys;
using Template.Domain.Authentication;

namespace Template.Api.Tests.ApiKeys;

public sealed class MachineApiEndpointExecutionTests
{
    [Fact]
    public async Task SuccessReturnsTheResultAndWritesOneSafeSucceededAudit()
    {
        var logs = new CapturedLogProvider();
        var logger = logs.CreateLogger("machine-execution-test");
        var principal = Principal();
        var expected = Results.Ok();

        var actual = await MachineApiEndpointExecution.ExecuteAsync(
            () => Task.FromResult<IResult>(expected),
            "team_list",
            principal,
            logger,
            TestContext.Current.CancellationToken);

        Assert.Same(expected, actual);
        var audit = Assert.Single(logs.Logs);
        Assert.Equal("team_list", audit.State["MachineApiOperation"]);
        Assert.Equal("succeeded", audit.State["MachineApiOutcome"]);
        Assert.Equal(principal.Id.Value, audit.State["ApiKeyId"]);
        Assert.Equal(principal.Owner.UserId!.Value.Value, audit.State["OwnerId"]);
        Assert.False(audit.State.ContainsKey("SessionId"));
    }

    [Fact]
    public async Task KnownProblemIsRethrownAndWritesItsStableCode()
    {
        var logs = new CapturedLogProvider();
        var logger = logs.CreateLogger("machine-execution-test");
        var expected = new ApiProblemException(
            StatusCodes.Status403Forbidden,
            "organization_access_denied");

        var actual = await Assert.ThrowsAsync<ApiProblemException>(() =>
            MachineApiEndpointExecution.ExecuteAsync(
                () => Task.FromException<IResult>(expected),
                "team_members_list",
                Principal(),
                logger,
                TestContext.Current.CancellationToken));

        Assert.Same(expected, actual);
        var audit = Assert.Single(logs.Logs);
        Assert.Equal("team_members_list", audit.State["MachineApiOperation"]);
        Assert.Equal(
            "organization_access_denied",
            audit.State["MachineApiOutcome"]);
    }

    [Fact]
    public async Task RequestCancellationPassesThroughWithoutMachineFailureAudit()
    {
        var logs = new CapturedLogProvider();
        var logger = logs.CreateLogger("machine-execution-test");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var expected = new OperationCanceledException(cancellation.Token);

        var actual = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            MachineApiEndpointExecution.ExecuteAsync(
                () => Task.FromException<IResult>(expected),
                "organization_list",
                Principal(),
                logger,
                cancellation.Token));

        Assert.Same(expected, actual);
        Assert.Empty(logs.Logs);
    }

    [Fact]
    public async Task UnexpectedFailureWritesInternalErrorWithoutExceptionDisclosure()
    {
        const string sensitiveMessage =
            "sensitive database exception with user@example.test";
        var logs = new CapturedLogProvider();
        var logger = logs.CreateLogger("machine-execution-test");
        var expected = new InvalidOperationException(sensitiveMessage);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            MachineApiEndpointExecution.ExecuteAsync(
                () => Task.FromException<IResult>(expected),
                "organization_get",
                Principal(),
                logger,
                TestContext.Current.CancellationToken));

        Assert.Same(expected, actual);
        var audit = Assert.Single(logs.Logs);
        Assert.Equal("internal_error", audit.State["MachineApiOutcome"]);
        Assert.Null(audit.Exception);
        Assert.DoesNotContain(
            sensitiveMessage,
            string.Join(
                ' ',
                new[] { audit.Message }.Concat(audit.State.Values.Select(value =>
                    value?.ToString() ?? string.Empty))),
            StringComparison.Ordinal);
    }

    private static ApiKeyPrincipal Principal()
    {
        var userId = new UserId(
            Guid.Parse("0198a7ac-d0f8-7832-b711-211f56c57701"));
        return new(
            new ApiKeyId(
                Guid.Parse("0198a7ac-d0f8-7832-b711-211f56c57702")),
            "user_abcdefghijk",
            new(ApiKeyOwnerKind.User, userId, null),
            [ApiKeyScopes.OrganizationRead, ApiKeyScopes.TeamRead]);
    }
}
