using BackendApi.Modules.Notifications.Domain.StateMachines;
using BackendApi.Modules.Notifications.Persistence;
using BackendApi.Modules.Notifications.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Notifications.Workers;

/// <summary>
/// T041 — ages out notifications stuck in <c>sending</c> for over 1 hour
/// (worker crash mid-dispatch, provider hang, etc.). Such rows are
/// transitioned to <c>retrying</c> so the dispatch worker re-picks them on
/// its next sweep. Runs every 30 minutes.
/// </summary>
public sealed class SendingStuckReconciler : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<SendingStuckReconciler> _logger;
    private readonly TimeProvider _clock;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan StuckThreshold = TimeSpan.FromHours(1);

    public SendingStuckReconciler(
        IServiceScopeFactory scopes,
        ILogger<SendingStuckReconciler> logger,
        TimeProvider clock)
    {
        _scopes = scopes;
        _logger = logger;
        _clock = clock;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "SendingStuckReconciler iteration failed");
            }
            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { }
        }
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var cutoff = _clock.GetUtcNow() - StuckThreshold;

        var stuck = await db.Notifications
            .Where(n => n.State == NotificationsConstants.NotificationStates.Sending
                && n.DeletedAt == null
                && n.UpdatedAt < cutoff)
            .Take(100)
            .ToListAsync(ct);

        foreach (var n in stuck)
        {
            if (!NotificationStateMachine.CanTransition(n.State, NotificationsConstants.NotificationStates.Retrying))
                continue;
            NotificationStateMachine.EnsureTransition(n.State, NotificationsConstants.NotificationStates.Retrying);
            n.State = NotificationsConstants.NotificationStates.Retrying;
            n.UpdatedAt = _clock.GetUtcNow();
        }
        if (stuck.Count > 0) await db.SaveChangesAsync(ct);
    }
}
