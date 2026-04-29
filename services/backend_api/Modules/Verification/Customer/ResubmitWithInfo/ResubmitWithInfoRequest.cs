namespace BackendApi.Modules.Verification.Customer.ResubmitWithInfo;

/// <summary>
/// Customer's resubmission after a reviewer requested more info per spec 020
/// contracts §2.6. The customer attaches new documents via the
/// <see cref="AttachDocument.AttachDocumentEndpoint"/> first, then calls this
/// endpoint to flip the state from <c>info_requested</c> back to
/// <c>in_review</c>. Original <c>submitted_at</c> is preserved (FR-016 — the
/// queue treats the row as the same case, not a new submission); reviewer
/// SLA timer resumes from where it paused.
/// </summary>
/// <param name="Acknowledgement">Free-text confirmation that the customer responded to the info request. Required to be non-blank so empty resubmits are rejected with <c>no_changes_provided</c>.</param>
public sealed record ResubmitWithInfoRequest(string Acknowledgement);

public sealed record ResubmitWithInfoResponse(
    Guid Id,
    string State,
    DateTimeOffset SubmittedAt,
    DateTimeOffset ResubmittedAt);
