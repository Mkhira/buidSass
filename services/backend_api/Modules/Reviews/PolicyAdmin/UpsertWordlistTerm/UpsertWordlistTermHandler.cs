using BackendApi.Modules.Reviews.Entities;
using BackendApi.Modules.Reviews.Filtering;
using BackendApi.Modules.Reviews.Persistence;
using BackendApi.Modules.Reviews.Primitives;
using BackendApi.Modules.Search.Primitives.Normalization;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Reviews.PolicyAdmin.UpsertWordlistTerm;

/// <summary>
/// PUT /api/admin/reviews/policy/wordlists per contract §4.2 — upserts a term
/// for a single market. Term is Arabic-normalized + lowercased at write time
/// (research §R13) so the runtime profanity filter sees the same canonicalized
/// form. After the write, the per-market in-process cache is invalidated so
/// the next submission picks up the new term within the documented 60 s
/// refresh window (in fact: immediately on the same instance).
/// </summary>
public sealed class UpsertWordlistTermHandler
{
    private readonly ReviewsDbContext _db;
    private readonly IArabicNormalizer _normalizer;
    private readonly ProfanityFilter _filter;
    private readonly TimeProvider _time;

    public UpsertWordlistTermHandler(
        ReviewsDbContext db,
        IArabicNormalizer normalizer,
        ProfanityFilter filter,
        TimeProvider time)
    {
        _db = db;
        _normalizer = normalizer;
        _filter = filter;
        _time = time;
    }

    public async Task<UpsertWordlistTermResult> HandleAsync(
        Guid actorId,
        UpsertWordlistTermRequest body,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Term))
        {
            return UpsertWordlistTermResult.Reject(400,
                ReviewReasonCode.PolicyWordlistTermInvalid,
                "Term must not be empty.");
        }

        // Trim BEFORE normalize+lowercase so " fraud " and "fraud" canonicalize to
        // the same key — preventing near-duplicate policy entries that would also
        // miss expected matches at filter time.
        var normalized = _normalizer.Normalize(body.Term.Trim()).ToLowerInvariant();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > 200)
        {
            return UpsertWordlistTermResult.Reject(400,
                ReviewReasonCode.PolicyWordlistTermInvalid,
                "Term must be 1-200 characters after Arabic normalization + lowercasing.");
        }

        var nowUtc = _time.GetUtcNow();
        var existing = await _db.Wordlists
            .FirstOrDefaultAsync(w => w.MarketCode == body.MarketCode && w.Term == normalized, ct);
        if (existing is null)
        {
            _db.Wordlists.Add(new ReviewsFilterWordlist
            {
                MarketCode = body.MarketCode,
                Term = normalized,
                Severity = body.Severity,
                CreatedByActorId = actorId,
                CreatedAtUtc = nowUtc,
            });
        }
        else
        {
            existing.Severity = body.Severity;
        }

        await _db.SaveChangesAsync(ct);

        // Invalidate in-process cache so subsequent submissions see the new term.
        _filter.Invalidate(body.MarketCode);

        return UpsertWordlistTermResult.Success(new UpsertWordlistTermResponse(
            body.MarketCode, normalized, body.Severity, nowUtc));
    }
}

public sealed record UpsertWordlistTermRequest(string MarketCode, string Term, string? Severity);
public sealed record UpsertWordlistTermResponse(string MarketCode, string Term, string? Severity, DateTimeOffset PersistedAtUtc);

public sealed record UpsertWordlistTermResult(
    bool IsSuccess,
    int Status,
    string? ReasonCode,
    string? Detail,
    UpsertWordlistTermResponse? Response)
{
    public static UpsertWordlistTermResult Success(UpsertWordlistTermResponse r) => new(true, 200, null, null, r);
    public static UpsertWordlistTermResult Reject(int s, string c, string d) => new(false, s, c, d, null);
}
