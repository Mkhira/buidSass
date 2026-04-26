using System.Globalization;
using System.Text;

namespace BackendApi.Modules.TaxInvoices.Primitives;

/// <summary>
/// FR-004 + research R4 — ZATCA Phase 1 (e-invoicing) QR code TLV encoder.
///
/// <para>
/// TLV layout (each field = 1-byte tag + 1-byte length + UTF-8 value bytes), concatenated and
/// base64-encoded. Phase 1 mandatory tags (in order):
/// </para>
/// <list type="bullet">
///   <item><description>1 — Seller name (Arabic, UTF-8)</description></item>
///   <item><description>2 — Seller VAT registration number</description></item>
///   <item><description>3 — Invoice timestamp (ISO 8601 UTC)</description></item>
///   <item><description>4 — Invoice total with VAT (string, decimal)</description></item>
///   <item><description>5 — VAT total (string, decimal)</description></item>
/// </list>
///
/// <para>
/// Length is a single unsigned byte; values longer than 255 bytes are rejected (Phase 1
/// invariant — seller names + VAT numbers fit comfortably). Phase 2 clearance API will use
/// the official ZATCA SDK; this hand-rolled encoder covers Phase 1 only.
/// </para>
/// </summary>
public static class ZatcaQrTlvBuilder
{
    public static string Build(
        string sellerName,
        string sellerVatNumber,
        DateTimeOffset invoiceTimestamp,
        long totalWithVatMinor,
        long vatTotalMinor,
        int currencyExponent = 2)
    {
        if (string.IsNullOrWhiteSpace(sellerName))
        {
            throw new ArgumentException("Seller name is required.", nameof(sellerName));
        }
        if (string.IsNullOrWhiteSpace(sellerVatNumber))
        {
            throw new ArgumentException("Seller VAT number is required.", nameof(sellerVatNumber));
        }

        // ZATCA expects ISO 8601 with seconds + timezone; we always emit UTC ('Z').
        var iso = invoiceTimestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var totalStr = FormatMinor(totalWithVatMinor, currencyExponent);
        var vatStr = FormatMinor(vatTotalMinor, currencyExponent);

        using var stream = new MemoryStream();
        WriteTlv(stream, tag: 1, sellerName);
        WriteTlv(stream, tag: 2, sellerVatNumber);
        WriteTlv(stream, tag: 3, iso);
        WriteTlv(stream, tag: 4, totalStr);
        WriteTlv(stream, tag: 5, vatStr);
        return Convert.ToBase64String(stream.ToArray());
    }

    private static void WriteTlv(MemoryStream stream, byte tag, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > 255)
        {
            throw new ArgumentException(
                $"Value for ZATCA TLV tag {tag} is {bytes.Length} bytes; Phase 1 only supports up to 255-byte values.",
                nameof(value));
        }
        stream.WriteByte(tag);
        stream.WriteByte((byte)bytes.Length);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static string FormatMinor(long minor, int currencyExponent)
    {
        if (currencyExponent < 0) throw new ArgumentOutOfRangeException(nameof(currencyExponent));
        if (currencyExponent == 0)
        {
            return minor.ToString(CultureInfo.InvariantCulture);
        }
        var scale = (long)Math.Pow(10, currencyExponent);
        var integerPart = minor / scale;
        var fractionalPart = Math.Abs(minor % scale);
        var sign = minor < 0 && integerPart == 0 ? "-" : string.Empty;
        return string.Create(CultureInfo.InvariantCulture,
            $"{sign}{integerPart}.{fractionalPart.ToString($"D{currencyExponent}", CultureInfo.InvariantCulture)}");
    }
}
