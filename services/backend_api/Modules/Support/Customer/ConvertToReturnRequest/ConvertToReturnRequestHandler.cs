using BackendApi.Modules.Shared;
using BackendApi.Modules.Support.Entities;
using BackendApi.Modules.Support.Persistence;
using BackendApi.Modules.Support.Primitives;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Support.Customer.ConvertToReturnRequest;

public sealed record ConvertToReturnRequestCommand(
    Guid TicketId,
    Guid CustomerId,
    string IdempotencyKey,
    string? Narrative,
    IReadOnlyList<Guid>? AttachmentIds);

public sealed record ConvertToReturnRequestResult(
    bool Success,
    Guid? ReturnRequestId,
    bool Idempotent,
    string? ReasonCode,
    string? Detail);

/// <summary>
/// FR-028 — idempotent ticket→return conversion. The handler:
///  1. Validates ticket ownership + category eligibility.
///  2. Locates the originating order_line via the ticket's linked entity or
///     via the most recent <c>order_line</c> link.
///  3. Calls <see cref="IReturnRequestCreationContract.CreateAsync"/> with the
///     same idempotency key on every retry.
///  4. Persists a <c>TicketLink</c> with kind=<c>return_request</c> and the
///     same idempotency key (the partial unique index makes the row insertion
///     itself idempotent).
///  5. Transitions the ticket to <c>waiting_customer</c> and emits
///     <see cref="TicketConvertedToReturn"/>.
/// </summary>
public sealed class ConvertToReturnRequestHandler
{
    private readonly SupportDbContext _db;
    private readonly IReturnRequestCreationContract _returnContract;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _clock;

    public ConvertToReturnRequestHandler(
        SupportDbContext db,
        IReturnRequestCreationContract returnContract,
        IPublisher publisher,
        TimeProvider clock)
    {
        _db = db;
        _returnContract = returnContract;
        _publisher = publisher;
        _clock = clock;
    }

    public async Task<ConvertToReturnRequestResult> HandleAsync(
        ConvertToReturnRequestCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.IdempotencyKey))
        {
            return Failure(TicketReasonCode.ConversionForbidden, "Idempotency-Key required.");
        }

        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == cmd.TicketId, ct);
        if (ticket is null)
        {
            return Failure(TicketReasonCode.LinkedEntityNotFound, "Ticket not found.");
        }
        if (ticket.CustomerId != cmd.CustomerId)
        {
            return Failure(TicketReasonCode.ConversionForbidden,
                "Ticket is not owned by this customer.");
        }
        if (ticket.State == TicketStateNames.Closed)
        {
            return Failure(TicketReasonCode.ClosedTerminal, "Ticket is closed.");
        }

        var category = TicketCategoryNames.FromWire(ticket.Category);
        if (!TicketCategoryNames.IsConvertibleToReturn(category))
        {
            return Failure(TicketReasonCode.ConversionCategoryNotEligible,
                $"Category '{ticket.Category}' is not eligible for ticket→return conversion.");
        }

        // Idempotent retry: a prior conversion under this same idempotency key
        // already produced the row.
        var existingLink = await _db.Links.AsNoTracking()
            .FirstOrDefaultAsync(l => l.TicketId == ticket.Id
                && l.Kind == TicketLinkedEntityKindNames.ReturnRequest
                && l.IdempotencyKey == cmd.IdempotencyKey, ct);
        if (existingLink is not null)
        {
            return new ConvertToReturnRequestResult(true, existingLink.LinkedEntityId, true, null, null);
        }

        // Find the order_line — either the ticket's own LinkedEntityId (if order_line)
        // or the most recent order_line link on this ticket.
        Guid? orderLineId = null;
        if (ticket.LinkedEntityKind == TicketLinkedEntityKindNames.OrderLine
            && ticket.LinkedEntityId is not null)
        {
            orderLineId = ticket.LinkedEntityId;
        }
        else
        {
            var olLink = await _db.Links.AsNoTracking()
                .Where(l => l.TicketId == ticket.Id && l.Kind == TicketLinkedEntityKindNames.OrderLine)
                .OrderByDescending(l => l.CreatedAtUtc)
                .FirstOrDefaultAsync(ct);
            orderLineId = olLink?.LinkedEntityId;
        }

        if (orderLineId is null)
        {
            return Failure(TicketReasonCode.ConversionCategoryNotEligible,
                "Ticket has no associated order_line; cannot convert to return-request.");
        }

        var creationResult = await _returnContract.CreateAsync(
            customerId: cmd.CustomerId,
            orderLineId: orderLineId.Value,
            narrative: cmd.Narrative ?? ticket.Body,
            attachmentIds: cmd.AttachmentIds ?? Array.Empty<Guid>(),
            originatingTicketId: ticket.Id,
            idempotencyKey: cmd.IdempotencyKey,
            ct: ct);

        if (!creationResult.Success || creationResult.ReturnRequestId is null)
        {
            return Failure(creationResult.ReasonCode ?? TicketReasonCode.ReturnCreationContractFailed,
                "Return-request creation contract did not succeed.");
        }

        var nowUtc = _clock.GetUtcNow();
        _db.Links.Add(new TicketLink
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Kind = TicketLinkedEntityKindNames.ReturnRequest,
            LinkedEntityId = creationResult.ReturnRequestId.Value,
            CreatedVia = TicketLinkCreatedVia.Conversion,
            IdempotencyKey = cmd.IdempotencyKey,
            CreatedAtUtc = nowUtc,
        });

        // Transition the ticket to waiting_customer (we're waiting on the
        // return-request flow to produce its outcome).
        var fromState = TicketStateNames.FromWire(ticket.State);
        if (fromState == TicketState.Open || fromState == TicketState.InProgress)
        {
            // open → in_progress would be required first if currently open;
            // the subsequent transition to waiting_customer is the convert action's
            // operational step.
            if (fromState == TicketState.Open)
            {
                ticket.State = TicketStateNames.ToWire(TicketState.InProgress);
            }
            ticket.State = TicketStateNames.ToWire(TicketState.WaitingCustomer);
            ticket.UpdatedAtUtc = nowUtc;
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is Npgsql.PostgresException pg && pg.SqlState == "23505")
        {
            // Idempotency-key uniqueness collision — another concurrent convert won.
            // Re-read and return the existing return-request id.
            var raceLink = await _db.Links.AsNoTracking()
                .FirstOrDefaultAsync(l => l.TicketId == ticket.Id
                    && l.Kind == TicketLinkedEntityKindNames.ReturnRequest
                    && l.IdempotencyKey == cmd.IdempotencyKey, ct);
            if (raceLink is not null)
            {
                return new ConvertToReturnRequestResult(true, raceLink.LinkedEntityId, true, null, null);
            }
            return Failure(TicketReasonCode.ConversionAlreadyConverted,
                "Concurrent conversion in progress; retry.");
        }

        await _publisher.Publish(new TicketConvertedToReturn(
            TicketId: ticket.Id,
            ReturnRequestId: creationResult.ReturnRequestId.Value,
            OccurredAtUtc: nowUtc), ct);

        return new ConvertToReturnRequestResult(
            Success: true,
            ReturnRequestId: creationResult.ReturnRequestId,
            Idempotent: creationResult.Idempotent,
            ReasonCode: null,
            Detail: null);
    }

    private static ConvertToReturnRequestResult Failure(string reasonCode, string detail) =>
        new(false, null, false, reasonCode, detail);
}
