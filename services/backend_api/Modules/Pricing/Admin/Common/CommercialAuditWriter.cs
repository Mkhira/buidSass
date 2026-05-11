using System.Text.Json;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Pricing.Entities;
using BackendApi.Modules.Pricing.Persistence;

namespace BackendApi.Modules.Pricing.Admin.Common;

/// <summary>
/// Spec 007-b dual-write audit helper. Every commercial-authoring action MUST
/// write both:
/// <list type="bullet">
///   <item>a row in <c>pricing.commercial_audit_events</c> (data-model §2.9 — append-only,
///   trigger-enforced; the per-target detail trail consumed by the operator UI's
///   "Audit summary" panel and by SC-003 audit-coverage script).</item>
///   <item>a row in <c>shared.audit_log_entries</c> via <see cref="IAuditEventPublisher"/>
///   (Principle 25 — the cross-cutting platform audit channel).</item>
/// </list>
/// Failing to write either is a Principle 25 violation. This helper coordinates
/// both writes inside the caller's <see cref="PricingDbContext.SaveChangesAsync"/>
/// transaction so a rollback on either side rolls back both.
/// </summary>
public sealed class CommercialAuditWriter
{
    private readonly PricingDbContext _db;
    private readonly IAuditEventPublisher _audit;

    public CommercialAuditWriter(PricingDbContext db, IAuditEventPublisher audit)
    {
        _db = db;
        _audit = audit;
    }

    /// <summary>
    /// Append a commercial audit event for the given target. The dual write is
    /// staged in two phases:
    /// <list type="number">
    ///   <item><c>StageLocal</c> — queue the
    ///   <c>pricing.commercial_audit_events</c> row on the caller's
    ///   <see cref="PricingDbContext"/> change-tracker and capture the platform
    ///   <see cref="AuditEvent"/> payload. The caller is expected to commit
    ///   the DbContext (<c>SaveChangesAsync</c>) as part of their own
    ///   transaction.</item>
    ///   <item><c>PublishPlatformAfterCommit</c> — once the DbContext save
    ///   succeeds, the caller invokes the returned delegate to publish the
    ///   platform <c>audit_log_entries</c> row. Publishing before commit is a
    ///   bug: a later <c>SaveChangesAsync</c> failure leaves the two channels
    ///   out of sync (CodeRabbit PR #78 round 1 Major).</item>
    /// </list>
    /// Most call sites use <see cref="AppendAndCommitAsync"/> which wraps both
    /// phases in the correct order; legacy paths that need a custom commit
    /// boundary may stage and publish manually via the returned delegate.
    /// </summary>
    /// <param name="targetEntityKind">One of: coupon, promotion, campaign,
    /// business_pricing, preview_profile, commercial_threshold, commercial_approval.</param>
    /// <param name="kind">One of the 18 audit-event-kind enum values in data-model §5.</param>
    public Func<CancellationToken, Task> StageLocal(
        string targetEntityKind,
        Guid targetEntityId,
        string kind,
        Guid actorId,
        string actorRole,
        object? before,
        object? after,
        object? diff,
        string? reasonNote,
        Guid? correlationId,
        DateTimeOffset nowUtc)
    {
        var beforeJson = before is null ? null : JsonSerializer.Serialize(before);
        var afterJson = after is null ? null : JsonSerializer.Serialize(after);
        var diffJson = diff is null ? null : JsonSerializer.Serialize(diff);

        _db.CommercialAuditEvents.Add(new CommercialAuditEvent
        {
            Id = Guid.NewGuid(),
            TargetEntityKind = targetEntityKind,
            TargetEntityId = targetEntityId,
            Kind = kind,
            ActorId = actorId,
            ActorRole = actorRole,
            BeforeJson = beforeJson,
            AfterJson = afterJson,
            DiffJson = diffJson,
            ReasonNote = reasonNote,
            RecordedAtUtc = nowUtc,
            CorrelationId = correlationId,
        });

        // Capture the platform-channel payload so the caller can publish AFTER
        // a successful SaveChangesAsync. Closing over `_audit` keeps the
        // delegate self-contained.
        return ct => _audit.PublishAsync(
            new AuditEvent(
                ActorId: actorId,
                ActorRole: actorRole,
                Action: kind,
                EntityType: PascalCaseEntityType(targetEntityKind),
                EntityId: targetEntityId,
                BeforeState: before,
                AfterState: after,
                Reason: reasonNote),
            ct);
    }

    /// <summary>
    /// Backwards-compatible wrapper that stages the local row, calls
    /// <see cref="PricingDbContext.SaveChangesAsync"/> on the shared context,
    /// and only then publishes the platform <c>audit_log_entries</c> row. Use
    /// this when the calling handler does not perform additional writes after
    /// the audit append. Handlers that DO need to add/commit other entities
    /// (e.g., the coupon row alongside the audit) should call
    /// <see cref="StageLocal"/> directly, then save, then invoke the returned
    /// delegate so all writes commit in a single transaction.
    /// </summary>
    public async Task AppendAndCommitAsync(
        string targetEntityKind,
        Guid targetEntityId,
        string kind,
        Guid actorId,
        string actorRole,
        object? before,
        object? after,
        object? diff,
        string? reasonNote,
        Guid? correlationId,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        var publish = StageLocal(targetEntityKind, targetEntityId, kind, actorId, actorRole,
            before, after, diff, reasonNote, correlationId, nowUtc);
        await _db.SaveChangesAsync(ct);
        await publish(ct);
    }

    private static string PascalCaseEntityType(string snakeOrLower) => snakeOrLower switch
    {
        "coupon" => "Coupon",
        "promotion" => "Promotion",
        "campaign" => "Campaign",
        "business_pricing" => "BusinessPricing",
        "preview_profile" => "PreviewProfile",
        "commercial_threshold" => "CommercialThreshold",
        "commercial_approval" => "CommercialApproval",
        _ => snakeOrLower,
    };
}
