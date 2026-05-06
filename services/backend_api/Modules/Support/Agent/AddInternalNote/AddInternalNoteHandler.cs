using BackendApi.Modules.Support.Entities;
using BackendApi.Modules.Support.Persistence;
using BackendApi.Modules.Support.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Support.Agent.AddInternalNote;

public sealed record AddInternalNoteCommand(
    Guid TicketId,
    Guid ActorId,
    string ActorRole,
    string Body);

public sealed record AddInternalNoteResult(
    bool Success,
    Guid? MessageId,
    string? ReasonCode,
    string? Detail);

/// <summary>
/// FR-014a — any agent in market may post an internal note (no assignment
/// requirement). Internal notes are server-side stripped from customer-facing
/// reads (see <see cref="Customer.GetMyTicket.GetMyTicketHandler"/>).
/// </summary>
public sealed class AddInternalNoteHandler
{
    private readonly SupportDbContext _db;
    private readonly TimeProvider _clock;

    public AddInternalNoteHandler(SupportDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<AddInternalNoteResult> HandleAsync(AddInternalNoteCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.Body))
        {
            return Failure(TicketReasonCode.MessageBodyRequired, "Body is required.");
        }
        if (cmd.Body.Length > 8000)
        {
            return Failure(TicketReasonCode.MessageBodyTooLong, "Body exceeds 8000 characters.");
        }

        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == cmd.TicketId, ct);
        if (ticket is null)
        {
            return Failure(TicketReasonCode.LinkedEntityNotFound, "Ticket not found.");
        }
        if (ticket.State == TicketStateNames.Closed)
        {
            return Failure(TicketReasonCode.ClosedTerminal, "Ticket is closed.");
        }

        var nowUtc = _clock.GetUtcNow();
        var messageId = Guid.NewGuid();

        _db.Messages.Add(new TicketMessage
        {
            Id = messageId,
            TicketId = ticket.Id,
            Kind = TicketMessageKindNames.InternalNote,
            ActorId = cmd.ActorId,
            ActorRole = cmd.ActorRole,
            Body = cmd.Body,
            BodyLocale = ticket.Locale,
            LeadIntervention = false,
            CreatedAtUtc = nowUtc,
        });

        await _db.SaveChangesAsync(ct);
        return new AddInternalNoteResult(true, messageId, null, null);
    }

    private static AddInternalNoteResult Failure(string code, string detail) =>
        new(false, null, code, detail);
}
