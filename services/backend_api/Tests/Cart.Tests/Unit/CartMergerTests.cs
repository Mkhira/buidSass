using BackendApi.Modules.Cart.Primitives;
using FluentAssertions;

namespace Cart.Tests.Unit;

/// <summary>
/// SC-003: merge determinism across 100 randomized anon/auth pairings — same inputs must
/// always produce the same line set, qty, and notice list regardless of dictionary insert
/// order. Qty sums; per-line max_per_order caps; capped lines emit `cart.merge.qty_capped`.
/// </summary>
public sealed class CartMergerTests
{
    private static readonly CartMerger Merger = new();

    private static Guid ProductId(int i) => new Guid($"00000000-0000-0000-0000-{i:D12}");

    [Fact]
    public void Merge_EmptyInputs_ReturnsEmpty()
    {
        var result = Merger.Merge([], []);

        result.Lines.Should().BeEmpty();
        result.Notices.Should().BeEmpty();
    }

    [Fact]
    public void Merge_AuthOnly_ReturnsAuthUntouched()
    {
        var p = ProductId(1);
        var result = Merger.Merge([], [new CartMerger.MergeLine(p, 2, 10)]);

        result.Lines.Should().ContainSingle().Which.Qty.Should().Be(2);
        result.Notices.Should().BeEmpty();
    }

    [Fact]
    public void Merge_AnonOnly_ReturnsAnonLines()
    {
        var p = ProductId(2);
        var result = Merger.Merge([new CartMerger.MergeLine(p, 3, 10)], []);

        result.Lines.Should().ContainSingle().Which.Qty.Should().Be(3);
        result.Notices.Should().BeEmpty();
    }

    [Fact]
    public void Merge_OverlapProduct_SumsQty()
    {
        var p = ProductId(3);
        var result = Merger.Merge(
            [new CartMerger.MergeLine(p, 2, 10)],
            [new CartMerger.MergeLine(p, 3, 10)]);

        result.Lines.Should().ContainSingle().Which.Qty.Should().Be(5);
        result.Notices.Should().BeEmpty();
    }

