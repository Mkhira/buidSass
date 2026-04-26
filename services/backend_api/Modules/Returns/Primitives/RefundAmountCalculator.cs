namespace BackendApi.Modules.Returns.Primitives;

/// <summary>
/// FR-014, SC-003, SC-009 — pro-rata refund math. Both discount AND tax are pro-rated by qty
/// ratio against the *original captured amounts* (not recomputed from a rate). This matches
/// spec 012's <c>IssueCreditNoteHandler</c> exactly (`LineDiscountMinor * input.Qty / origin.Qty`
/// + same for tax), so SC-009 reconciliation `refund_amount == |credit_note.grand_total|`
/// holds to 0 minor units. Recomputing tax from rate would round-drift by up to 1 minor unit
/// per line (deep-review pass 1 finding).
/// </summary>
public sealed class RefundAmountCalculator
{
    public RefundComputation Compute(IReadOnlyList<RefundLineInput> lines, long restockingFeeMinor)
    {
        if (lines is null) throw new ArgumentNullException(nameof(lines));
        if (restockingFeeMinor < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(restockingFeeMinor), "Restocking fee must be non-negative.");
        }

        long totalSubtotal = 0;
        long totalDiscount = 0;
        long totalTax = 0;
        long totalLineAmount = 0;
        var resultLines = new List<RefundLineComputation>(lines.Count);

        foreach (var line in lines)
        {
            if (line.QtyToRefund <= 0)
            {
                throw new ArgumentException($"Line {line.OrderLineId} qty must be positive.", nameof(lines));
            }
            if (line.OriginalQty <= 0)
            {
                throw new ArgumentException($"Line {line.OrderLineId} original qty must be positive.", nameof(lines));
            }
            if (line.QtyToRefund > line.OriginalQty)
            {
                throw new ArgumentException(
                    $"Line {line.OrderLineId}: qtyToRefund {line.QtyToRefund} exceeds originalQty {line.OriginalQty}.",
                    nameof(lines));
            }
            if (line.UnitPriceMinor < 0 || line.OriginalDiscountMinor < 0
                || line.OriginalTaxMinor < 0
                || line.TaxRateBp < 0 || line.TaxRateBp > 10_000)
            {
                throw new ArgumentException(
                    $"Line {line.OrderLineId}: numeric inputs out of range.", nameof(lines));
            }

            // Pro-rate BOTH discount and tax by qty ratio against the captured originals
            // (deep-review pass 1 fix). Floor (truncate) — final-credit reclamation lives in
            // spec 012's IssueCreditNoteHandler so the credit note can pick up any leftover.
            var lineSubtotal = line.UnitPriceMinor * line.QtyToRefund;
            var lineDiscount = line.OriginalDiscountMinor * line.QtyToRefund / line.OriginalQty;
            var lineTax = line.OriginalTaxMinor * line.QtyToRefund / line.OriginalQty;
            var lineAmount = lineSubtotal - lineDiscount + lineTax;

            resultLines.Add(new RefundLineComputation(
                OrderLineId: line.OrderLineId,
                ReturnLineId: line.ReturnLineId,
                Qty: line.QtyToRefund,
                UnitPriceMinor: line.UnitPriceMinor,
                LineSubtotalMinor: lineSubtotal,
                LineDiscountMinor: lineDiscount,
                LineTaxMinor: lineTax,
                LineAmountMinor: lineAmount,
                TaxRateBp: line.TaxRateBp));

            totalSubtotal += lineSubtotal;
            totalDiscount += lineDiscount;
            totalTax += lineTax;
            totalLineAmount += lineAmount;
        }

        // Restocking fee reduces the refund (positive fee → smaller refund). Guard against the
        // fee exceeding the refundable total — caller should treat this as a 400.
        var refundBeforeFee = totalLineAmount;
        if (restockingFeeMinor > refundBeforeFee)
        {
            throw new InvalidOperationException(
                $"Restocking fee {restockingFeeMinor} exceeds refundable total {refundBeforeFee}.");
        }
        var refundAmount = refundBeforeFee - restockingFeeMinor;

        return new RefundComputation(
            Lines: resultLines,
            SubtotalMinor: totalSubtotal,
            DiscountMinor: totalDiscount,
            TaxMinor: totalTax,
            RestockingFeeMinor: restockingFeeMinor,
            GrandRefundMinor: refundAmount);
    }

    /// <summary>Banker's rounding of <c>numerator * factor / divisor</c> on signed longs.</summary>
    private static long MultiplyDivBankers(long numerator, long factor, long divisor)
    {
        if (divisor <= 0) throw new ArgumentOutOfRangeException(nameof(divisor));
        var product = numerator * factor;
        var quotient = product / divisor;
        var remainder = product - quotient * divisor;
        if (remainder == 0) return quotient;
        // Round half-to-even.
        var twiceRem = remainder * 2;
        if (Math.Abs(twiceRem) > divisor) return quotient + Math.Sign(remainder);
        if (Math.Abs(twiceRem) < divisor) return quotient;
        // Exactly half — round to even.
        return (quotient & 1L) == 0L ? quotient : quotient + Math.Sign(remainder);
    }
}

public sealed record RefundLineInput(
    Guid ReturnLineId,
    Guid OrderLineId,
    int OriginalQty,
    int QtyToRefund,
    long UnitPriceMinor,
    long OriginalDiscountMinor,
    /// <summary>The full <c>line_tax_minor</c> that was actually captured on the original
    /// order (NOT recomputed from a rate). Pro-rated by qty so refund + credit-note totals
    /// reconcile exactly.</summary>
    long OriginalTaxMinor,
    int TaxRateBp);

public sealed record RefundLineComputation(
    Guid OrderLineId,
    Guid ReturnLineId,
    int Qty,
    long UnitPriceMinor,
    long LineSubtotalMinor,
    long LineDiscountMinor,
    long LineTaxMinor,
    long LineAmountMinor,
    int TaxRateBp);

public sealed record RefundComputation(
    IReadOnlyList<RefundLineComputation> Lines,
    long SubtotalMinor,
    long DiscountMinor,
    long TaxMinor,
    long RestockingFeeMinor,
    long GrandRefundMinor);
