using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Template.Infrastructure.Persistence;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Template.Infrastructure.Authentication;

public sealed class OpenIddictStateCleanupService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider)
    : BackgroundService
{
    public const int MaximumBatchSize = 500;

    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan RedeemedRetention = TimeSpan.FromHours(24);

    public async Task<int> CleanupOnceAsync(
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var redeemedBefore = now - RedeemedRetention;

        return await db.OpenIddictTokens
            .Where(token =>
                token.Type == TokenTypeIdentifiers.Private.StateToken
                && (token.ExpirationDate < now
                    || token.Status == Statuses.Redeemed
                    && token.RedemptionDate < redeemedBefore))
            .OrderBy(token => token.ExpirationDate)
            .ThenBy(token => token.RedemptionDate)
            .ThenBy(token => token.Id)
            .Take(MaximumBatchSize)
            .ExecuteDeleteAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CleanupInterval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await CleanupOnceAsync(stoppingToken);
        }
    }
}
