using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Cms.Entities;
using BackendApi.Modules.Cms.Persistence;
using BackendApi.Modules.Cms.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Cms.Preview.MintPreviewToken;

public sealed record MintPreviewTokenRequest(int? TtlHours);

public sealed record MintPreviewTokenResponse(
    string Token,
    string TokenHashHex,
    DateTimeOffset ExpiresAtUtc,
    string Url);

public sealed record MintPreviewTokenResult(
    bool IsSuccess,
    MintPreviewTokenResponse? Response,
    string? ReasonCode,
    string? Detail,
    int Status)
{
    public static MintPreviewTokenResult Ok(MintPreviewTokenResponse response) => new(true, response, null, null, 201);
    public static MintPreviewTokenResult Reject(string reason, string detail, int status) =>
        new(false, null, reason, detail, status);
}

/// <summary>
/// POST /v1/admin/cms/{kind}/{id}/preview-token per spec 024 contract §6.
/// Mints an HMAC-signed opaque token, persists its SHA-256 hash; the token is
/// returned ONCE in the response. TTL bounded [1h, 168h]; default per market
/// schema. Rate-limited 30/h/actor at the endpoint level.
/// </summary>
public sealed class MintPreviewTokenHandler
{
    private const int MinTtlHours = 1;
    private const int MaxTtlHours = 168; // 7 days.

    private readonly CmsDbContext _db;
    private readonly PreviewTokenSigner _signer;
    private readonly IAuditEventPublisher _audit;
    private readonly TimeProvider _clock;

    public MintPreviewTokenHandler(
        CmsDbContext db,
        PreviewTokenSigner signer,
        IAuditEventPublisher audit,
        TimeProvider clock)
    {
        _db = db;
        _signer = signer;
        _audit = audit;
        _clock = clock;
    }

    public async Task<MintPreviewTokenResult> HandleAsync(
        EntityKind entityKind,
        Guid entityId,
        Guid actorId,
        string actorRole,
        MintPreviewTokenRequest body,
        CancellationToken ct)
    {
        var nowUtc = _clock.GetUtcNow();
        var ttl = body.TtlHours ?? 24;
        if (ttl < MinTtlHours || ttl > MaxTtlHours)
        {
            return MintPreviewTokenResult.Reject(
                CmsReasonCode.PreviewTtlOutOfRange,
                $"ttl_hours must be in [{MinTtlHours}, {MaxTtlHours}].",
                400);
        }

        // Confirm the target entity actually exists in the appropriate kind.
        var exists = await EntityExistsAsync(entityKind, entityId, ct);
        if (!exists)
        {
            return MintPreviewTokenResult.Reject(
                CmsReasonCode.PreviewEntityNotFound,
                "Preview target entity not found.",
                404);
        }

        var ttlSeconds = ttl * 3600;
        var claims = new PreviewTokenClaims(
            EntityKind: entityKind,
            EntityId: entityId,
            VersionId: entityId,
            MintTimestampUtc: nowUtc,
            TtlSeconds: ttlSeconds,
            ActorRoleAtMint: actorRole);

        var token = _signer.Sign(claims);
        var tokenHash = PreviewTokenSigner.HashToken(token);
        var expiresAt = nowUtc.AddSeconds(ttlSeconds);

        await _db.PreviewTokens.AddAsync(new CmsPreviewToken
        {
            Id = Guid.NewGuid(),
            TokenHash = tokenHash,
            EntityKindWire = entityKind.ToWire(),
            EntityId = entityId,
            VersionId = entityId,
            ActorRoleAtMint = actorRole,
            MintedByActorId = actorId,
            MintedAtUtc = nowUtc,
            ExpiresAtUtc = expiresAt,
        }, ct);
        await _db.SaveChangesAsync(ct);

        await _audit.PublishAsync(new AuditEvent(
            ActorId: actorId,
            ActorRole: actorRole,
            Action: "cms.preview_token.minted",
            EntityType: $"cms.{entityKind.ToWire()}",
            EntityId: entityId,
            BeforeState: null,
            AfterState: new
            {
                EntityKind = entityKind.ToWire(),
                EntityId = entityId,
                ExpiresAtUtc = expiresAt,
                TokenHashHex = Convert.ToHexString(tokenHash),
            },
            Reason: null), ct);

        var response = new MintPreviewTokenResponse(
            Token: token,
            TokenHashHex: Convert.ToHexString(tokenHash),
            ExpiresAtUtc: expiresAt,
            Url: $"/v1/storefront/cms/preview/{entityKind.ToWire()}/{entityId}?token={Uri.EscapeDataString(token)}");
        return MintPreviewTokenResult.Ok(response);
    }

    private async Task<bool> EntityExistsAsync(EntityKind kind, Guid id, CancellationToken ct) =>
        kind switch
        {
            EntityKind.BannerSlot =>
                await _db.BannerSlots.AnyAsync(b => b.Id == id, ct),
            EntityKind.FeaturedSection =>
                await _db.FeaturedSections.AnyAsync(b => b.Id == id, ct),
            EntityKind.FaqEntry =>
                await _db.FaqEntries.AnyAsync(b => b.Id == id, ct),
            EntityKind.BlogArticle =>
                await _db.BlogArticles.AnyAsync(b => b.Id == id, ct),
            EntityKind.LegalPageVersion =>
                await _db.LegalPageVersions.AnyAsync(b => b.Id == id, ct),
            _ => false,
        };
}
