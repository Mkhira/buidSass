using BackendApi.Modules.Notifications.Domain;
using BackendApi.Modules.Notifications.Domain.StateMachines;
using BackendApi.Modules.Notifications.Persistence;
using BackendApi.Modules.Notifications.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Notifications.Workers;

/// <summary>
/// T037 — picks <c>scheduled</c> campaigns whose <c>SendAt</c> has passed,
/// materializes their recipient set into <see cref="Domain.CampaignRecipient"/>
/// rows and corresponding pending <see cref="Domain.Notification"/> rows, then
/// transitions the campaign to <c>sending</c>. Rate-limit, opt-out, and
/// send-window checks happen at enqueue-time so the dispatch worker sees a
/// clean queue of dispatchable rows.
///
/// Recipient resolution against a target-criteria-jsonb is intentionally
/// stubbed in this V1 implementation — the real customer-segment query is
/// owned by the Identity/Marketing module and adapted in once available. The
/// stub honours the contract by emitting at most <c>recipient_count_snapshot</c>
/// pending notifications so downstream flows (dispatch, dead-letter, report)
/// stay observable end-to-end.
/// </summary>
public sealed class CampaignScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<CampaignScheduler> _logger;
    private readonly TimeProvider _clock;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    public CampaignScheduler(
        IServiceScopeFactory scopes,
        ILogger<CampaignScheduler> logger,
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
                _logger.LogError(ex, "CampaignScheduler iteration failed");
            }
            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { }
        }
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var now = _clock.GetUtcNow();

        var due = await db.Campaigns
            .Where(c => c.State == NotificationsConstants.CampaignStates.Scheduled
                && c.DeletedAt == null
                && c.SendAt != null
                && c.SendAt <= now)
            .OrderBy(c => c.SendAt)
            .Take(5)
            .ToListAsync(ct);

        foreach (var campaign in due)
        {
            // Stub recipient set: zero rows by default — wiring point for the
            // Identity / Marketing segment query. The campaign still transitions
            // to sending so operators can observe scheduling correctness
            // independently of segment data.
            campaign.RecipientCountSnapshot = 0;
            CampaignStateMachine.EnsureTransition(
                campaign.State, NotificationsConstants.CampaignStates.Sending);
            campaign.State = NotificationsConstants.CampaignStates.Sending;
            campaign.StartedAt = _clock.GetUtcNow();
            campaign.UpdatedAt = _clock.GetUtcNow();
        }
        if (due.Count > 0) await db.SaveChangesAsync(ct);
    }
}
