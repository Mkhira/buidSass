using BackendApi.Modules.Support.Customer.OpenTicket;
using BackendApi.Modules.Support.Primitives;
using FluentAssertions;

namespace Support.Tests.Unit;

public class OpenTicketValidatorTests
{
    private static OpenTicketCommand Valid(
        string category = "general_question",
        string priority = "normal",
        string locale = "en",
        string? linkedKind = null,
        Guid? linkedId = null,
        string subject = "Help",
        string body = "Body text") =>
        new(
            CustomerId: Guid.NewGuid(),
            CustomerMarketCode: "SA",
            Locale: locale,
            Category: category,
            Priority: priority,
            Subject: subject,
            Body: body,
            LinkedEntityKind: linkedKind,
            LinkedEntityId: linkedId,
            AttachmentIds: null);

    [Fact]
    public void Valid_GeneralQuestion_NoLink_Passes()
    {
        var (ok, _, _) = OpenTicketValidator.Validate(Valid());
        ok.Should().BeTrue();
    }

    [Fact]
    public void EmptySubject_RejectedWithSubjectRequired()
    {
        var (ok, code, _) = OpenTicketValidator.Validate(Valid(subject: "  "));
        ok.Should().BeFalse();
        code.Should().Be(TicketReasonCode.SubjectRequired);
    }

    [Fact]
    public void TooLongSubject_RejectedWithSubjectTooLong()
    {
        var (ok, code, _) = OpenTicketValidator.Validate(Valid(subject: new string('x', 200)));
        ok.Should().BeFalse();
        code.Should().Be(TicketReasonCode.SubjectTooLong);
    }

    [Fact]
    public void TooLongBody_RejectedWithBodyTooLong()
    {
        var (ok, code, _) = OpenTicketValidator.Validate(Valid(body: new string('x', 8001)));
        ok.Should().BeFalse();
        code.Should().Be(TicketReasonCode.BodyTooLong);
    }

    [Fact]
    public void BadCategory_RejectedWithCategoryInvalid()
    {
        var (ok, code, _) = OpenTicketValidator.Validate(Valid(category: "no_such_category"));
        ok.Should().BeFalse();
        code.Should().Be(TicketReasonCode.CategoryInvalid);
    }

    [Fact]
    public void HighPriority_RejectedAtCustomerLayer()
    {
        var (ok, code, _) = OpenTicketValidator.Validate(Valid(priority: "high"));
        ok.Should().BeFalse();
        code.Should().Be(TicketReasonCode.PriorityNotCustomerSelectable);
    }

    [Fact]
    public void OrderIssue_WithoutLinkedEntity_Rejected()
    {
        // FR-007: order_issue requires a linked order or order_line.
        var (ok, code, _) = OpenTicketValidator.Validate(Valid(category: "order_issue"));
        ok.Should().BeFalse();
        code.Should().Be(TicketReasonCode.LinkedEntityKindInconsistent);
    }

    [Fact]
    public void OrderIssue_WithLinkedOrderLine_Passes()
    {
        var (ok, _, _) = OpenTicketValidator.Validate(Valid(
            category: "order_issue",
            linkedKind: "order_line",
            linkedId: Guid.NewGuid()));
        ok.Should().BeTrue();
    }

    [Fact]
    public void LinkedEntityKind_WithoutId_Rejected()
    {
        var (ok, code, _) = OpenTicketValidator.Validate(Valid(
            category: "order_issue",
            linkedKind: "order_line",
            linkedId: null));
        ok.Should().BeFalse();
        code.Should().Be(TicketReasonCode.LinkedEntityKindInconsistent);
    }

    [Fact]
    public void UnknownLocale_Rejected()
    {
        var (ok, code, _) = OpenTicketValidator.Validate(Valid(locale: "fr"));
        ok.Should().BeFalse();
        code.Should().Be(TicketReasonCode.LocaleInvalid);
    }
}
