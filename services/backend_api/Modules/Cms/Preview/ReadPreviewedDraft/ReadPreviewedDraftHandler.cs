using BackendApi.Modules.Cms.Persistence;
using BackendApi.Modules.Cms.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Cms.Preview.ReadPreviewedDraft;

public sealed record ReadPreviewedDraftResult(
    bool IsSuccess,
    object? Payload,
    string? ReasonCode,
    string? Detail,
    int Status)
{
    public static ReadPreviewedDraftResult Ok(object payload) => new(true, payload, null, null, 200);
    public static ReadPreviewedDraftResult Reject(string reason, string detail, int status) =>
        new(false, null, reason, detail, status);
}

/// <summary>
/// GET /v1/storefront/cms/preview/{kind}/{id}?token=...
/// Verifies HMAC signature; checks token-store row by SHA-256 hash; rejects
/// if expired or revoked. Loads the entity (any state) and returns the
/// payload; the endpoint sets X-Robots-Tag: noindex, nofollow and adds
/// preview_banner_marker=true.
/// </summary>
public sealed class ReadPreviewedDraftHandler
{
    private readonly CmsDbContext _db;
    private readonly PreviewTokenSigner _signer;
    private readonly TimeProvider _clock;

    public ReadPreviewedDraftHandler(
        CmsDbContext db,
        PreviewTokenSigner signer,
        TimeProvider clock)
    {
        _db = db;
        _signer = signer;
        _clock = clock;
    }

    public async Task<ReadPreviewedDraftResult> HandleAsync(
        EntityKind kind,
        Guid entityId,
        string token,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return ReadPreviewedDraftResult.Reject(
                CmsReasonCode.PreviewTokenSignatureInvalid, "Token is required.", 401);
        }

        PreviewTokenClaims claims;
        try
        {
            claims = _signer.Verify(token);
        }
        catch (PreviewTokenSignatureInvalidException)
        {
            return ReadPreviewedDraftResult.Reject(
                CmsReasonCode.PreviewTokenSignatureInvalid, "Token signature is invalid.", 401);
        }

        if (claims.EntityKind != kind || claims.EntityId != entityId)
        {
            return ReadPreviewedDraftResult.Reject(
                CmsReasonCode.PreviewTokenSignatureInvalid,
                "Token does not match the requested entity.", 401);
        }

        var nowUtc = _clock.GetUtcNow();
        if (claims.IsExpiredAt(nowUtc))
        {
            return ReadPreviewedDraftResult.Reject(
                CmsReasonCode.PreviewTokenExpiredOrRevoked, "Token has expired.", 401);
        }

        var tokenHash = PreviewTokenSigner.HashToken(token);
        var stored = await _db.PreviewTokens.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);
        if (stored is null)
        {
            return ReadPreviewedDraftResult.Reject(
                CmsReasonCode.PreviewTokenNotFound, "Preview token not found.", 401);
        }
        if (stored.RevokedAtUtc is not null)
        {
            return ReadPreviewedDraftResult.Reject(
                CmsReasonCode.PreviewTokenExpiredOrRevoked, "Preview token has been revoked.", 401);
        }
        if (stored.ExpiresAtUtc <= nowUtc)
        {
            return ReadPreviewedDraftResult.Reject(
                CmsReasonCode.PreviewTokenExpiredOrRevoked, "Preview token has expired.", 401);
        }

        object? payload = await LoadEntityAsync(kind, entityId, ct);
        if (payload is null)
        {
            return ReadPreviewedDraftResult.Reject(
                CmsReasonCode.PreviewEntityNotFound, "Preview target entity not found.", 404);
        }

        return ReadPreviewedDraftResult.Ok(new { entity = payload, preview_banner_marker = true });
    }

    private async Task<object?> LoadEntityAsync(EntityKind kind, Guid id, CancellationToken ct) => kind switch
    {
        EntityKind.BannerSlot =>
            await _db.BannerSlots.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct),
        EntityKind.FeaturedSection =>
            await _db.FeaturedSections.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct),
        EntityKind.FaqEntry =>
            await _db.FaqEntries.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct),
        EntityKind.BlogArticle =>
            await _db.BlogArticles.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct),
        EntityKind.LegalPageVersion =>
            await _db.LegalPageVersions.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct),
        _ => null,
    };
}
