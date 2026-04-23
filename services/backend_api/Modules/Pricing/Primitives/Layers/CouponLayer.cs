using BackendApi.Modules.Pricing.Primitives.Rounding;

namespace BackendApi.Modules.Pricing.Primitives.Layers;

/// <summary>
/// Layer 4: applies at most one coupon.
/// Kind=percent → value is bps, optional cap_minor caps the total discount.
/// Kind=amount  → value is minor units off the cart subtotal.
///
/// Distribution across lines: pro-rata by current line net, banker's rounding per line.
/// If excludes_restricted, restricted lines are skipped.
/// The resolved coupon (or failure reason) is pre-computed by the caller and attached to the working set.
/// </summary>
public sealed class CouponLayer
{
    public void Apply(PricingWorkingSet ws)
    {
        var coupon = ws.AppliedCoupon;
        if (coupon is null)
        {
            return;
        }

        var eligibleLines = ws.Lines
            .Where(l => !coupon.ExcludesRestricted || !l.Restricted)
            .Where(l => l.NetMinor > 0)
            .ToArray();

        if (eligibleLines.Length == 0)
        {
            return;
        }

        var eligibleSubtotal = eligibleLines.Sum(l => l.NetMinor);
        if (eligibleSubtotal <= 0)
        {
            return;
        }

        long totalDiscount;
        if (coupon.Kind == "percent")
        {
            var raw = BankersRounding.RoundMinor((decimal)eligibleSubtotal * coupon.Value / 10_000m);
            totalDiscount = coupon.CapMinor is long cap ? Math.Min(raw, cap) : raw;
        }
        else // amount
        {
            totalDiscount = Math.Min(coupon.Value, eligibleSubtotal);
        }

        if (totalDiscount <= 0)
        {
            return;
        }

        // Pro-rata distribution with half-even rounding; residual goes to the last eligible line.
        var remaining = totalDiscount;
        for (var i = 0; i < eligibleLines.Length; i++)
        {
            var line = eligibleLines[i];
            long lineDiscount;
            if (i == eligibleLines.Length - 1)
            {
                lineDiscount = remaining;
            }
            else
            {
                lineDiscount = BankersRounding.RoundMinor(
                    (decimal)totalDiscount * line.NetMinor / eligibleSubtotal);
                lineDiscount = Math.Min(lineDiscount, line.NetMinor);
            }

            lineDiscount = Math.Min(lineDiscount, line.NetMinor);
            if (lineDiscount <= 0)
            {
                continue;
            }

            line.NetMinor -= lineDiscount;
            remaining -= lineDiscount;

            line.Explanation.Add(new ExplanationRow(
                Layer: "coupon",
                RuleId: $"coupon:{coupon.CouponId:N}",
                RuleKind: coupon.Kind,
                AppliedMinor: -lineDiscount,
                ReasonCode: null));
        }
    }
}
