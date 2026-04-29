using BackendApi.Modules.AuditLog;
using Microsoft.AspNetCore.Http;

namespace BackendApi.Modules.Verification.Primitives;

/// <summary>
/// Concrete <see cref="IPiiAccessRecorder"/> writing
/// <c>verification.pii_access</c> audit events via the platform
/// <see cref="IAuditEventPublisher"/>. Per spec 020 research §R13 / FR-015a-e:
/// every read of a regulator identifier or document body MUST flow through
/// this chokepoint so the audit trail captures who read what when.
/// </summary>
public sealed class PiiAccessRecorder(
    IAuditEventPublisher auditPublisher,
    IHttpContextAccessor httpContextAccessor) : IPiiAccessRecorder
{
    public async Task RecordAsync(
        PiiAccessKind kind,
        Guid verificationId,
        Guid? documentId,
        CancellationToken ct)
    {
        var (actorId, actorRole, surface) = ResolveContext();

        await auditPublisher.PublishAsync(new AuditEvent(
                ActorId: actorId,
                ActorRole: actorRole,
                Action: "verification.pii_access",
                EntityType: "verification",
                EntityId: verificationId,
                BeforeState: null,
                AfterState: new
                {
                    kind = kind.ToWireValue(),
                    document_id = documentId,
                    surface,
                },
                Reason: "pii_access"),
            ct);
    }

    /// <summary>
    /// Variant for the historical-document open path that emits two audit
    /// events per spec 020 contracts §3.7: one for the body read, one for the
    /// "open historical document" action with <c>surface=admin_review</c> so
    /// the audit dashboard can flag terminal-state PII opens for review.
    /// </summary>
    public async Task RecordHistoricalDocumentOpenAsync(
        Guid verificationId,
        Guid documentId,
        CancellationToken ct)
    {
        var (actorId, actorRole, _) = ResolveContext();

        // Event 1 — body read.
        await auditPublisher.PublishAsync(new AuditEvent(
                ActorId: actorId,
                ActorRole: actorRole,
                Action: "verification.pii_access",
                EntityType: "verification",
                EntityId: verificationId,
                BeforeState: null,
                AfterState: new
                {
                    kind = PiiAccessKind.DocumentBodyRead.ToWireValue(),
                    document_id = (Guid?)documentId,
                    surface = "admin_review",
                },
                Reason: "pii_access"),
            ct);

        // Event 2 — "open historical document" action stamp.
        await auditPublisher.PublishAsync(new AuditEvent(
                ActorId: actorId,
                ActorRole: actorRole,
                Action: "verification.pii_access.historical_open",
                EntityType: "verification",
                EntityId: verificationId,
                BeforeState: null,
                AfterState: new
                {
                    kind = PiiAccessKind.DocumentBodyRead.ToWireValue(),
                    document_id = (Guid?)documentId,
                    surface = "admin_review",
                    historical = true,
                },
                Reason: "pii_access_historical_open"),
            ct);
    }

    private (Guid actorId, string actorRole, string surface) ResolveContext()
    {
        var ctx = httpContextAccessor.HttpContext;
        if (ctx is null)
        {
            return (Guid.Empty, "system", "unknown");
        }

        var sub = ctx.User.FindFirst("sub")?.Value
            ?? ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var actorId = Guid.TryParse(sub, out var id) ? id : Guid.Empty;

        var actorRole = ctx.User.FindFirst("role")?.Value
            ?? (ctx.Request.Path.StartsWithSegments("/api/admin") ? "reviewer" : "customer");

        var surface = ctx.Request.Path.StartsWithSegments("/api/admin/verifications")
            ? "admin_review"
            : ctx.Request.Path.StartsWithSegments("/api/admin/customers")
                ? "admin_customers"
                : ctx.Request.Path.StartsWithSegments("/api/admin/support")
                    ? "admin_support"
                    : "unknown";

        return (actorId, actorRole, surface);
    }
}
