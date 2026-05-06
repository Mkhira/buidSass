using BackendApi.Modules.Shared;
using BackendApi.Modules.Support.Entities;
using BackendApi.Modules.Support.Persistence;
using BackendApi.Modules.Support.Primitives;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Support.Customer.OpenTicket;

/// <summary>
/// Handler for the customer-side ticket open flow per quickstart §1.4.
///
/// Steps:
///   1. Run pure-function validation (caller has already done HTTP-level validation).
///   2. Resolve <c>market_code</c> + ownership via <see cref="MarketCodeResolver"/>.
///   3. Resolve B2B <c>company_id</c> via <see cref="ICompanyAccountQuery"/>.
///   4. Look up the per-market policy + the SLA policy for the chosen priority.
///   5. Snapshot the SLA targets onto the ticket row (FR-022).
///   6. Persist the ticket + initial system_event message + optional ticket_link.
///   7. Emit <see cref="TicketOpened"/> + <see cref="TicketStateChanged"/> events.
/// </summary>
public sealed class OpenTicketHandler
{
    private readonly SupportDbContext _db;
    private readonly MarketCodeResolver _marketCodeResolver;
    private readonly ICompanyAccountQuery _companyAccountQuery;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _clock;

    public OpenTicketHandler(
        SupportDbContext db,
        MarketCodeResolver marketCodeResolver,
        ICompanyAccountQuery companyAccountQuery,
        IPublisher publisher,
        TimeProvider clock)
    {
        _db = db;
        _marketCodeResolver = marketCodeResolver;
        _companyAccountQuery = companyAccountQuery;
        _publisher = publisher;
        _clock = clock;
    }

    public async Task<OpenTicketResult> HandleAsync(OpenTicketCommand cmd, CancellationToken ct)
    {
        var (ok, reasonCode, detail) = OpenTicketValidator.Validate(cmd);
        if (!ok)
        {
            return Failure(reasonCode!, detail);
        }

        // Step 2 — resolve market + ownership.
        MarketResolution market;
        try
        {
            market = await _marketCodeResolver.ResolveAsync(
                cmd.LinkedEntityKind,
                cmd.LinkedEntityId,
                cmd.CustomerId,
                cmd.CustomerMarketCode,
                ct);
        }
        catch (MarketCodeUnresolvableException)
        {
            return Failure(TicketReasonCode.MarketCodeUnresolvable,
                "Linked entity is not currently available; cannot resolve market.");
        }

        if (cmd.LinkedEntityKind is not null && !market.OwnedByActor)
        {
            return Failure(TicketReasonCode.LinkedEntityNotOwned,
                "Linked entity is not owned by the authenticated customer.");
        }

        // Step 3 — B2B company resolution (best-effort; null for B2C).
        var companyId = await _companyAccountQuery.ResolveCompanyIdAsync(cmd.CustomerId, ct);

        // Step 4 — load SLA policy for (market, priority); if missing, fall back to defaults.
        var sla = await _db.SlaPolicies
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.MarketCode == market.MarketCode && p.Priority == cmd.Priority, ct);

        int firstResponseTarget;
        int resolutionTarget;
        if (sla is not null)
        {
            firstResponseTarget = sla.FirstResponseTargetMinutes;
            resolutionTarget = sla.ResolutionTargetMinutes;
        }
        else
        {
            (firstResponseTarget, resolutionTarget) = SlaPolicySnapshot.DefaultFor(cmd.Priority);
        }

        var nowUtc = _clock.GetUtcNow();
        var ticketId = Guid.NewGuid();

        var ticket = new SupportTicket
        {
            Id = ticketId,
            CustomerId = cmd.CustomerId,
            CompanyId = companyId,
            MarketCode = market.MarketCode,
            Locale = cmd.Locale,
            Category = cmd.Category,
            Priority = cmd.Priority,
            State = TicketStateNames.Open,
            Subject = cmd.Subject,
            Body = cmd.Body,
            LinkedEntityKind = cmd.LinkedEntityKind,
            LinkedEntityId = cmd.LinkedEntityId,
            VendorId = market.VendorId,
            AssignedAgentId = null,
            FirstResponseTargetMinutesSnapshot = firstResponseTarget,
            ResolutionTargetMinutesSnapshot = resolutionTarget,
            FirstResponseDueUtc = nowUtc.AddMinutes(firstResponseTarget),
            ResolutionDueUtc = nowUtc.AddMinutes(resolutionTarget),
            ReopenCount = 0,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };

        _db.Tickets.Add(ticket);

        // Initial system_event message capturing the open transition.
        _db.Messages.Add(new TicketMessage
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            Kind = TicketMessageKindNames.SystemEvent,
            ActorId = null,
            ActorRole = TicketActorKindNames.System,
            Body = null,
            BodyLocale = null,
            LeadIntervention = false,
            CreatedAtUtc = nowUtc,
        });

        // Submission link if a linked entity is supplied.
        if (cmd.LinkedEntityKind is not null && cmd.LinkedEntityId is not null)
        {
            _db.Links.Add(new TicketLink
            {
                Id = Guid.NewGuid(),
                TicketId = ticketId,
                Kind = cmd.LinkedEntityKind,
                LinkedEntityId = cmd.LinkedEntityId.Value,
                CreatedVia = TicketLinkCreatedVia.Submission,
                IdempotencyKey = null,
                CreatedAtUtc = nowUtc,
            });
        }

        await _db.SaveChangesAsync(ct);

        // Emit domain events. Failures here do not roll back the persisted ticket.
        await _publisher.Publish(new TicketOpened(
            TicketId: ticketId,
            CustomerId: cmd.CustomerId,
            MarketCode: market.MarketCode,
            Category: cmd.Category,
            Priority: cmd.Priority,
            OccurredAtUtc: nowUtc), ct);

        await _publisher.Publish(new TicketStateChanged(
            TicketId: ticketId,
            FromState: "(none)",
            ToState: TicketStateNames.Open,
            TriggeredBy: TicketTriggerKind.CustomerSubmission,
            OccurredAtUtc: nowUtc), ct);

        return new OpenTicketResult(
            Success: true,
            TicketId: ticketId,
            State: TicketStateNames.Open,
            MarketCode: market.MarketCode,
            AssignedAgentId: null,
            FirstResponseDueUtc: ticket.FirstResponseDueUtc,
            ResolutionDueUtc: ticket.ResolutionDueUtc,
            ReasonCode: null,
            Detail: null);
    }

    private static OpenTicketResult Failure(string reasonCode, string? detail) =>
        new(false, null, null, null, null, null, null, reasonCode, detail);
}
