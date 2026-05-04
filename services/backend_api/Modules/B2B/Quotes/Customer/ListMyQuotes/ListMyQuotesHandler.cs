using BackendApi.Modules.B2B.Persistence;
using BackendApi.Modules.B2B.Primitives;
using BackendApi.Modules.B2B.Quotes.Customer.Visibility;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.B2B.Quotes.Customer.ListMyQuotes;

/// <summary>
/// Spec 021 contract §2.3 — paginated list of quotes the caller can see.
///
/// Visibility (delegated to <see cref="CustomerQuoteVisibility"/>):
/// caller's individual quotes (no <c>company_id</c>) PLUS quotes of any company
/// where caller holds a <c>buyer</c> / <c>approver</c> / <c>companies.admin</c>
/// membership.
///
/// Filtering: optional <c>state</c> CSV (validator-checked) AND optional
/// <c>company_id</c>. The company-id filter, when supplied, MUST also pass the
/// visibility set — a non-member who passes a known company id sees an empty
/// list rather than a 403 (visibility-leak prevention mirrors §2.4).
///
/// Sort: <c>newest</c> (default) sorts by <c>requested_at DESC</c>;
/// <c>oldest</c> sorts ASC. The <c>IX_quotes_state_market_requested</c> index
/// covers the non-terminal subset; for fully-paginated terminal queries the
/// fallback is a sequential scan — acceptable at V1 traffic per research §R8.
///
/// Soft-delete: spec 021 has NO row-level deletion. "Removed" quotes transition
/// into a terminal state (<c>accepted</c> | <c>rejected</c> | <c>expired</c> |
/// <c>withdrawn</c>) and stay queryable for audit / history per the immutable
/// state-transition ledger (<see cref="QuoteStateTransition"/>). Clients that
/// want a non-terminal-only view pass <c>?state=requested,drafted,revised,pending-approver</c>.
/// </summary>
public sealed class ListMyQuotesHandler
{
    private readonly B2BDbContext _db;

    public ListMyQuotesHandler(B2BDbContext db)
    {
        _db = db;
    }

    public async Task<ListMyQuotesResponse> HandleAsync(
        Guid customerId,
        ListMyQuotesRequest request,
        CancellationToken ct)
    {
        // Resolve query-string defaults. Validator already enforced bounds.
        var page = request.Page ?? ListMyQuotesValidator.DefaultPage;
        var pageSize = request.PageSize ?? ListMyQuotesValidator.DefaultPageSize;
        var sort = request.Sort ?? ListMyQuotesSort.Newest;

        // ---------- Visibility: which company ids does this caller see? ----------
        var visibleCompanyIds = await CustomerQuoteVisibility.GetVisibleCompanyIdsAsync(_db, customerId, ct);

        // ---------- State CSV → typed token list ----------
        var stateTokens = ParseStateCsv(request.State);

        // ---------- Build the LINQ pipeline (single SELECT against b2b.quotes) ----------
        var query = _db.Quotes
            .AsNoTracking()
            .Where(CustomerQuoteVisibility.BuildListPredicate(customerId, visibleCompanyIds));

        if (request.CompanyId is { } companyFilter)
        {
            // Constrains to a single company id — irrelevant for individual-only callers
            // but MUST also pass the visibility predicate (defense in depth: if a caller
            // passes a company id outside their visible set, the result is empty, not 403).
            query = query.Where(q => q.CompanyId == companyFilter);
        }

        if (stateTokens.Count > 0)
        {
            query = query.Where(q => stateTokens.Contains(q.State));
        }

        // Total count BEFORE pagination — same predicate.
        var total = await query.CountAsync(ct);

        // Sort + paginate + project to the wire shape (avoids materializing the full Quote
        // entity per row; the column subset matches IX_quotes_customer_state coverage).
        IQueryable<ListMyQuotesItem> projected = sort == ListMyQuotesSort.Oldest
            ? query.OrderBy(q => q.RequestedAt).ThenBy(q => q.Id)
                   .Select(q => new ListMyQuotesItem(
                       q.Id,
                       q.State,
                       q.MarketCode,
                       q.CompanyId,
                       q.BranchId,
                       q.PoNumber,
                       q.RequestedAt,
                       q.ExpiresAt,
                       q.DecidedAt,
                       q.CurrentVersion == null ? (int?)null : q.CurrentVersion.VersionNumber))
            : query.OrderByDescending(q => q.RequestedAt).ThenByDescending(q => q.Id)
                   .Select(q => new ListMyQuotesItem(
                       q.Id,
                       q.State,
                       q.MarketCode,
                       q.CompanyId,
                       q.BranchId,
                       q.PoNumber,
                       q.RequestedAt,
                       q.ExpiresAt,
                       q.DecidedAt,
                       q.CurrentVersion == null ? (int?)null : q.CurrentVersion.VersionNumber));

        var items = await projected
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new ListMyQuotesResponse(
            Items: items,
            Page: page,
            PageSize: pageSize,
            Total: total);
    }

    /// <summary>
    /// Parses the <c>?state=</c> CSV into the set of stable string tokens stored on
    /// <c>quotes.state</c>. The validator already pre-flighted that every token
    /// resolves to a <see cref="QuoteState"/> — the parse here cannot throw.
    /// </summary>
    private static HashSet<string> ParseStateCsv(string? csv)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(csv)) return result;
        foreach (var token in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (QuoteStateExtensions.TryParseToken(token, out var s))
            {
                result.Add(s.ToToken());
            }
        }
        return result;
    }
}
