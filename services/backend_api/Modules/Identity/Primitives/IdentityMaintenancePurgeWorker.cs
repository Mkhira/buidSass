using BackendApi.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Identity.Primitives;

public sealed class IdentityMaintenancePurgeWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<IdentityMaintenancePurgeWorker> logger) : BackgroundService
{
    private static readonly TimeSpan RunInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ReplayGuardRetention = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RevokedTokenRetention = TimeSpan.FromDays(90);

    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ILogger<IdentityMaintenancePurgeWorker> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Identity maintenance purge pass failed.");
            }

            try
            {
                await Task.Delay(RunInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task PurgeAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var replayGuardThreshold = now.Subtract(ReplayGuardRetention);
        var revokedTokenThreshold = now.Subtract(RevokedTokenRetention);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var removedReplayGuards = await dbContext.AdminMfaReplayGuards
            .Where(x => x.ObservedAt < replayGuardThreshold)
            .ExecuteDeleteAsync(cancellationToken);

        var removedRevokedTokens = await dbContext.RevokedRefreshTokens
            .Where(x => x.RevokedAt < revokedTokenThreshold)
            .ExecuteDeleteAsync(cancellationToken);

        if (removedReplayGuards > 0 || removedRevokedTokens > 0)
        {
            _logger.LogInformation(
                "Identity maintenance purge removed {ReplayGuardCount} replay-guard rows and {RevokedTokenCount} revoked-token rows.",
                removedReplayGuards,
                removedRevokedTokens);
        }
    }
}
