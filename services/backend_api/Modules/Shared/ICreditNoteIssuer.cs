namespace BackendApi.Modules.Shared;

/// <summary>
/// Spec 013 → spec 012 seam (FR-008). On <c>refund.completed</c> / <c>refund.manual_confirmed</c>,
/// the Returns dispatcher calls this to issue a credit note referencing the original invoice.
/// Idempotent on <c>refundId</c> — replays return the existing credit note.
///
/// Spec 012 ships the real implementation as an in-process adapter around
/// <c>IssueCreditNoteHandler</c>.
/// </summary>
public interface ICreditNoteIssuer
{
    Task<CreditNoteIssueResult> IssueForRefundAsync(
        CreditNoteIssueRequest request,
        CancellationToken cancellationToken);
}

public sealed record CreditNoteIssueRequest(
    Guid OrderId,
    Guid RefundId,
    string ReasonCode,
    IReadOnlyList<CreditNoteIssueLine> Lines);

public sealed record CreditNoteIssueLine(Guid OrderLineId, int Qty);

public sealed record CreditNoteIssueResult(
    bool IsSuccess,
    Guid? CreditNoteId,
    string? CreditNoteNumber,
    string? ErrorCode,
    string? ErrorMessage);
