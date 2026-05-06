using BackendApi.Modules.Shared;
using BackendApi.Modules.Support.Entities;
using BackendApi.Modules.Support.Persistence;
using BackendApi.Modules.Support.Primitives;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Support.Subscribers;

/// <summary>
/// Spec 023 Phase 10 T136 / FR-027 — when a customer account is locked or
/// deleted, every non-closed ticket authored by that customer transitions to
/// <c>closed</c> with <c>triggered_by=author_account_locked</c>. Market-change
/// is a no-op (tickets stay in their original market per Principle 5).
///
/// <para>Implements spec 004's <see cref="ICustomerAccountLifecycleSubscriber"/>
/// contract. Handler is idempotent: a redelivered event finds the affected
/// tickets already closed and exits cleanly.</para>
/// </summary>
public sealed class CustomerAccountLifecycleHandler(
    SupportDbContext db,
    IPublisher publisher,
    TimeProvider clock,
    ILogger<CustomerAccountLifecycleHandler> logger) : ICustomerAccountLifecycleSubscriber
{
    public Task OnAccountLockedAsync(CustomerAccountLocked evt, CancellationToken ct) =>
        CascadeCloseAsync(evt.CustomerId, "account_locked", ct);

    public Task OnAccountDeletedAsync(CustomerAccountDeleted evt, CancellationToken ct) =>
        CascadeCloseAsync(evt.CustomerId, "account_deleted", ct);

    public Task OnMarketChangedAsync(CustomerMarketChanged evt, CancellationToken ct) =>
        Task.CompletedTask;

    private async Task CascadeCloseAsync(Guid customerId, string reasonLabel, CancellationToken ct)
    {
        var affected = await db.Tickets
            .Where(t => t.CustomerId == customerId
                     && t.State != TicketStateNames.Closed)
            .ToListAsync(ct);
        if (affected.Count == 0) return;

        var nowUtc = clock.GetUtcNow();
        var transitioned = new List<(SupportTicket Ticket, string FromState)>();

        foreach (var ticket in affected)
        {
            var fromState = TicketStateNames.FromWire(ticket.State);
            if (!TicketStateMachine.TryTransition(
                    fromState, TicketState.Closed,
                    TicketTriggerKind.AuthorAccountLocked,
                    TicketActorKind.System, out var reason))
            {
                logger.LogInformation(
                    "AccountLifecycle cascade-close skipped ticket {TicketId} from state {FromState}: {Reason}",
                    ticket.Id, ticket.State, reason);
                continue;
            }

            ticket.State = TicketStateNames.ToWire(TicketState.Closed);
            ticket.ClosedAtUtc = nowUtc;
            ticket.UpdatedAtUtc = nowUtc;

            db.Messages.Add(new TicketMessage
            {
                Id = Guid.NewGuid(),
                TicketId = ticket.Id,
                Kind = TicketMessageKindNames.SystemEvent,
                ActorId = null,
                ActorRole = TicketActorKindNames.System,
                Body = $"Auto-closed — customer {reasonLabel}.",
                BodyLocale = ticket.Locale,
                LeadIntervention = false,
                CreatedAtUtc = nowUtc,
            });

            transitioned.Add((ticket, TicketStateNames.ToWire(fromState)));
        }

        if (transitioned.Count == 0) return;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogInformation(
                "AccountLifecycle cascade-close hit optimistic-concurrency for customer {CustomerId}; redelivery will retry.",
                customerId);
            return;
        }

        foreach (var (ticket, fromState) in transitioned)
        {
            try
            {
                await publisher.Publish(new TicketStateChanged(
                    TicketId: ticket.Id,
                    FromState: fromState,
                    ToState: TicketStateNames.ToWire(TicketState.Closed),
                    TriggeredBy: TicketTriggerKind.AuthorAccountLocked,
                    OccurredAtUtc: nowUtc), ct);

                await publisher.Publish(new TicketClosed(
                    TicketId: ticket.Id,
                    TriggeredBy: TicketTriggerKind.AuthorAccountLocked,
                    ClosedAtUtc: nowUtc), ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "AccountLifecycle event publish failed for ticket {TicketId}; row already persisted.",
                    ticket.Id);
            }
        }

        logger.LogInformation(
            "AccountLifecycle cascade-closed {Count} ticket(s) for customer {CustomerId} ({Reason}).",
            transitioned.Count, customerId, reasonLabel);
    }
}
