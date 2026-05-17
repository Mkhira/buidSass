using BackendApi.Modules.Notifications.Domain.StateMachines;
using BackendApi.Modules.Notifications.Persistence;
using BackendApi.Modules.Notifications.Primitives;
using BackendApi.Modules.Notifications.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Notifications.Workers;

/// <summary>
/// T030 — default-queue dispatch worker. Picks up <c>pending</c> notifications
/// (excluding OTP — that path runs through <see cref="OtpDispatchWorker"/> with
/// higher priority per BR-15), runs the BR-4 retry policy
/// (5xx/network transient → 1s/3s/9s backoff up to 3 attempts; 4xx terminal),
/// and advances each row through the canonical state machine to one of
/// <c>delivered | failed | dead_letter | skipped</c>.
/// </summary>
public class DispatchWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly TimeProvider _clock;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    protected virtual bool IncludesOtp => false;
    private static readonly TimeSpan[] RetryDelays =
        { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(9) };
    private const int MaxAttempts = 3;

    public DispatchWorker(
        IServiceScopeFactory scopes,
        ILoggerFactory loggerFactory,
        TimeProvider clock)
    {
        _scopes = scopes;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger(GetType());
        _clock = clock;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "{Worker} iteration failed", GetType().Name);
            }
            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { }
        }
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var router = scope.ServiceProvider.GetRequiredService<NotificationProviderRouter>();
        var addresses = scope.ServiceProvider.GetRequiredService<IRecipientAddressResolver>();

        var now = _clock.GetUtcNow();
        var query = db.Notifications
            .Where(n => n.State == NotificationsConstants.NotificationStates.Pending
                && n.DeletedAt == null
                && (n.NotBefore == null || n.NotBefore <= now));
        query = IncludesOtp
            ? query.Where(n => n.EventKind == NotificationsConstants.EventKinds.AuthOtpRequested)
            : query.Where(n => n.EventKind != NotificationsConstants.EventKinds.AuthOtpRequested);

        var batch = await query.OrderBy(n => n.CreatedAt).Take(25).ToListAsync(ct);

        foreach (var row in batch)
        {
            await DispatchOneAsync(row, db, router, addresses, ct);
        }
    }

    private async Task DispatchOneAsync(
        Domain.Notification row,
        NotificationsDbContext db,
        NotificationProviderRouter router,
        IRecipientAddressResolver addresses,
        CancellationToken ct)
    {
        var provider = router.Resolve(row.Channel, row.MarketCode);
        if (provider is null)
        {
            // No provider available — mark skipped (operator-actionable).
            NotificationStateMachine.EnsureTransition(row.State, NotificationsConstants.NotificationStates.Skipped);
            row.State = NotificationsConstants.NotificationStates.Skipped;
            row.SkippedReason = "no_provider_for_channel_market";
            row.UpdatedAt = _clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            return;
        }

        // pending → queued → sending (single commit; the retry loop owns sending→*)
        NotificationStateMachine.EnsureTransition(row.State, NotificationsConstants.NotificationStates.Queued);
        row.State = NotificationsConstants.NotificationStates.Queued;
        NotificationStateMachine.EnsureTransition(row.State, NotificationsConstants.NotificationStates.Sending);
        row.State = NotificationsConstants.NotificationStates.Sending;
        row.ProviderId = provider.ProviderId;
        row.UpdatedAt = _clock.GetUtcNow();
        await db.SaveChangesAsync(ct);

        var recipientAddr = row.RecipientId.HasValue
            ? await addresses.ResolveAsync(row.RecipientId.Value, row.Channel, ct)
            : null;
        if (recipientAddr is null)
        {
            NotificationStateMachine.EnsureTransition(row.State, NotificationsConstants.NotificationStates.Skipped);
            row.State = NotificationsConstants.NotificationStates.Skipped;
            row.SkippedReason = "no_deliverable_address";
            row.UpdatedAt = _clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            return;
        }

        SendResult? result = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            row.Attempts = attempt;
            try
            {
                var dispatch = new NotificationDispatch(
                    NotificationId: row.Id,
                    Channel: row.Channel,
                    Recipient: recipientAddr,
                    Subject: row.Channel == NotificationsConstants.Channels.Email ? row.EventKind : string.Empty,
                    Body: row.PayloadRedactedJson,
                    Locale: row.Locale,
                    MarketCode: row.MarketCode,
                    IdempotencyKey: row.IdempotencyKey,
                    Headers: new Dictionary<string, string>
                    {
                        ["x-correlation-id"] = row.CorrelationId.ToString("N"),
                        ["x-notification-id"] = row.Id.ToString("N"),
                    });
                result = await provider.SendAsync(dispatch, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Provider {Provider} threw on attempt {Attempt} for {Id}",
                    provider.ProviderId, attempt, row.Id);
                result = SendResult.Transient("provider_exception", "redacted", 0);
            }

            if (result.Accepted || !result.IsTransient) break;
            if (attempt < MaxAttempts)
            {
                try { await Task.Delay(RetryDelays[attempt - 1], ct); }
                catch (OperationCanceledException) { return; }
            }
        }

        var finalNow = _clock.GetUtcNow();
        row.UpdatedAt = finalNow;
        if (result is { Accepted: true })
        {
            NotificationStateMachine.EnsureTransition(row.State, NotificationsConstants.NotificationStates.Delivered);
            row.State = NotificationsConstants.NotificationStates.Delivered;
            row.ProviderMessageId = result.ProviderMessageId;
            row.DeliveredAt = finalNow;
        }
        else if (result is { IsTransient: true })
        {
            NotificationStateMachine.EnsureTransition(row.State, NotificationsConstants.NotificationStates.DeadLetter);
            row.State = NotificationsConstants.NotificationStates.DeadLetter;
            row.FailedReason = result.ErrorCode;
            row.FailedAt = finalNow;
        }
        else
        {
            NotificationStateMachine.EnsureTransition(row.State, NotificationsConstants.NotificationStates.Failed);
            row.State = NotificationsConstants.NotificationStates.Failed;
            row.FailedReason = result?.ErrorCode;
            row.FailedAt = finalNow;
        }
        await db.SaveChangesAsync(ct);
    }
}
