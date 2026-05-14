using BackendApi.Modules.Support.Entities;
using BackendApi.Modules.Support.Persistence;
using BackendApi.Modules.Support.Primitives;
using Microsoft.Extensions.DependencyInjection;

namespace Support.Tests.Contract.Infrastructure;

/// <summary>
/// Per-test ticket seeding helpers shared across the spec 023 contract suite.
/// Tests use these to land tickets in specific states without going through
/// the OpenTicket HTTP surface (keeps the SUT focused on the endpoint under
/// test).
/// </summary>
public static class SupportSeedHelper
{
    /// <summary>
    /// Insert a ticket directly into Postgres at the requested state.
    /// </summary>
    public static async Task<Guid> SeedTicketAsync(
        this SupportApiFactory factory,
        Guid customerId,
        string state = TicketStateNames.Open,
        string market = "SA",
        string category = TicketCategoryNames.GeneralQuestion,
        string priority = TicketPriorityNames.Normal,
        Guid? assignedAgentId = null,
        DateTimeOffset? resolvedAtUtc = null,
        DateTimeOffset? closedAtUtc = null,
        int reopenCount = 0,
        string? linkedEntityKind = null,
        Guid? linkedEntityId = null,
        DateTimeOffset? firstResponseDueUtc = null,
        DateTimeOffset? resolutionDueUtc = null,
        int firstResponseTargetMinutesSnapshot = 240,
        int resolutionTargetMinutesSnapshot = 2880)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportDbContext>();

        var nowUtc = factory.Clock.GetUtcNow();
        var ticketId = Guid.NewGuid();
        var ticket = new SupportTicket
        {
            Id = ticketId,
            CustomerId = customerId,
            CompanyId = null,
            MarketCode = market,
            Locale = "en",
            Category = category,
            Priority = priority,
            State = state,
            Subject = "Seeded ticket",
            Body = "Seeded body content of sufficient length.",
            LinkedEntityKind = linkedEntityKind,
            LinkedEntityId = linkedEntityId,
            VendorId = null,
            AssignedAgentId = assignedAgentId,
            FirstResponseTargetMinutesSnapshot = firstResponseTargetMinutesSnapshot,
            ResolutionTargetMinutesSnapshot = resolutionTargetMinutesSnapshot,
            FirstResponseDueUtc = firstResponseDueUtc ?? nowUtc.AddMinutes(firstResponseTargetMinutesSnapshot),
            ResolutionDueUtc = resolutionDueUtc ?? nowUtc.AddMinutes(resolutionTargetMinutesSnapshot),
            ReopenCount = reopenCount,
            ResolvedAtUtc = resolvedAtUtc,
            ClosedAtUtc = closedAtUtc,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
        db.Tickets.Add(ticket);

        if (assignedAgentId is not null)
        {
            db.Assignments.Add(new TicketAssignment
            {
                Id = Guid.NewGuid(),
                TicketId = ticketId,
                AgentId = assignedAgentId.Value,
                AssignmentKind = TicketAssignmentKind.SelfClaim,
                AssignedByActorId = assignedAgentId,
                JustificationNote = null,
                AssignedAtUtc = nowUtc,
                SupersededAtUtc = null,
                SupersededReason = null,
            });
        }

        if (linkedEntityKind is not null && linkedEntityId is not null)
        {
            db.Links.Add(new TicketLink
            {
                Id = Guid.NewGuid(),
                TicketId = ticketId,
                Kind = linkedEntityKind,
                LinkedEntityId = linkedEntityId.Value,
                CreatedVia = TicketLinkCreatedVia.Submission,
                IdempotencyKey = null,
                CreatedAtUtc = nowUtc,
            });
        }

        db.Messages.Add(new TicketMessage
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

        await db.SaveChangesAsync();
        return ticketId;
    }

    /// <summary>Convenience client with the agent permission set.</summary>
    public static HttpClient AuthenticatedAgentClient(
        this SupportApiFactory factory,
        Guid agentId,
        string market = "SA",
        string permissions = "support.agent")
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Admin-Id", agentId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Market", market);
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    public static HttpClient AuthenticatedLeadClient(
        this SupportApiFactory factory, Guid leadId, string market = "SA") =>
        factory.AuthenticatedAgentClient(leadId, market, permissions: "support.lead");

    public static HttpClient AuthenticatedCustomerClient(
        this SupportApiFactory factory, Guid customerId, string market = "SA")
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Customer-Id", customerId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Market", market);
        return client;
    }
}
