using BackendApi.Modules.Pricing.Primitives.Rounding;

namespace BackendApi.Modules.Pricing.Primitives.Layers;

/// <summary>
/// Layer 2: if the caller has a B2B tier and a tier price exists for (product, tier, market),
/// replace the line's net with `tierNet * qty`. Records the delta as a negative applied amount.
///
/// Tier price lookup is pre-resolved and passed in via <see cref="B2BTierLayerResolver"/> so
/// the layer stays pure (no DB call inside; determinism requires this).
/// </summary>
public sealed class B2BTierLayer
{
    private readonly IReadOnlyDictionary<Guid, long> _tierPricesByProductId;
    private readonly string? _tierSlug;

    public B2BTierLayer(IReadOnlyDictionary<Guid, long> tierPricesByProductId, string? tierSlug)
    {
        _tierPricesByProductId = tierPricesByProductId;
        _tierSlug = tierSlug;
    }

    public void Apply(PricingWorkingSet ws)
    {
        if (string.IsNullOrWhiteSpace(_tierSlug) || _tierPricesByProductId.Count == 0)
        {
            return;
        }

        foreach (var line in ws.Lines)
        {
            if (!_tierPricesByProductId.TryGetValue(line.ProductId, out var tierNetPerUnit))
            {
                continue;
            }

            var newNet = BankersRounding.RoundMinor((decimal)tierNetPerUnit * line.Qty);
            var delta = newNet - line.NetMinor;
            if (delta == 0)
            {
                continue;
            }

            line.NetMinor = newNet;
            line.Explanation.Add(new ExplanationRow(
                Layer: "tier",
                RuleId: $"tier:{_tierSlug}/p:{line.ProductId:N}",
                RuleKind: "b2b_tier",
                AppliedMinor: delta,
                ReasonCode: null));
        }
    }
}
