using System.Text.RegularExpressions;
using BackendApi.Modules.Payments.Services;
using FluentAssertions;

namespace BackendApi.Tests.Payments.Unit;

/// <summary>Research §7 — bank-transfer reference format.</summary>
public sealed class BankTransferReferenceGeneratorTests
{
    private static readonly Regex Pattern = new("^(SA|EG)-[0-9a-f]{8}-[A-Z2-9]{4}$", RegexOptions.Compiled);

    [Theory]
    [InlineData("sa")]
    [InlineData("SA")]
    public void GeneratesExpectedShape_KSA(string market)
    {
        var reference = BankTransferReferenceGenerator.Generate(market, Guid.NewGuid());
        Pattern.IsMatch(reference).Should().BeTrue($"got: {reference}");
        reference.Should().StartWith("SA-");
    }

    [Theory]
    [InlineData("eg")]
    [InlineData("EG")]
    public void GeneratesExpectedShape_EG(string market)
    {
        var reference = BankTransferReferenceGenerator.Generate(market, Guid.NewGuid());
        Pattern.IsMatch(reference).Should().BeTrue($"got: {reference}");
        reference.Should().StartWith("EG-");
    }

    [Fact]
    public void DifferentPayments_ProduceDifferentReferences()
    {
        var refs = Enumerable.Range(0, 50)
            .Select(_ => BankTransferReferenceGenerator.Generate("sa", Guid.NewGuid()))
            .ToHashSet();
        refs.Should().HaveCount(50);
    }
}
