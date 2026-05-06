using BackendApi.Modules.Shared;
using BackendApi.Modules.Support.Entities;
using BackendApi.Modules.Support.Persistence;
using BackendApi.Modules.Support.Primitives;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Support.SuperAdmin.RedactAttachment;

/// <summary>
/// Implements FR-012a (super-admin attachment redaction). Mutates the
/// otherwise-append-only <c>ticket_attachments</c> row through the controlled
/// redaction-exception path: clears <c>storage_object_id</c>, sets
/// <c>state=redacted</c>, captures actor + reason note, and emits
/// <see cref="TicketAttachmentRedacted"/>.
///
/// <para>Storage tombstone (deletion of the underlying object via spec 015's
/// storage abstraction) is left as a TODO until that abstraction is wired —
/// the redaction tombstone in Postgres is the durable signal in V1.</para>
/// </summary>
public sealed class RedactAttachmentHandler
{
    public const int MinimumReasonLength = 10;

    private readonly SupportDbContext _db;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _clock;

    public RedactAttachmentHandler(SupportDbContext db, IPublisher publisher, TimeProvider clock)
    {
        _db = db;
        _publisher = publisher;
        _clock = clock;
    }

    public async Task<RedactAttachmentResult> HandleAsync(RedactAttachmentCommand cmd, CancellationToken ct)
    {
        if (cmd.ReasonNote is null
            || cmd.ReasonNote.Trim().Length < MinimumReasonLength)
        {
            return Failure(TicketReasonCode.RedactionReasonRequired,
                $"Redaction reason note must be at least {MinimumReasonLength} characters.");
        }

        var attachment = await _db.Attachments
            .FirstOrDefaultAsync(a => a.Id == cmd.AttachmentId && a.TicketId == cmd.TicketId, ct);
        if (attachment is null)
        {
            return Failure(TicketReasonCode.LinkedEntityNotFound, "Attachment not found.");
        }

        if (attachment.State == TicketAttachmentState.Redacted)
        {
            return Failure(TicketReasonCode.RedactionAttachmentAlreadyRedacted,
                "Attachment is already redacted.");
        }

        var nowUtc = _clock.GetUtcNow();
        attachment.State = TicketAttachmentState.Redacted;
        attachment.StorageObjectId = null;
        attachment.RedactedAtUtc = nowUtc;
        attachment.RedactedByRole = TicketActorKindNames.SuperAdmin;
        attachment.RedactionReasonNote = cmd.ReasonNote.Trim();

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Failure(TicketReasonCode.VersionConflict,
                "Attachment was modified concurrently; retry the request.");
        }

        await _publisher.Publish(new TicketAttachmentRedacted(
            TicketId: cmd.TicketId,
            AttachmentId: cmd.AttachmentId,
            RequestingActorId: cmd.SuperAdminActorId,
            OccurredAtUtc: nowUtc), ct);

        return new RedactAttachmentResult(true, nowUtc, null, null);
    }

    private static RedactAttachmentResult Failure(string reasonCode, string detail) =>
        new(false, null, reasonCode, detail);
}
