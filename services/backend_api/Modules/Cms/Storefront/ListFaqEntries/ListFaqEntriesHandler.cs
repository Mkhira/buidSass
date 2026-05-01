using BackendApi.Modules.Cms.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Cms.Storefront.ListFaqEntries;

public sealed record ListFaqEntriesQuery(string Market, string Locale, string? Category);

public sealed record ListFaqEntriesResponse(IReadOnlyList<FaqEntryResponseItem> Items);

public sealed record FaqEntryResponseItem(
    Guid Id,
    string Category,
    string MarketCode,
    int DisplayOrder,
    string Question,
    string Answer);

public sealed class ListFaqEntriesHandler
{
    private readonly CmsDbContext _db;
    private readonly StorefrontContentResolver _resolver;
    private readonly TimeProvider _clock;

    public ListFaqEntriesHandler(CmsDbContext db, StorefrontContentResolver resolver, TimeProvider clock)
    {
        _db = db;
        _resolver = resolver;
        _clock = clock;
    }

    public async Task<ListFaqEntriesResponse> HandleAsync(ListFaqEntriesQuery query, CancellationToken ct)
    {
        IQueryable<BackendApi.Modules.Cms.Entities.FaqEntry> filtered =
            _resolver.ApplyStorefrontFilterStateOnly(_db.FaqEntries.AsNoTracking(), query.Market);

        if (!string.IsNullOrEmpty(query.Category))
        {
            filtered = filtered.Where(f => f.CategoryWire == query.Category);
        }

        var rows = await filtered
            .OrderBy(f => f.DisplayOrder)
            .ThenBy(f => f.CreatedAtUtc)
            .ToListAsync(ct);

        var items = rows.Select(r =>
        {
            var question = query.Locale == "ar" ? r.QuestionAr : r.QuestionEn;
            var answer = query.Locale == "ar" ? r.AnswerAr : r.AnswerEn;
            return new FaqEntryResponseItem(
                r.Id, r.CategoryWire, r.MarketCode, r.DisplayOrder,
                question ?? string.Empty, answer ?? string.Empty);
        })
        .Where(i => !string.IsNullOrEmpty(i.Question))
        .ToList();

        return new ListFaqEntriesResponse(items);
    }
}
