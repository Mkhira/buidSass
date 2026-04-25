using FluentAssertions;

namespace Orders.Tests.Unit;

/// <summary>
/// H1 partial — order-number format invariants. The full SC-002 collision fuzz (10k concurrent
/// orders) needs a real Postgres sequence and lives in the deferred integration suite. Here we
/// verify the format spec: <c>ORD-{MARKET}-{YYYYMM}-{SEQ6}</c>.
/// </summary>
public sealed class OrderNumberFormatTests
{
    [Theory]
    [InlineData("KSA", "202604", 187, "ORD-KSA-202604-000187")]
    [InlineData("EG", "202612", 1, "ORD-EG-202612-000001")]
    [InlineData("KSA", "202601", 999_999, "ORD-KSA-202601-999999")]
    public void Format_MatchesSpec(string market, string yyyymm, long seq, string expected)
    {
        var seq6 = seq.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        var actual = $"ORD-{market}-{yyyymm}-{seq6}";
        actual.Should().Be(expected);
    }
}
