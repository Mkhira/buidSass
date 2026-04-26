using System.Text.Json;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Returns.Entities;
using BackendApi.Modules.Returns.Persistence;
using BackendApi.Modules.Returns.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Returns.Admin.Common;

/// <summary>
/// FR-019. Common helpers for admin-action idempotency, return-state-machine validation, audit
/// emission, and outbox writes. Every admin mutation routes through here so the idempotency
/// key (return_id, action, optionalDiscriminator) and audit signature stay consistent.
///
/// Idempotency is enforced at TWO layers:
///   1. <see cref="WasAlreadyApplied"/> — best-effort fast path; if the record is already
///      visible we short-circuit with deduped=true without opening a transaction.
///   2. The DB-level partial unique index <c>IX_returns_state_transitions_admin_dedup</c>
///      on <c>(ReturnRequestId, Machine, Trigger, Reason)</c> — the actual correctness gate.
///      Concurrent requests that both pass (1) will collide here at SaveChanges; the loser
///      catches the 23505 via <see cref="IsUniqueDedupViolation"/> and returns the deduped
///      response.
/// </summary>
internal static class AdminMutation
{
    public static async Task<bool> WasAlreadyApplied(
        ReturnsDbContext db, Guid returnRequestId, string trigger, string idempotencyDiscriminator, CancellationToken ct)
    {
        var key = BuildIdempotencyKey(trigger, idempotencyDiscriminator);
        return await db.StateTransitions.AnyAsync(
            t => t.ReturnRequestId == returnRequestId
                && t.Machine == ReturnStateTransition.MachineReturn
                && t.Trigger == trigger
                && t.Reason == key,
            ct);
    }

    /// <summary>True when the caught EF exception is the dedup unique-index violation
    /// (Postgres SQLSTATE 23505 on <c>IX_returns_state_transitions_admin_dedup</c>).</summary>
    public static bool IsUniqueDedupViolation(DbUpdateException ex)
    {
        if (ex.InnerException is Npgsql.PostgresException pg && pg.SqlState == "23505")
        {
            return string.Equals(pg.ConstraintName,
                "IX_returns_state_transitions_admin_dedup", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    public static ReturnStateTransition NewReturnTransition(
        Guid returnRequestId, string marketCode, string from, string to, Guid actorAccountId, string trigger,
        string idempotencyDiscriminator, object? contextPayload, DateTimeOffset nowUtc) =>
        new()
        {
            ReturnRequestId = returnRequestId,
            MarketCode = marketCode,
            Machine = ReturnStateTransition.MachineReturn,
            FromState = from,
            ToState = to,
            ActorAccountId = actorAccountId,
            Trigger = trigger,
            Reason = BuildIdempotencyKey(trigger, idempotencyDiscriminator),
            ContextJson = contextPayload is null ? null : JsonSerializer.Serialize(contextPayload),
            OccurredAt = nowUtc,
        };

    public static ReturnsOutboxEntry NewOutbox(string eventType, Guid returnRequestId, string marketCode, object payload, DateTimeOffset nowUtc) =>
        new()
        {
            EventType = eventType,
            AggregateId = returnRequestId,
            MarketCode = marketCode,
            PayloadJson = JsonSerializer.Serialize(payload),
            CommittedAt = nowUtc,
        };

    public static async Task PublishAuditAsync(
        IAuditEventPublisher auditPublisher, Guid actorAccountId, string action,
        Guid returnRequestId, object? before, object? after, string? reason, CancellationToken ct)
    {
        await auditPublisher.PublishAsync(new AuditEvent(
            ActorId: actorAccountId,
            ActorRole: "admin",
            Action: action,
            EntityType: "returns.return_request",
            EntityId: returnRequestId,
            BeforeState: before,
            AfterState: after,
            Reason: reason), ct);
    }

    public static bool ValidateTransition(string from, string to)
        => ReturnStateMachine.IsValidTransition(from, to);

    /// <summary>Per-action idempotency reason stored on the state-transition row. Stable so a
    /// duplicate click hits <see cref="WasAlreadyApplied"/> and short-circuits.</summary>
    private static string BuildIdempotencyKey(string trigger, string discriminator)
        => string.IsNullOrEmpty(discriminator) ? $"trigger={trigger}" : $"trigger={trigger} disc={discriminator}";
}
