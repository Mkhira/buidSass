namespace BackendApi.Modules.Cart.Primitives;

/// <summary>
/// Deterministic anon → auth cart merge (R2). Sums line qty subject to per-line
/// max_per_order cap. Returns a per-line merge notice whenever the sum was capped,
/// surfaced to the caller as `cart.merge.qty_capped`. The authenticated cart's B2B
/// metadata and coupon win over the anonymous cart.
///
/// Pure function — no DB, no I/O — so the 100-scenario SC-003 determinism test is
/// observable entirely in memory.
/// </summary>
public sealed class CartMerger
{
    public sealed record MergeLine(Guid ProductId, int Qty, int MaxPerOrder);

    public sealed record MergeNotice(Guid ProductId, string ReasonCode, int RequestedQty, int AppliedQty);

    public sealed record MergeResult(
        IReadOnlyList<MergeLine> Lines,
        IReadOnlyList<MergeNotice> Notices);

    public MergeResult Merge(IReadOnlyList<MergeLine> anonLines, IReadOnlyList<MergeLine> authLines)
    {
        var byProduct = new Dictionary<Guid, (int sumQty, int maxPerOrder)>(authLines.Count + anonLines.Count);

        foreach (var l in authLines)
        {
            byProduct[l.ProductId] = (l.Qty, l.MaxPerOrder);
        }
        foreach (var l in anonLines)
        {
            if (byProduct.TryGetValue(l.ProductId, out var existing))
            {
                byProduct[l.ProductId] = (existing.sumQty + l.Qty, Math.Max(existing.maxPerOrder, l.MaxPerOrder));
            }
            else
            {
                byProduct[l.ProductId] = (l.Qty, l.MaxPerOrder);
            }
        }

        var merged = new List<MergeLine>(byProduct.Count);
        var notices = new List<MergeNotice>();

        // Deterministic output order — sort by productId so the 100-scenario SC test has a stable
        // comparison surface regardless of insert order in the dictionaries.
        foreach (var kv in byProduct.OrderBy(x => x.Key))
        {
            var (sumQty, maxPerOrder) = kv.Value;
            var cappedQty = maxPerOrder > 0 ? Math.Min(sumQty, maxPerOrder) : sumQty;
            if (cappedQty != sumQty)
            {
                notices.Add(new MergeNotice(kv.Key, "cart.merge.qty_capped", sumQty, cappedQty));
            }
            merged.Add(new MergeLine(kv.Key, cappedQty, maxPerOrder));
        }

        return new MergeResult(merged, notices);
    }
}