    [Fact]
    public void Merge_SumExceedsMax_CapsAtMaxAndEmitsNotice()
    {
        var p = ProductId(4);
        var result = Merger.Merge(
            [new CartMerger.MergeLine(p, 6, 8)],
            [new CartMerger.MergeLine(p, 5, 8)]);

        result.Lines.Should().ContainSingle().Which.Qty.Should().Be(8);
        result.Notices.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new CartMerger.MergeNotice(p, "cart.merge.qty_capped", 11, 8));
    }

    [Fact]
    public void Merge_MaxZero_NotTreatedAsCap()
    {
        // max_per_order=0 means no cap (catalog signal: unbounded).
        var p = ProductId(5);
        var result = Merger.Merge(
            [new CartMerger.MergeLine(p, 50, 0)],
            [new CartMerger.MergeLine(p, 75, 0)]);

        result.Lines.Should().ContainSingle().Which.Qty.Should().Be(125);
        result.Notices.Should().BeEmpty();
    }

    [Fact]
    public void Merge_DisjointProducts_UnionsWithoutNotices()
    {
        var a = ProductId(6);
        var b = ProductId(7);
        var result = Merger.Merge(
            [new CartMerger.MergeLine(a, 1, 5)],
            [new CartMerger.MergeLine(b, 2, 5)]);

        result.Lines.Should().HaveCount(2);
        result.Lines.Should().Contain(l => l.ProductId == a && l.Qty == 1);
        result.Lines.Should().Contain(l => l.ProductId == b && l.Qty == 2);
        result.Notices.Should().BeEmpty();
    }

    [Fact]
    public void Merge_OutputSortedByProductId_IsDeterministic()
    {
        // Insert anon/auth in reverse ID order — result must still be sorted ascending.
        var p1 = ProductId(100);
        var p2 = ProductId(200);
        var p3 = ProductId(300);

        var result = Merger.Merge(
            [new CartMerger.MergeLine(p3, 1, 5), new CartMerger.MergeLine(p1, 1, 5)],
            [new CartMerger.MergeLine(p2, 1, 5)]);

        result.Lines.Select(l => l.ProductId).Should().ContainInOrder(p1, p2, p3);
    }

    [Fact]
    public void Merge_MaxPerOrder_UsesMaxOfAnonAndAuth()
    {
        // If anon and auth disagree on max_per_order (e.g. catalog was updated between sessions),
        // use the higher of the two so the merge doesn't manufacture a cap that neither side saw.
        var p = ProductId(8);
        var result = Merger.Merge(
            [new CartMerger.MergeLine(p, 5, 10)],
            [new CartMerger.MergeLine(p, 5, 4)]);

        result.Lines.Single().Qty.Should().Be(10); // sum=10, max=max(10,4)=10 -> no cap
        result.Notices.Should().BeEmpty();
    }

    [Fact]
    public void Merge_100Scenarios_DeterministicAndCorrect()
    {
        // SC-003: run 100 randomized (but seeded) scenarios; each scenario runs twice and
        // the two runs must produce identical output. Invariants checked per scenario:
        //   - per-product qty ≤ maxPerOrder (when maxPerOrder > 0)
        //   - cart.merge.qty_capped emitted iff sum exceeded max
        //   - Lines ordered by ProductId ascending
        var rng = new Random(20260424);
        for (var scenario = 0; scenario < 100; scenario++)
        {
            var productCount = rng.Next(1, 8);
            var anon = new List<CartMerger.MergeLine>();
            var auth = new List<CartMerger.MergeLine>();
            var caps = new Dictionary<Guid, int>();

            for (var i = 0; i < productCount; i++)
            {
                var pid = ProductId(1_000 + scenario * 10 + i);
                var max = rng.Next(0, 12); // 0 means uncapped
                caps[pid] = max;
                var inAnon = rng.Next(0, 2) == 1;
                var inAuth = rng.Next(0, 2) == 1;
                if (!inAnon && !inAuth) inAnon = true; // guarantee one side
                if (inAnon) anon.Add(new CartMerger.MergeLine(pid, rng.Next(1, 10), max));
                if (inAuth) auth.Add(new CartMerger.MergeLine(pid, rng.Next(1, 10), max));
            }

            var first = Merger.Merge(anon, auth);

            // Determinism: reverse both input lists — output must match.
            var second = Merger.Merge([.. anon.AsEnumerable().Reverse()], [.. auth.AsEnumerable().Reverse()]);
            second.Lines.Should().BeEquivalentTo(first.Lines, options => options.WithStrictOrdering(),
                because: $"scenario {scenario} must be deterministic regardless of insert order");
            second.Notices.Should().BeEquivalentTo(first.Notices);

            // Sorted by productId.
            first.Lines.Select(l => l.ProductId).Should().BeInAscendingOrder();

            // Per-line invariants.
            foreach (var line in first.Lines)
            {
                var max = caps[line.ProductId];
                if (max > 0)
                {
                    line.Qty.Should().BeLessThanOrEqualTo(max);
                }
                var requestedAnon = anon.Where(l => l.ProductId == line.ProductId).Sum(l => l.Qty);
                var requestedAuth = auth.Where(l => l.ProductId == line.ProductId).Sum(l => l.Qty);
                var requested = requestedAnon + requestedAuth;
                var hasNotice = first.Notices.Any(n => n.ProductId == line.ProductId);
                var wasCapped = max > 0 && requested > max;
                hasNotice.Should().Be(wasCapped,
                    because: $"scenario {scenario}, product {line.ProductId}: notice iff capped");
                if (hasNotice)
                {
                    var notice = first.Notices.Single(n => n.ProductId == line.ProductId);
                    notice.ReasonCode.Should().Be("cart.merge.qty_capped");
                    notice.RequestedQty.Should().Be(requested);
                    notice.AppliedQty.Should().Be(line.Qty);
                }
            }
        }
    }
}
