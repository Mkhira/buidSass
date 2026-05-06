using BackendApi.Modules.Support.Primitives;
using FluentAssertions;

namespace Support.Tests.Unit;

public class TicketCategoryConsistencyTests
{
    [Theory]
    [InlineData(TicketCategory.OrderIssue, "order_line", true)]
    [InlineData(TicketCategory.OrderIssue, "order", true)]
    [InlineData(TicketCategory.OrderIssue, null, false)]
    [InlineData(TicketCategory.OrderIssue, "review", false)]
    [InlineData(TicketCategory.ProductDefect, "order_line", true)]
    [InlineData(TicketCategory.ReturnRefundRequest, "order_line", true)]
    [InlineData(TicketCategory.ReturnRefundRequest, "return_request", true)]
    [InlineData(TicketCategory.QuoteQuestion, "quote", true)]
    [InlineData(TicketCategory.ReviewDispute, "review", true)]
    [InlineData(TicketCategory.VerificationQuery, "verification", true)]
    [InlineData(TicketCategory.GeneralQuestion, null, true)]
    [InlineData(TicketCategory.GeneralQuestion, "order", false)]
    [InlineData(TicketCategory.AccountVerification, null, true)]
    [InlineData(TicketCategory.RedactionRequest, null, true)]
    [InlineData(TicketCategory.RedactionRequest, "review", true)]
    public void IsConsistentWithLinkedKind_MatchesFR007(TicketCategory category, string? linkedKind, bool expected)
    {
        TicketCategoryNames.IsConsistentWithLinkedKind(category, linkedKind).Should().Be(expected);
    }

    [Fact]
    public void IsConvertibleToReturn_OnlyReturnRefundAndProductDefect()
    {
        TicketCategoryNames.IsConvertibleToReturn(TicketCategory.ReturnRefundRequest).Should().BeTrue();
        TicketCategoryNames.IsConvertibleToReturn(TicketCategory.ProductDefect).Should().BeTrue();
        TicketCategoryNames.IsConvertibleToReturn(TicketCategory.OrderIssue).Should().BeFalse();
        TicketCategoryNames.IsConvertibleToReturn(TicketCategory.GeneralQuestion).Should().BeFalse();
        TicketCategoryNames.IsConvertibleToReturn(TicketCategory.QuoteQuestion).Should().BeFalse();
    }

    [Fact]
    public void EveryWireCategoryRoundTrips()
    {
        foreach (var c in Enum.GetValues<TicketCategory>())
        {
            var wire = TicketCategoryNames.ToWire(c);
            TicketCategoryNames.FromWire(wire).Should().Be(c);
            TicketCategoryNames.All.Should().Contain(wire);
        }
    }
}
