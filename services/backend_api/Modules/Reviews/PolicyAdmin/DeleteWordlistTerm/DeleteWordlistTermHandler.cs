using BackendApi.Modules.Reviews.Filtering;
using BackendApi.Modules.Reviews.Persistence;
using BackendApi.Modules.Search.Primitives.Normalization;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Reviews.PolicyAdmin.DeleteWordlistTerm;

/// <summary>
/// DELETE /api/admin/reviews/policy/wordlists per contract §4.3. Deleting a
/// term is a hard delete — no historical retention requirement on the wordlist
/// itself. Existing reviews held for moderation that tripped on the deleted
/// term are NOT auto-resolved (per Edge Cases note in spec); a moderator must
/// resolve them manually.
/// </summary>
public sealed class DeleteWordlistTermHandler
{
    private readonly ReviewsDbContext _db;
    private readonly IArabicNormalizer _normalizer;
    private readonly ProfanityFilter _filter;

    public DeleteWordlistTermHandler(
        ReviewsDbContext db,
        IArabicNormalizer normalizer,
        ProfanityFilter filter)
    {
        _db = db;
        _normalizer = normalizer;
        _filter = filter;
    }

    public async Task<bool> HandleAsync(string marketCode, string rawTerm, CancellationToken ct)
    {
        var normalized = _normalizer.Normalize(rawTerm).ToLowerInvariant();
        var existing = await _db.Wordlists
            .FirstOrDefaultAsync(w => w.MarketCode == marketCode && w.Term == normalized, ct);
        if (existing is null) return false;

        _db.Wordlists.Remove(existing);
        await _db.SaveChangesAsync(ct);
        _filter.Invalidate(marketCode);
        return true;
    }
}
