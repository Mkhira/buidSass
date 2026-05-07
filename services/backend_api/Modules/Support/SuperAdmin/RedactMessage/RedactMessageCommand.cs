namespace BackendApi.Modules.Support.SuperAdmin.RedactMessage;

/// <summary>
/// Spec 023 Phase 10 T128 — super-admin message-body redaction per FR-011a.
/// Wipes the message body, captures actor + originating redaction-request
/// linkage, and emits <c>TicketMessageRedacted</c>. Caller may pass an
/// originating-redaction-request ticket id (from the customer's redaction
/// request flow) so the resulting redaction can be cross-linked.
/// </summary>
public sealed record RedactMessageCommand(
    Guid TicketId,
    Guid MessageId,
    Guid SuperAdminActorId,
    string ReasonNote,
    Guid? OriginatingRedactionRequestTicketId,
    Guid? RequestingCustomerId);

public sealed record RedactMessageResult(
    bool Success,
    DateTimeOffset? RedactedAtUtc,
    string? ReasonCode,
    string? Detail);
