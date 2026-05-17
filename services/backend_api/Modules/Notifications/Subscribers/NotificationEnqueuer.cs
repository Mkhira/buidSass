using System.Security.Cryptography;
using System.Text;
using BackendApi.Modules.Notifications.Domain;
using BackendApi.Modules.Notifications.Persistence;
using BackendApi.Modules.Notifications.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Notifications.Subscribers;

/// <summary>
/// Default <see cref="INotificationEnqueuer"/> impl. Computes SHA-256 over
/// (correlation_id, channel, recipient_id) for the idempotency key, then upserts
/// — if a row with that key already exists, returns its id; otherwise inserts
/// a new <c>pending</c> notification row.
/// </summary>
public sealed class NotificationEnqueuer : INotificationEnqueuer
{
    private readonly NotificationsDbContext _db;
    private readonly TimeProvider _clock;

    public NotificationEnqueuer(NotificationsDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Guid> EnqueueAsync(EnqueueRequest request, CancellationToken cancellationToken)
    {
        var idempotencyKey = ComputeIdempotencyKey(
            request.CorrelationId, request.EventKind, request.Channel, request.RecipientId);

        var existing = await _db.Notifications
            .Where(n => n.IdempotencyKey == idempotencyKey)
            .Select(n => (Guid?)n.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing.HasValue) return existing.Value;

        var now = _clock.GetUtcNow();
        var row = new Notification
        {
            Id = Guid.NewGuid(),
            CorrelationId = request.CorrelationId,
            RecipientId = request.RecipientId,
            RecipientKind = request.RecipientKind,
            Channel = request.Channel,
            EventKind = request.EventKind,
            MarketCode = request.MarketCode,
            Locale = request.Locale,
            State = NotificationsConstants.NotificationStates.Pending,
            IdempotencyKey = idempotencyKey,
            PayloadRedactedJson = request.PayloadJson,
            CampaignId = request.CampaignId,
            NotBefore = request.NotBefore,
            Attempts = 0,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.Notifications.Add(row);
        await _db.SaveChangesAsync(cancellationToken);
        return row.Id;
    }

    internal static string ComputeIdempotencyKey(Guid correlationId, string eventKind, string channel, Guid? recipientId)
    {
        // event_kind is included so two distinct events sharing the same
        // (correlation_id, channel, recipient_id) — e.g. order.placed and
        // order.confirmed both keyed off OrderId — do not collapse into a
        // single notification row. (BR-3 derivation; CodeRabbit pass-1 fix.)
        var sb = new StringBuilder(80);
        sb.Append(correlationId.ToString("N"));
        sb.Append(':');
        sb.Append(eventKind);
        sb.Append(':');
        sb.Append(channel);
        sb.Append(':');
        sb.Append(recipientId?.ToString("N") ?? "anon");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
