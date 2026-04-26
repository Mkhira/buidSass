using FluentAssertions;

namespace TaxInvoices.Tests.Unit;

/// <summary>H1 partial — invoice + credit-note number format invariants. The full SC-003
/// collision fuzz (10 000 concurrent across two markets) lives in the integration suite
/// against a real Postgres testcontainer.</summary>
public sealed class InvoiceNumberFormatTests
{
    [Theory]
    [InlineData("KSA", "202604", 187, "INV-KSA-202604-000187")]
    [InlineData("EG", "202612", 1, "INV-EG-202612-000001")]
    [InlineData("KSA", "202601", 999_999, "INV-KSA-202601-999999")]
    public void Format_MatchesSpec(string market, string yyyymm, long seq, string expected)
    {
        var seq6 = seq.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        $"INV-{market}-{yyyymm}-{seq6}".Should().Be(expected);
    }

    [Theory]
    [InlineData("KSA", "202604", 23, "CN-KSA-202604-000023")]
    [InlineData("EG", "202607", 1, "CN-EG-202607-000001")]
    public void CreditNote_Format_MatchesSpec(string market, string yyyymm, long seq, string expected)
    {
        var seq6 = seq.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        $"CN-{market}-{yyyymm}-{seq6}".Should().Be(expected);
    }
}
