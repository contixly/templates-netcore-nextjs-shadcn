using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Template.Api.Tests.Infrastructure;
using Template.Application.Collaboration;
using Template.Application.Collaboration.Ports;
using Template.Infrastructure.Collaboration;
using Template.Infrastructure.Persistence;

namespace Template.Api.Tests.Collaboration;

public sealed class InvitationNotifierTests
{
    private const string Recipient = "private-recipient@example.test";
    private const string InvitationPath =
        "/invite/aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee";

    [Fact]
    public async Task No_op_notifier_is_deterministic_and_performs_no_delivery()
    {
        var notifier = new NoOpInvitationNotifier();

        var outcome = await notifier.NotifyCreatedAsync(
            new InvitationNotification(Recipient, InvitationPath),
            TestContext.Current.CancellationToken);

        Assert.Equal(InvitationNotificationOutcome.Skipped, outcome);
    }

    [Fact]
    public async Task Safe_notifier_logs_only_completed_outcome()
    {
        var logs = new CapturedLogProvider();
        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddProvider(logs));
        var notifier = new SafeInvitationNotifier(
            new FixedInvitationNotifier(InvitationNotificationOutcome.Completed),
            loggerFactory.CreateLogger<SafeInvitationNotifier>());

        var outcome = await notifier.NotifyCreatedAsync(
            new InvitationNotification(Recipient, InvitationPath),
            TestContext.Current.CancellationToken);

        Assert.Equal(InvitationNotificationOutcome.Completed, outcome);
        var log = Assert.Single(logs.Logs);
        Assert.Equal(LogLevel.Information, log.Level);
        Assert.Equal("Completed", log.State["Outcome"]);
        Assert.DoesNotContain(Recipient, log.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(InvitationPath, log.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Safe_notifier_converts_adapter_exception_without_logging_secrets_or_exception()
    {
        var logs = new CapturedLogProvider();
        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddProvider(logs));
        var notifier = new SafeInvitationNotifier(
            new ThrowingInvitationNotifier(
                new InvalidOperationException($"{Recipient} {InvitationPath}")),
            loggerFactory.CreateLogger<SafeInvitationNotifier>());

        var outcome = await notifier.NotifyCreatedAsync(
            new InvitationNotification(Recipient, InvitationPath),
            TestContext.Current.CancellationToken);

        Assert.Equal(InvitationNotificationOutcome.Failed, outcome);
        var log = Assert.Single(logs.Logs);
        Assert.Equal(LogLevel.Warning, log.Level);
        Assert.Equal("Failed", log.State["Outcome"]);
        Assert.Null(log.Exception);
        Assert.DoesNotContain(Recipient, log.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(InvitationPath, log.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Safe_notifier_propagates_caller_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var notifier = new SafeInvitationNotifier(
            new ThrowingInvitationNotifier(
                new OperationCanceledException(cancellation.Token)),
            Microsoft.Extensions.Logging.Abstractions
                .NullLogger<SafeInvitationNotifier>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            notifier.NotifyCreatedAsync(
                new InvitationNotification(Recipient, InvitationPath),
                cancellation.Token));
    }

    [Fact]
    public async Task Infrastructure_registers_safe_no_network_notifier_by_default()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] =
                    "Host=localhost;Database=unused;Username=unused;Password=unused",
                ["DataProtection:ApplicationName"] = "Template"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(TimeProvider.System);
        services.AddAuthInfrastructure(configuration, new TestHostEnvironment());
        await using var provider = services.BuildServiceProvider();

        var notifier = provider.GetRequiredService<IInvitationNotifier>();
        var outcome = await notifier.NotifyCreatedAsync(
            new InvitationNotification(Recipient, InvitationPath),
            TestContext.Current.CancellationToken);

        Assert.IsType<SafeInvitationNotifier>(notifier);
        Assert.Equal(InvitationNotificationOutcome.Skipped, outcome);
    }

    private sealed class FixedInvitationNotifier(
        InvitationNotificationOutcome outcome)
        : IInvitationNotifier
    {
        public Task<InvitationNotificationOutcome> NotifyCreatedAsync(
            InvitationNotification notification,
            CancellationToken cancellationToken) => Task.FromResult(outcome);
    }

    private sealed class ThrowingInvitationNotifier(Exception exception)
        : IInvitationNotifier
    {
        public Task<InvitationNotificationOutcome> NotifyCreatedAsync(
            InvitationNotification notification,
            CancellationToken cancellationToken) =>
            Task.FromException<InvitationNotificationOutcome>(exception);
    }
}
