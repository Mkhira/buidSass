using BackendApi.Modules.Pricing.Primitives.Rounding;

namespace BackendApi.Modules.Pricing.Primitives.Layers;

/// <summary>
/// Layer 3: applies scheduled promotions ordered by priority (lower = earlier).
/// Kinds: percent_off, amount_off, bogo, bundle_wrapper.
/// BOGO: qualifying_qty units required, reward_qty units discounted at reward_percent.
/// Each qualifying match records an explanation row on the affected line.
/// </summary>
public sealed class PromotionLayer
{
    private readonly IReadOnlyList<PromotionSnapshot> _promotions;

    public PromotionLayer(IReadOnlyList<PromotionSnapshot> promotions)
    {
        _promotions = [.. promotions.OrderBy(p => p.Priority).ThenBy(p => p.Id)];
    }

    public void Apply(PricingWorkingSet ws)
    {
        if (_promotions.Count == 0)
        {
            return;
        }

        foreach (var promo in _promotions)
        {
            if (!IsActive(promo, ws.Context.NowUtc))
            {
                continue;
            }

            if (!promo.MarketCodes.Any(m => string.Equals(m, ws.Context.MarketCode, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            switch (promo.Kind)
            {
                case "percent_off":
                    ApplyPercentOff(ws, promo);
                    break;
                case "amount_off":
                    ApplyAmountOff(ws, promo);
                    break;
                case "bogo":
                    ApplyBogo(ws, promo);
                    break;
                case "bundle_wrapper":
                    // Bundles ship as SKUs in spec 005; pricing engine treats them as normal lines.
                    // This kind exists for admin authoring; no runtime math.
                    break;
            }
        }
    }

    private static bool IsActive(PromotionSnapshot promo, DateTimeOffset nowUtc)
    {
        if (!promo.IsActive)
        {
            return false;
        }
        if (promo.StartsAt is not null && nowUtc < promo.StartsAt)
        {
            return false;
        }
        if (promo.EndsAt is not null && nowUtc >= promo.EndsAt)
        {
            return false;
        }
        return true;
    }

    private static void ApplyPercentOff(PricingWorkingSet ws, PromotionSnapshot promo)
    {
        var pctBps = promo.PercentBps ?? 0;
        if (pctBps <= 0)
        {
            return;
        }

        foreach (var line in ws.Lines)
        {
            if (!Qualifies(promo, line))
            {
                continue;
            }

            var discount = BankersRounding.RoundMinor((decimal)line.NetMinor * pctBps / 10_000m);
            if (discount <= 0)
            {
                continue;
            }

            line.NetMinor -= discount;
            line.Explanation.Add(new ExplanationRow(
                Layer: "promotion",
                RuleId: $"promo:{promo.Id:N}",
                RuleKind: "percent_off",
                AppliedMinor: -discount,
                ReasonCode: null));
        }
    }

    private static void ApplyAmountOff(PricingWorkingSet ws, PromotionSnapshot promo)
    {
        var amountPerUnit = promo.AmountMinor ?? 0;
        if (amountPerUnit <= 0)
        {
            return;
        }

        foreach (var line in ws.Lines)
        {
            if (!Qualifies(promo, line))
            {
                continue;
            }

            var discount = Math.Min(amountPerUnit * line.Qty, line.NetMinor);
            if (discount <= 0)
            {
                continue;
            }

            line.NetMinor -= discount;
            line.Explanation.Add(new ExplanationRow(
                Layer: "promotion",
                RuleId: $"promo:{promo.Id:N}",
                RuleKind: "amount_off",
                AppliedMinor: -discount,
                ReasonCode: null));
        }
    }

    private static void ApplyBogo(PricingWorkingSet ws, PromotionSnapshot promo)
    {
        var qualifyQty = promo.BogoQualifyQty ?? 0;
        var rewardQty = promo.BogoRewardQty ?? 0;
        var rewardPctBps = promo.BogoRewardPercentBps ?? 10_000;

        if (qualifyQty <= 0 || rewardQty <= 0)
        {
            return;
        }

        // Qualifies only if the line's productId is listed as qualifying.
        var qualifyingSku = promo.BogoQualifyingProductId;
        var rewardSku = promo.BogoRewardProductId ?? qualifyingSku;

        foreach (var line in ws.Lines.OrderBy(l => l.ProductId))
        {
            if (qualifyingSku is null || line.ProductId != qualifyingSku.Value)
            {
                continue;
            }

            var groupsOfQualify = line.Qty / (qualifyQty + rewardQty);
            var freeUnits = groupsOfQualify * rewardQty;
            if (freeUnits <= 0)
            {
                continue;
            }

            var perUnitNet = line.Qty == 0 ? 0 : line.NetMinor / line.Qty;
            var discount = BankersRounding.RoundMinor((decimal)perUnitNet * freeUnits * rewardPctBps / 10_000m);
            discount = Math.Min(discount, line.NetMinor);
            if (discount <= 0)
            {
                continue;
            }

            line.NetMinor -= discount;

            _ = rewardSku; // reward-sku variant reserved; at launch reward = qualifying.
            line.Explanation.Add(new ExplanationRow(
                Layer: "promotion",
                RuleId: $"promo:{promo.Id:N}",
                RuleKind: "bogo",
                AppliedMinor: -discount,
                ReasonCode: null));
        }
    }

    private static bool Qualifies(PromotionSnapshot promo, WorkingLine line)
    {
        if (promo.AppliesToProductIds is { Count: > 0 } products
            && !products.Contains(line.ProductId))
        {
            return false;
        }
        if (promo.AppliesToCategoryIds is { Count: > 0 } categories
            && !line.CategoryIds.Any(categories.Contains))
        {
            return false;
        }
        return true;
    }
}

public sealed record PromotionSnapshot(
    Guid Id,
    string Kind,
    int Priority,
    bool IsActive,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    IReadOnlyList<string> MarketCodes,
    IReadOnlyList<Guid>? AppliesToProductIds,
    IReadOnlyList<Guid>? AppliesToCategoryIds,
    int? PercentBps,
    long? AmountMinor,
    Guid? BogoQualifyingProductId,
    Guid? BogoRewardProductId,
    int? BogoQualifyQty,
    int? BogoRewardQty,
    int? BogoRewardPercentBps);
