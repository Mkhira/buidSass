using BackendApi.Modules.Cms.Persistence;
using BackendApi.Modules.Cms.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Cms.Storefront.GetLegalPage;

/// <summary>
/// GET /v1/storefront/cms/legal-pages/{kind} per spec 024 contract §7.6.
/// Single-row substitution: prefer (kind, market) live, else (kind, '*')
/// live, else 404 cms.legal_page.not_found_for_market.
/// </summary>
public sealed class GetLegalPageHandler
{
    private readonly CmsDbContext _db;

    public GetLegalPageHandler(CmsDbContext db)
    {
        _db = db;
    }

    public async Task<GetLegalPageResponse?> HandleAsync(
        string legalPageKindWire,
        string marketCode,
        string locale,
        CancellationToken ct)
    {
        var liveWire = ContentLifecycleState.Live.ToWire();

        // Pull both candidates in a single round-trip and pick the
        // specific-market one if present.
        var candidates = await _db.LegalPageVersions
            .AsNoTracking()
            .Where(v => v.LegalPageKindWire == legalPageKindWire
                && v.StateWire == liveWire
                && (v.MarketCode == marketCode || v.MarketCode == "*"))
            .ToListAsync(ct);

        if (candidates.Count == 0) return null;

        var pick = candidates.FirstOrDefault(c => c.MarketCode == marketCode)
            ?? candidates.FirstOrDefault(c => c.MarketCode == "*");
        if (pick is null) return null;

        var body = locale == "ar" ? pick.BodyAr : pick.BodyEn;
        // Both bodies are required at publish, so a null here means an unsupported locale.
        if (string.IsNullOrEmpty(body)) return null;

        return new GetLegalPageResponse(
            VersionId: pick.Id,
            LegalPageKind: pick.LegalPageKindWire,
            MarketCode: pick.MarketCode,
            VersionLabel: pick.VersionLabel,
            Locale: locale,
            Body: body,
            EffectiveAtUtc: pick.EffectiveAtUtc,
            PublishedAtUtc: pick.PublishedAtUtc);
    }
}

public sealed record GetLegalPageResponse(
    Guid VersionId,
    string LegalPageKind,
    string MarketCode,
    string VersionLabel,
    string Locale,
    string Body,
    DateTimeOffset? EffectiveAtUtc,
    DateTimeOffset? PublishedAtUtc);
