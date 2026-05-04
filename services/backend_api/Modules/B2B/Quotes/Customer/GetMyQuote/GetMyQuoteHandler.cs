using BackendApi.Modules.B2B.Entities;
using BackendApi.Modules.B2B.Persistence;
using BackendApi.Modules.B2B.Primitives;
using BackendApi.Modules.B2B.Quotes.Customer.Visibility;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.B2B.Quotes.Customer.GetMyQuote;

/// <summary>
/// Spec 021 contract §2.4 — single-quote read with visibility-leak prevention.
///
/// Returns:
/// <list type="bullet">
///   <item><see cref="GetMyQuoteResult.Success"/> with the full quote, current
///         version, prior-version metadata, and derived <c>next_action</c>.</item>
///   <item><see cref="GetMyQuoteResult.NotFound"/> for both "doesn't exist" and
///         "exists but caller can't see it" — the wire surface MUST NOT
///         distinguish these (visibility-leak prevention per §2.4).</item>
/// </list>
///
/// All reads are <see cref="QueryTrackingBehavior.NoTracking"/>. The Drafted state
/// is admin-only-visible per data-model §3.1; we don't expose the
/// <c>customer_invisible</c> filter here because the customer cannot reach the
/// detail screen for a drafted-only quote — they wouldn't see it in the list either
/// (Cycle C-2 will revisit if any UX needs the filtered view).
/// </summary>
public sealed class GetMyQuoteHandler
{
    private readonly B2BDbContext _db;

    public GetMyQuoteHandler(B2BDbContext db)
    {
        _db = db;
    }

    public async Task<GetMyQuoteResult> HandleAsync(
        Guid customerId,
        GetMyQuoteRequest request,
        CancellationToken ct)
    {
        // ---------- Visibility ----------
        var visibleCompanyIds = await CustomerQuoteVisibility
            .GetVisibleCompanyIdsAsync(_db, customerId, ct);

        var quote = await CustomerQuoteVisibility.GetVisibleQuoteAsync(
            _db, request.QuoteId, customerId, visibleCompanyIds, ct);

        if (quote is null)
        {
            // Both "no such quote" and "you can't see this quote" land here.
            // Single 404 path — no leak.
            return GetMyQuoteResult.NotFound();
        }

        // ---------- Versions ----------
        // Pull every published version for this quote in version-number order so
        // the response can split the most-recent (current_version) from the
        // metadata-only history (prior_versions). One round trip — caller surfaces
        // ≤ 1 + N rows where N is small in practice (V1 quotes rarely exceed 3-5
        // revisions; the bounded count makes a sub-projection worthwhile rather
        // than two queries).
        var versions = await _db.QuoteVersions
            .AsNoTracking()
            .Where(v => v.QuoteId == quote.Id)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new
            {
                v.Id,
                v.VersionNumber,
                v.PublishedAt,
                v.LineItemsJson,
                v.TermsTextJson,
                v.TermsDays,
                v.ValidityExtends,
                v.TotalsSummaryJson,
                v.CustomerRevisionCommentJson,
            })
            .ToListAsync(ct);

        GetMyQuoteVersionDetail? currentVersion = null;
        var priorVersions = new List<GetMyQuoteVersionMetadata>(Math.Max(0, versions.Count - 1));

        if (versions.Count > 0)
        {
            var head = versions[0];
            currentVersion = new GetMyQuoteVersionDetail(
                Id: head.Id,
                VersionNumber: head.VersionNumber,
                PublishedAt: head.PublishedAt,
                LineItemsJson: head.LineItemsJson,
                TermsTextJson: head.TermsTextJson,
                TermsDays: head.TermsDays,
                ValidityExtends: head.ValidityExtends,
                TotalsSummaryJson: head.TotalsSummaryJson,
                CustomerRevisionCommentJson: head.CustomerRevisionCommentJson);

            for (var i = 1; i < versions.Count; i++)
            {
                priorVersions.Add(new GetMyQuoteVersionMetadata(
                    Id: versions[i].Id,
                    VersionNumber: versions[i].VersionNumber,
                    PublishedAt: versions[i].PublishedAt));
            }
        }

        // ---------- Derived next_action ----------
        var nextAction = ComputeNextAction(quote, currentVersion is not null);

        var response = new GetMyQuoteResponse(
            Id: quote.Id,
            State: quote.State,
            MarketCode: quote.MarketCode,
            CompanyId: quote.CompanyId,
            BranchId: quote.BranchId,
            PoNumber: quote.PoNumber,
            RequestedAt: quote.RequestedAt,
            ExpiresAt: quote.ExpiresAt,
            DecidedAt: quote.DecidedAt,
            TerminalAt: quote.TerminalAt,
            InvoiceBilling: quote.InvoiceBilling,
            CurrentVersion: currentVersion,
            PriorVersions: priorVersions,
            NextAction: nextAction);

        return GetMyQuoteResult.Success(response);
    }

    /// <summary>
    /// Spec §2.4 derives <c>next_action ∈ { null | request_revision | submit_acceptance | renew_now }</c>
    /// without specifying which to pick when both are technically allowed
    /// (state=<c>revised</c> with a published current version supports BOTH §2.6
    /// request-revision AND §2.7 submit-acceptance).
    ///
    /// Convention chosen here:
    /// <list type="bullet">
    ///   <item><c>requested</c> | <c>drafted</c> | <c>pending-approver</c> →
    ///         <c>null</c> (waiting on operator / approver — no buyer CTA).</item>
    ///   <item><c>revised</c> AND has published version → <c>submit_acceptance</c>
    ///         (acceptance is the happy-path primary CTA; revision is offered as a
    ///         secondary action via the §2.6 endpoint independent of next_action).</item>
    ///   <item><c>revised</c> WITHOUT published version → <c>request_revision</c>
    ///         (defensive — should not happen because the state machine only
    ///         transitions to <c>revised</c> on a publish, but covered for safety).</item>
    ///   <item><c>expired</c> → <c>renew_now</c> (UX seam for the Phase 1.5 renew
    ///         flow; storefront UI MAY route to a re-quote screen).</item>
    ///   <item>Other terminal states (<c>accepted</c> | <c>rejected</c> |
    ///         <c>withdrawn</c>) → <c>null</c> (history-only view).</item>
    /// </list>
    ///
    /// If product later wants both CTAs surfaced, the Cycle C-3 schema can extend
    /// the response with an array <c>available_actions</c> while keeping
    /// <c>next_action</c> as the primary-CTA hint.
    /// </summary>
    private static string? ComputeNextAction(Quote quote, bool hasCurrentVersion)
    {
        if (!QuoteStateExtensions.TryParseToken(quote.State, out var state))
        {
            // Defensive: an unknown state token shouldn't get here (DB CHECK
            // constraint enforces the enum). If it does, surface no CTA.
            return null;
        }
        return state switch
        {
            QuoteState.Revised when hasCurrentVersion => NextActionVocabulary.SubmitAcceptance,
            QuoteState.Revised => NextActionVocabulary.RequestRevision,
            QuoteState.Expired => NextActionVocabulary.RenewNow,
            _ => null,
        };
    }
}

/// <summary>
/// Internal handler envelope. The endpoint translates this into <see cref="Results.Ok"/>
/// or a 404 problem-details payload.
/// </summary>
public sealed record GetMyQuoteResult(bool Found, GetMyQuoteResponse? Response)
{
    public static GetMyQuoteResult Success(GetMyQuoteResponse r) => new(true, r);
    public static GetMyQuoteResult NotFound() => new(false, null);
}
