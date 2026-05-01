using BackendApi.Modules.Cms.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Cms.LegalOwner.ListLegalPageVersionHistory;

/// <summary>
/// GET /v1/admin/cms/legal-pages/{kind}/versions per spec 024 contract §5.1.
/// Returns all versions (draft + scheduled + live + superseded) for the
/// requested (legal_page_kind, market_code), ordered by effective_at_utc DESC
/// with stable secondary sort by created_at_utc DESC.
/// Permitted by cms.legal_owner | cms.viewer.finance | super_admin.
/// </summary>
public sealed class ListLegalPageVersionHistoryHandler
{
    private readonly CmsDbContext _db;

    public ListLegalPageVersionHistoryHandler(CmsDbContext db)
    {
        _db = db;
    }

    public async Task<ListLegalPageVersionHistoryResponse> HandleAsync(
        string legalPageKindWire,
        string marketCode,
        CancellationToken ct)
    {
        var rows = await _db.LegalPageVersions
            .AsNoTracking()
            .Where(v => v.LegalPageKindWire == legalPageKindWire && v.MarketCode == marketCode)
            .OrderByDescending(v => v.EffectiveAtUtc ?? v.CreatedAtUtc)
            .ThenByDescending(v => v.CreatedAtUtc)
            .ToListAsync(ct);

        var items = rows.Select(r => new LegalPageVersionHistoryItem(
            VersionId: r.Id,
            LegalPageKindWire: r.LegalPageKindWire,
            MarketCode: r.MarketCode,
            VersionLabel: r.VersionLabel,
            State: r.StateWire,
            EffectiveAtUtc: r.EffectiveAtUtc,
            PublishedAtUtc: r.PublishedAtUtc,
            SupersededAtUtc: r.SupersededAtUtc,
            SupersededByVersionId: r.SupersededByVersionId,
            CreatedAtUtc: r.CreatedAtUtc,
            EditorSaveAtUtc: r.EditorSaveAtUtc,
            OwnerActorId: r.OwnerActorId,
            Xmin: r.Xmin)).ToList();

        return new ListLegalPageVersionHistoryResponse(items);
    }
}
