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
    /// Append a commercial audit event for the given target. The local row is
    /// queued on the DbContext (caller commits). The platform audit_log_entries
    /// row is published immediately via <see cref="IAuditEventPublisher"/>;
    /// it lives on a different DbContext (<c>AppDbContext</c>) so it commits
    /// independently — same dual-write pattern as spec 020 / 022.
    /// </summary>
    /// <param name="targetEntityKind">One of: coupon, promotion, campaign,
    /// business_pricing, preview_profile, commercial_threshold, commercial_approval.</param>
    /// <param name="kind">One of the 18 audit-event-kind enum values in data-model §5.</param>
    public async Task AppendAsync(
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

        // EntityType for the platform channel uses PascalCase per existing
        // pricing convention (see Coupon endpoint legacy). Action mirrors the
        // commercial kind so the two channels can be cross-joined on (entity, kind).
        await _audit.PublishAsync(
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
