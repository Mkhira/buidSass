using BackendApi.Modules.B2B.Entities;
using BackendApi.Modules.B2B.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.B2B.Quotes.Customer.Visibility;

/// <summary>
/// Spec 021 contract §2.3 / §2.4 — single source of truth for "which quotes can
/// this customer see?". Used by both <c>ListMyQuotesHandler</c> (LINQ predicate) and
/// <c>GetMyQuoteHandler</c> (single-row visibility check).
///
/// The visibility rules per §2.3:
/// <list type="bullet">
///   <item>Caller's own individual quotes (<c>customer_id == caller</c>, <c>company_id IS NULL</c>).</item>
///   <item>Quotes of any company where caller holds <c>buyer</c> or <c>approver</c> membership.</item>
///   <item>Every quote of any company where caller holds <c>companies.admin</c> membership.</item>
/// </list>
///
/// Spec §2.4 inherits these same rules: "caller must be the customer-owner OR a
/// member of the quote's company with role visible-to-the-quote (per 2.3 rules)".
///
/// **Visibility-leak prevention** (§2.4): unauthorized callers get <c>404 quote.not_found</c>,
/// NOT <c>403</c>. Treating "exists but you can't see it" as "doesn't exist" prevents
/// the API from leaking the existence of company quotes to non-members. The single-row
/// helper <see cref="GetVisibleQuoteAsync"/> is what enforces this — callers that can
/// short-circuit on null get the visibility-leak guarantee for free.
///
/// All three roles (<c>buyer</c>, <c>approver</c>, <c>companies.admin</c>) confer the
/// SAME visibility on the read surface — distinctions matter at write time (only
/// <c>companies.admin</c> can withdraw company quotes per §2.5; only <c>approver</c>
/// can finalize-acceptance per §3.2). Cycle C-2 / Cycle C-3 enforces those role
/// distinctions at their respective handler boundaries.
/// </summary>
public static class CustomerQuoteVisibility
{
    /// <summary>
    /// Roles on a <see cref="CompanyMembership"/> row that confer read access to
    /// any of the company's quotes. Per §2.3 every active membership grants list-and-get
    /// visibility; role-specific WRITE permissions are checked at the handler boundary.
    /// </summary>
    public static readonly IReadOnlyCollection<string> VisibleRoles = new[]
    {
        "buyer",
        "approver",
        "companies.admin",
    };

    /// <summary>
    /// Returns the set of company ids where <paramref name="customerId"/> holds at
    /// least one membership row with a role that confers read visibility. The
    /// caller plugs this into the LINQ <c>WHERE</c> predicate or the single-row
    /// visibility check.
    ///
    /// We materialize the set into memory rather than sub-query because:
    /// <list type="bullet">
    ///   <item>The membership table is small per-customer (most customers hold ≤ 5 memberships).</item>
    ///   <item>The List query then becomes a single index-friendly <c>company_id IN (...)</c>
    ///         predicate that hits <c>IX_quotes_company_state_market</c>.</item>
    ///   <item>The Get query short-circuits on the in-memory set without a second round trip.</item>
    /// </list>
    /// </summary>
    public static async Task<IReadOnlySet<Guid>> GetVisibleCompanyIdsAsync(
        B2BDbContext db,
        Guid customerId,
        CancellationToken ct)
    {
        var ids = await db.CompanyMemberships
            .AsNoTracking()
            .Where(m => m.UserId == customerId && VisibleRoles.Contains(m.Role))
            .Select(m => m.CompanyId)
            .Distinct()
            .ToListAsync(ct);
        return ids.ToHashSet();
    }

    /// <summary>
    /// Single-row visibility-gated read. Returns the quote if and only if
    /// <paramref name="customerId"/> can see it per §2.3 / §2.4 rules; returns
    /// <c>null</c> for both the not-found and not-authorized cases — the caller
    /// surfaces 404 in both branches to honor visibility-leak prevention.
    ///
    /// Tracking: <see cref="QueryTrackingBehavior.NoTracking"/>. Read-only path —
    /// callers that want to mutate the quote (Cycle C-2 Withdraw / RequestRevision)
    /// re-fetch with tracking enabled in their own handler.
    /// </summary>
    public static async Task<Quote?> GetVisibleQuoteAsync(
        B2BDbContext db,
        Guid quoteId,
        Guid customerId,
        IReadOnlySet<Guid> visibleCompanyIds,
        CancellationToken ct)
    {
        var quote = await db.Quotes
            .AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == quoteId, ct);
        if (quote is null) return null;

        if (quote.CompanyId is null)
        {
            // Individual-customer quote — only the owner can see it.
            return quote.CustomerId == customerId ? quote : null;
        }

        // Company quote — visible if the caller has any qualifying membership row.
        return visibleCompanyIds.Contains(quote.CompanyId.Value) ? quote : null;
    }

    /// <summary>
    /// LINQ predicate factory for the List query. Combines the two visibility
    /// branches (own-individual + visible-company) into a single
    /// <c>WHERE</c> clause that the EF provider can translate to an indexed query.
    ///
    /// The result is a parameterized expression — embedded in
    /// <see cref="ListMyQuotesHandler"/> as the only WHERE on the
    /// <c>quotes</c> table.
    /// </summary>
    public static System.Linq.Expressions.Expression<Func<Quote, bool>> BuildListPredicate(
        Guid customerId,
        IReadOnlyCollection<Guid> visibleCompanyIds)
    {
        // Capture as a local variable so EF's expression-parameter rewriter sees
        // a stable closure target (some EF Core versions choke on direct
        // IReadOnlyCollection captures inside Contains).
        var ids = visibleCompanyIds.ToArray();
        return q =>
            (q.CompanyId == null && q.CustomerId == customerId)
            || (q.CompanyId != null && ids.Contains(q.CompanyId.Value));
    }
}
