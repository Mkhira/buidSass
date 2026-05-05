using System.Text.Json;
using BackendApi.Modules.B2B.Persistence;
using BackendApi.Modules.Shared;
using BackendApi.Modules.Verification.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.B2B.Quotes.Admin.GetQuoteDetail;

/// <summary>
/// Spec 021 T086 — admin quote detail (contract §4.2). Returns the full Quote +
/// every QuoteVersion + every transition + advisory <c>verification_warnings</c>
/// and <c>archived_sku_lines</c> blocks (re-evaluated each call, NOT snapshotted).
/// </summary>
public sealed class GetQuoteDetailHandler
{
    private readonly B2BDbContext _db;
    private readonly ICustomerVerificationEligibilityQuery _eligibility;
    private readonly IProductCatalogQuery _catalog;

    public GetQuoteDetailHandler(
        B2BDbContext db,
        ICustomerVerificationEligibilityQuery eligibility,
        IProductCatalogQuery catalog)
    {
        _db = db;
        _eligibility = eligibility;
        _catalog = catalog;
    }

    public async Task<GetQuoteDetailResponse?> HandleAsync(Guid quoteId, CancellationToken ct)
    {
        var quote = await _db.Quotes
            .AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == quoteId, ct);
        if (quote is null) return null;

        var versions = await _db.QuoteVersions
            .AsNoTracking()
            .Where(v => v.QuoteId == quoteId)
            .OrderBy(v => v.VersionNumber)
            .ToListAsync(ct);

        var transitions = await _db.QuoteStateTransitions
            .AsNoTracking()
            .Where(t => t.QuoteId == quoteId)
            .OrderBy(t => t.OccurredAt)
            .Select(t => new GetQuoteDetailTransition(
                t.PriorState, t.NewState, t.ActorKind, t.ActorId, t.OccurredAt))
            .ToListAsync(ct);

        var skus = ResolveLineSkus(quote.OriginatingCartSnapshotJson, versions);

        var verificationWarnings = new List<GetQuoteDetailWarning>();
        var archivedSkus = new List<string>();

        if (skus.Count > 0)
        {
            var eligibility = await _eligibility.EvaluateManyAsync(
                quote.CustomerId, quote.MarketCode, skus, ct);
            foreach (var sku in skus)
            {
                if (eligibility.TryGetValue(sku, out var r) && r.Class == EligibilityClass.Ineligible)
                {
                    verificationWarnings.Add(new GetQuoteDetailWarning(
                        Sku: sku,
                        ReasonCode: r.ReasonCode.ToString(),
                        MessageKey: r.MessageKey));
                }
                if (!await _catalog.IsActiveAsync(sku, ct))
                {
                    archivedSkus.Add(sku);
                }
            }
        }

        return new GetQuoteDetailResponse(
            Id: quote.Id,
            State: quote.State,
            MarketCode: quote.MarketCode,
            CustomerId: quote.CustomerId,
            CompanyId: quote.CompanyId,
            BranchId: quote.BranchId,
            PoNumber: quote.PoNumber,
            RequestedAt: quote.RequestedAt,
            ExpiresAt: quote.ExpiresAt,
            DecidedAt: quote.DecidedAt,
            TerminalAt: quote.TerminalAt,
            CurrentVersionId: quote.CurrentVersionId,
            InvoiceBilling: quote.InvoiceBilling,
            CustomerLocale: ResolveCustomerLocale(quote.CustomerSuppliedMessageJson),
            RestrictionPolicySnapshot: quote.RestrictionPolicySnapshotJson,
            SchemaVersion: quote.SchemaVersion,
            DraftBody: quote.DraftBodyJson,
            Versions: versions.Select(v => new GetQuoteDetailVersion(
                Id: v.Id,
                VersionNumber: v.VersionNumber,
                AuthoredBy: v.AuthoredBy,
                PublishedAt: v.PublishedAt,
                LineItems: v.LineItemsJson,
                TermsText: v.TermsTextJson,
                TermsDays: v.TermsDays,
                ValidityExtends: v.ValidityExtends,
                TotalsSummary: v.TotalsSummaryJson,
                CustomerRevisionComment: v.CustomerRevisionCommentJson)).ToList(),
            Transitions: transitions,
            VerificationWarnings: verificationWarnings,
            ArchivedSkuLines: archivedSkus);
    }

    private static IReadOnlyList<string> ResolveLineSkus(
        string? originatingCartSnapshot,
        IReadOnlyList<Entities.QuoteVersion> versions)
    {
        // Prefer the latest published version's lines; fall back to originating cart.
        var latest = versions.LastOrDefault();
        if (latest is not null)
        {
            var v = ExtractSkus(latest.LineItemsJson);
            if (v.Count > 0) return v;
        }
        return ExtractSkus(originatingCartSnapshot);
    }

    private static IReadOnlyList<string> ExtractSkus(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
            var skus = new List<string>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (item.TryGetProperty("sku", out var s) && s.ValueKind == JsonValueKind.String)
                {
                    var v = s.GetString();
                    if (!string.IsNullOrWhiteSpace(v)) skus.Add(v);
                }
            }
            return skus;
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static string ResolveCustomerLocale(string? customerMessageJson)
    {
        if (string.IsNullOrWhiteSpace(customerMessageJson)) return "en";
        try
        {
            using var doc = JsonDocument.Parse(customerMessageJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("ar", out var ar)
                && ar.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(ar.GetString()))
            {
                return "ar";
            }
        }
        catch (JsonException) { }
        return "en";
    }
}
