namespace BackendApi.Modules.Support.Customer.OpenRedactionRequestTicket;

/// <summary>
/// Spec 023 Phase 10 T129 — customer-initiated redaction-request ticket
/// flow per FR-011a. Targets a specific message in a specific originating
/// ticket; the resulting ticket uses <c>category=redaction_request</c>,
/// auto-routes to the super_admin queue, and bypasses the FR-019 auto-assign
/// path (super_admin pulls from the dedicated redaction-request queue).
/// </summary>
public sealed record OpenRedactionRequestTicketCommand(
    Guid CustomerId,
    string CustomerMarketCode,
    string Locale,
    Guid OriginatingTicketId,
    Guid TargetMessageId,
    string Subject,
    string Reason);

public sealed record OpenRedactionRequestTicketResult(
    bool Success,
    Guid? TicketId,
    string? State,
    string? ReasonCode,
    string? Detail);
