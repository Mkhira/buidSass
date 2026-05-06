namespace BackendApi.Modules.Support.SuperAdmin.RedactAttachment;

/// <summary>
/// Spec 023 Phase 10 T127 — super-admin redaction of an attachment per
/// FR-012a. Tombstones the storage object and stamps the row to
/// <see cref="BackendApi.Modules.Support.Entities.TicketAttachmentState.Redacted"/>.
/// </summary>
public sealed record RedactAttachmentCommand(
    Guid TicketId,
    Guid AttachmentId,
    Guid SuperAdminActorId,
    string ReasonNote);

public sealed record RedactAttachmentResult(
    bool Success,
    DateTimeOffset? RedactedAtUtc,
    string? ReasonCode,
    string? Detail);
