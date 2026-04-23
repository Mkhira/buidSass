namespace BackendApi.Modules.Pricing.Primitives.Layers;

/// <summary>
/// Layer 1: seed each working line with listMinor * qty as the starting net.
/// Records a "list" explanation row for auditability (Principle 10).
/// </summary>
public sealed class ListPriceLayer
{
    public void Apply(PricingWorkingSet ws)
    {
        foreach (var line in ws.Lines)
        {
            var listTotal = line.ListMinor * line.Qty;
            line.NetMinor = listTotal;
            line.Explanation.Add(new ExplanationRow(
                Layer: "list",
                RuleId: null,
                RuleKind: null,
                AppliedMinor: listTotal,
                ReasonCode: null));
        }
    }
}
