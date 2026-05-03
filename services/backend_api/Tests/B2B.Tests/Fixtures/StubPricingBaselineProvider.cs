using BackendApi.Modules.Shared;

namespace B2B.Tests.Fixtures;

/// <summary>
/// Spec 021 T053 — test-only stub for <see cref="IPricingBaselineProvider"/>. Returns
/// a deterministic baseline per SKU so admin-authoring tests can verify the
/// authored-line shape without a real Pricing module wired up.
///
/// Defaults: SAR currency, baseline 100.00 minor-unit-aligned, 15% tax preview, no
/// promotions. Tests can override per-SKU via <see cref="OverridesBySku"/>.
/// </summary>
public sealed class StubPricingBaselineProvider : IPricingBaselineProvider
{
    public Dictionary<string, PricingBaseline> OverridesBySku { get; } = new(StringComparer.Ordinal);

    public string DefaultCurrency { get; set; } = "SAR";
    public decimal DefaultBaseline { get; set; } = 100.00m;
    public decimal DefaultTaxRatePct { get; set; } = 15.00m;

    public ValueTask<IReadOnlyDictionary<string, PricingBaseline>> GetBaselinesAsync(
        Guid customerId,
        IReadOnlyCollection<string> skus,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, PricingBaseline>(StringComparer.Ordinal);
        foreach (var sku in skus)
        {
            if (OverridesBySku.TryGetValue(sku, out var custom))
            {
                result[sku] = custom;
                continue;
            }

            var taxAmount = decimal.Round(DefaultBaseline * (DefaultTaxRatePct / 100m), 2);
            result[sku] = new PricingBaseline(
                Sku: sku,
                BaselineUnitPrice: DefaultBaseline,
                Currency: DefaultCurrency,
                Promotions: Array.Empty<AppliedPromotion>(),
                TaxPreviewUnitAmount: taxAmount,
                TaxPreviewRatePct: DefaultTaxRatePct);
        }
        return ValueTask.FromResult<IReadOnlyDictionary<string, PricingBaseline>>(result);
    }
}
