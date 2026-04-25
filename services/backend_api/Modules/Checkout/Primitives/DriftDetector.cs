using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BackendApi.Modules.Checkout.Primitives;

/// <summary>
/// Compares the hash of a last-Preview explanation against a fresh Issue-mode explanation.
/// When they differ, the session MUST surface a `pricing_drift` diff and require the
/// customer to accept the new total before re-submit (FR-013 / R4 / SC-004).
///
/// The hash covers the fields that would change the customer's decision:
/// grand total, subtotal, discount, tax, per-line gross. Coupon code is included so
/// a coupon expiry between Preview and Issue counts as drift. We deliberately
/// exclude timestamps + explanation ids so identical economic outputs with
/// different run metadata don't falsely trip.
/// </summary>
public sealed class DriftDetector
{
    public byte[] Hash(PricingSnapshot snapshot)
    {
        // Canonical JSON — sorted keys, no whitespace — so the hash is stable across C#
        // runtimes + framework versions.
        var canonical = JsonSerializer.Serialize(new
        {
            grand = snapshot.GrandTotalMinor,
            sub = snapshot.SubtotalMinor,
            disc = snapshot.DiscountMinor,
            tax = snapshot.TaxMinor,
            cur = snapshot.Currency,
            coupon = snapshot.CouponCode ?? "",
            lines = snapshot.Lines
                .OrderBy(l => l.ProductId)
                .Select(l => new { p = l.ProductId, q = l.Qty, g = l.GrossMinor, n = l.NetMinor })
                .ToArray(),
        }, new JsonSerializerOptions { WriteIndented = false });
        return SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
    }

    public bool HasDrifted(byte[]? previousHash, byte[] currentHash)
    {
        if (previousHash is null || previousHash.Length == 0) return false;
        return !previousHash.AsSpan().SequenceEqual(currentHash);
    }

    public sealed record PricingSnapshot(
        long SubtotalMinor,
        long DiscountMinor,
        long TaxMinor,
        long GrandTotalMinor,
        string Currency,
        string? CouponCode,
        IReadOnlyList<LineSnapshot> Lines);

    public sealed record LineSnapshot(Guid ProductId, int Qty, long NetMinor, long GrossMinor);
}
