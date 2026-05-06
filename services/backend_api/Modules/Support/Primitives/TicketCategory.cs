namespace BackendApi.Modules.Support.Primitives;

/// <summary>
/// Fixed 10 categories per FR-007. The 10th — <see cref="RedactionRequest"/> —
/// is customer-initiated and routes to a super_admin-only triage queue.
/// </summary>
public enum TicketCategory
{
    OrderIssue = 0,
    PaymentIssue = 1,
    ShippingIssue = 2,
    ProductDefect = 3,
    ReturnRefundRequest = 4,
    QuoteQuestion = 5,
    AccountVerification = 6,
    GeneralQuestion = 7,
    ReviewDispute = 8,
    VerificationQuery = 9,
    RedactionRequest = 10,
}

public static class TicketCategoryNames
{
    public const string OrderIssue = "order_issue";
    public const string PaymentIssue = "payment_issue";
    public const string ShippingIssue = "shipping_issue";
    public const string ProductDefect = "product_defect";
    public const string ReturnRefundRequest = "return_refund_request";
    public const string QuoteQuestion = "quote_question";
    public const string AccountVerification = "account_verification";
    public const string GeneralQuestion = "general_question";
    public const string ReviewDispute = "review_dispute";
    public const string VerificationQuery = "verification_query";
    public const string RedactionRequest = "redaction_request";

    public static readonly string[] All =
    {
        OrderIssue, PaymentIssue, ShippingIssue, ProductDefect, ReturnRefundRequest,
        QuoteQuestion, AccountVerification, GeneralQuestion, ReviewDispute,
        VerificationQuery, RedactionRequest,
    };

    public static string ToWire(TicketCategory c) => c switch
    {
        TicketCategory.OrderIssue => OrderIssue,
        TicketCategory.PaymentIssue => PaymentIssue,
        TicketCategory.ShippingIssue => ShippingIssue,
        TicketCategory.ProductDefect => ProductDefect,
        TicketCategory.ReturnRefundRequest => ReturnRefundRequest,
        TicketCategory.QuoteQuestion => QuoteQuestion,
        TicketCategory.AccountVerification => AccountVerification,
        TicketCategory.GeneralQuestion => GeneralQuestion,
        TicketCategory.ReviewDispute => ReviewDispute,
        TicketCategory.VerificationQuery => VerificationQuery,
        TicketCategory.RedactionRequest => RedactionRequest,
        _ => throw new InvalidOperationException($"Unknown TicketCategory: {c}"),
    };

    public static TicketCategory FromWire(string s) => s switch
    {
        OrderIssue => TicketCategory.OrderIssue,
        PaymentIssue => TicketCategory.PaymentIssue,
        ShippingIssue => TicketCategory.ShippingIssue,
        ProductDefect => TicketCategory.ProductDefect,
        ReturnRefundRequest => TicketCategory.ReturnRefundRequest,
        QuoteQuestion => TicketCategory.QuoteQuestion,
        AccountVerification => TicketCategory.AccountVerification,
        GeneralQuestion => TicketCategory.GeneralQuestion,
        ReviewDispute => TicketCategory.ReviewDispute,
        VerificationQuery => TicketCategory.VerificationQuery,
        RedactionRequest => TicketCategory.RedactionRequest,
        _ => throw new InvalidOperationException($"Unknown TicketCategory wire value: '{s}'."),
    };

    /// <summary>FR-007 — category-vs-linked-entity-kind consistency map.</summary>
    public static bool IsConsistentWithLinkedKind(TicketCategory category, string? linkedKind)
    {
        // Categories that REQUIRE a specific linked-entity-kind:
        return (category, linkedKind) switch
        {
            (TicketCategory.OrderIssue, "order" or "order_line") => true,
            (TicketCategory.PaymentIssue, "order" or "order_line") => true,
            (TicketCategory.ShippingIssue, "order" or "order_line") => true,
            (TicketCategory.ProductDefect, "order_line") => true,
            (TicketCategory.ReturnRefundRequest, "order_line" or "return_request") => true,
            (TicketCategory.QuoteQuestion, "quote") => true,
            (TicketCategory.ReviewDispute, "review") => true,
            (TicketCategory.VerificationQuery, "verification") => true,
            // Categories that allow standalone (no linked entity):
            (TicketCategory.AccountVerification, null) => true,
            (TicketCategory.GeneralQuestion, null) => true,
            (TicketCategory.RedactionRequest, _) => true, // any link or none
            _ => false,
        };
    }

    /// <summary>FR-028 — categories eligible for ticket-to-return conversion.</summary>
    public static bool IsConvertibleToReturn(TicketCategory category) =>
        category == TicketCategory.ReturnRefundRequest
        || category == TicketCategory.ProductDefect;

    public static string IcuKey(TicketCategory category) =>
        $"support.category.{ToWire(category)}";
}
