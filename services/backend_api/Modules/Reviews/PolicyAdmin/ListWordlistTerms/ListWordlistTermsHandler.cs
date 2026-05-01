using BackendApi.Modules.Reviews.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Reviews.PolicyAdmin.ListWordlistTerms;

public sealed class ListWordlistTermsHandler
{
    private readonly ReviewsDbContext _db;

    public ListWordlistTermsHandler(ReviewsDbContext db) => _db = db;

    public async Task<ListWordlistTermsResponse> HandleAsync(string marketCode, CancellationToken ct)
    {
        var terms = await _db.Wordlists.AsNoTracking()
            .Where(w => w.MarketCode == marketCode)
            .OrderBy(w => w.Term)
            .Select(w => new WordlistTermItem(w.MarketCode, w.Term, w.Severity, w.CreatedAtUtc))
            .ToListAsync(ct);
        return new ListWordlistTermsResponse(terms);
    }
}

public sealed record WordlistTermItem(string MarketCode, string Term, string? Severity, DateTimeOffset CreatedAtUtc);
public sealed record ListWordlistTermsResponse(IReadOnlyList<WordlistTermItem> Items);
