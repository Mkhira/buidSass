using BackendApi.Modules.TaxInvoices.Primitives;
using FluentAssertions;

namespace TaxInvoices.Tests.Unit;

/// <summary>
/// SC-002 — ZATCA Phase 1 QR TLV invariants. The full official-validator pass (1000 sampled
/// KSA invoices) is a deferred manual-pass step; here we cover the encoding contract.
/// </summary>
public sealed class ZatcaQrTlvBuilderTests
{
    [Fact]
    public void Build_EmitsAllFiveMandatoryTags_InOrder()
    {
        var b64 = ZatcaQrTlvBuilder.Build(
            sellerName: "منصة تجارة الأسنان المحدودة",
            sellerVatNumber: "300000000000003",
            invoiceTimestamp: new DateTimeOffset(2026, 4, 15, 10, 30, 0, TimeSpan.Zero),
            totalWithVatMinor: 115_00,
            vatTotalMinor: 15_00);

        var bytes = Convert.FromBase64String(b64);
        var (tag, _, _, idx) = ReadTlv(bytes, 0);
        tag.Should().Be(1);
        (tag, _, _, idx) = ReadTlv(bytes, idx);
        tag.Should().Be(2);
        (tag, _, _, idx) = ReadTlv(bytes, idx);
        tag.Should().Be(3);
        (tag, _, _, idx) = ReadTlv(bytes, idx);
        tag.Should().Be(4);
        (tag, _, _, idx) = ReadTlv(bytes, idx);
        tag.Should().Be(5);
        idx.Should().Be(bytes.Length);
    }

    [Fact]
    public void Build_TimestampIsIso8601Utc()
    {
        var b64 = ZatcaQrTlvBuilder.Build("Seller", "VAT", new DateTimeOffset(2026, 4, 15, 10, 30, 0, TimeSpan.Zero), 100, 15);
        var bytes = Convert.FromBase64String(b64);
        // Skip first two TLVs to reach tag 3.
        var (_, _, _, idx) = ReadTlv(bytes, 0);
        (_, _, _, idx) = ReadTlv(bytes, idx);
        var (tag, len, value, _) = ReadTlv(bytes, idx);
        tag.Should().Be(3);
        System.Text.Encoding.UTF8.GetString(value).Should().Be("2026-04-15T10:30:00Z");
        len.Should().Be((byte)value.Length);
    }

    [Fact]
    public void Build_AmountFormatting_DecimalWithCurrencyExponent2()
    {
        var b64 = ZatcaQrTlvBuilder.Build("S", "V", DateTimeOffset.UtcNow, 12345, 1500);
        var bytes = Convert.FromBase64String(b64);
        var (_, _, _, idx) = ReadTlv(bytes, 0);
        (_, _, _, idx) = ReadTlv(bytes, idx);
        (_, _, _, idx) = ReadTlv(bytes, idx);
        var (totalTag, _, totalValue, _) = ReadTlv(bytes, idx);
        totalTag.Should().Be(4);
        System.Text.Encoding.UTF8.GetString(totalValue).Should().Be("123.45");
    }

    [Fact]
    public void Build_RejectsValuesOver255Bytes()
    {
        var huge = new string('s', 300);
        var act = () => ZatcaQrTlvBuilder.Build(huge, "VAT", DateTimeOffset.UtcNow, 100, 15);
        act.Should().Throw<ArgumentException>().WithMessage("*255-byte*");
    }

    [Theory]
    [InlineData(null, "VAT")]
    [InlineData("", "VAT")]
    [InlineData("Seller", null)]
    [InlineData("Seller", "")]
    public void Build_RequiresSellerAndVat(string? seller, string? vat)
    {
        var act = () => ZatcaQrTlvBuilder.Build(seller!, vat!, DateTimeOffset.UtcNow, 100, 15);
        act.Should().Throw<ArgumentException>();
    }

    private static (byte Tag, byte Length, byte[] Value, int NextIndex) ReadTlv(byte[] bytes, int idx)
    {
        var tag = bytes[idx];
        var len = bytes[idx + 1];
        var value = bytes.AsSpan(idx + 2, len).ToArray();
        return (tag, len, value, idx + 2 + len);
    }
}
