using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Cms.Persistence;
using BackendApi.Modules.Cms.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Cms.Preview.RevokePreviewToken;

public sealed record RevokePreviewTokenResult(
    bool IsSuccess,
    DateTimeOffset? RevokedAtUtc,
    string? ReasonCode,
    string? Detail,
    int Status)
{
    public static RevokePreviewTokenResult Ok(DateTimeOffset revokedAtUtc) =>
        new(true, revokedAtUtc, null, null, 200);
    public static RevokePreviewTokenResult Reject(string reason, string detail, int status) =>
        new(false, null, reason, detail, status);
}

/// <summary>
/// DELETE /v1/admin/cms/preview-token/{token_hash} per spec 024 contract §6.
/// Idempotent: re-call returns 200 with the original revoked_at_utc.
/// </summary>
public sealed class RevokePreviewTokenHandler
{
    private readonly CmsDbContext _db;
    private readonly IAuditEventPublisher _audit;
    private readonly TimeProvider _clock;

    public RevokePreviewTokenHandler(
        CmsDbContext db,
        IAuditEventPublisher audit,
        TimeProvider clock)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<RevokePreviewTokenResult> HandleAsync(
        byte[] tokenHash,
        Guid actorId,
        string actorRole,
        CancellationToken ct)
    {
        var row = await _db.PreviewTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);
        if (row is null)
        {
            return RevokePreviewTokenResult.Reject(
                CmsReasonCode.PreviewTokenNotFound, "Preview token not found.", 404);
        }

        if (row.RevokedAtUtc is { } already)
        {
            return RevokePreviewTokenResult.Ok(already);
        }

        var nowUtc = _clock.GetUtcNow();
        row.RevokedAtUtc = nowUtc;
        row.RevokedByActorId = actorId;
        await _db.SaveChangesAsync(ct);

        await _audit.PublishAsync(new AuditEvent(
            ActorId: actorId,
            ActorRole: actorRole,
            Action: "cms.preview_token.revoked",
            EntityType: $"cms.{row.EntityKindWire}",
            EntityId: row.EntityId,
            BeforeState: new { Revoked = false },
            AfterState: new { Revoked = true, RevokedAtUtc = nowUtc },
            Reason: null), ct);

        return RevokePreviewTokenResult.Ok(nowUtc);
    }
}
