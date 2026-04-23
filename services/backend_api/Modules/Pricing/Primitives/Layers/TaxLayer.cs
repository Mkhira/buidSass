using BackendApi.Modules.Pricing.Primitives.Rounding;

namespace BackendApi.Modules.Pricing.Primitives.Layers;

/// <summary>
/// Layer 5: adds VAT on top of each line's post-discount net.
/// Tax rate is pre-resolved by the orchestrator (cache lookup + ctx.NowUtc effective window)
/// and attached to <see cref="PricingWorkingSet.TaxRate"/>.
/// Missing tax rate → throw; orchestrator maps to 500 pricing.tax_rate_missing.
/// </summary>
public sealed class TaxLayer
{
    public void Apply(PricingWorkingSet ws)
    {
        var rate = ws.TaxRate
            ?? throw new InvalidOperationException("pricing.tax_rate_missing");

        foreach (var line in ws.Lines)
        {
            var tax = BankersRounding.RoundMinor((decimal)line.NetMinor * rate.RateBps / 10_000m);
            line.TaxMinor = tax;

            line.Explanation.Add(new ExplanationRow(
                Layer: "tax",
                RuleId: $"{rate.MarketCode}/{rate.Kind}",
                RuleKind: rate.Kind,
                AppliedMinor: tax,
                ReasonCode: null));
        }
    }
}
