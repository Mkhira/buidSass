using BackendApi.Modules.Shared;
using BackendApi.Modules.Support.Entities;
using BackendApi.Modules.Support.Lead.ReassignTicket;
using BackendApi.Modules.Support.Persistence;
using BackendApi.Modules.Support.Primitives;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Support.Tests.Infrastructure;

namespace Support.Tests.Integration;

/// <summary>
/// Spec 023 US4 (T085) — lead reassignment writes a superseded prior
/// assignment row, flips the denormalized AssignedAgentId cache, and emits
/// the TicketReassigned event with the justification note captured.
/// </summary>
[Collection(nameof(SupportPostgresCollection))]
public sealed class LeadReassignTicketTests
{
    private readonly SupportPostgresFixture _fx;
    public LeadReassignTicketTests(SupportPostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task Reassign_Supersedes_Prior_Assignment_And_Updates_Cache()
    {
        await using var ctx = _fx.NewContext();
        await Truncate.AllAsync(ctx);

        var nowUtc = DateTimeOffset.Parse("2026-05-06T16:00:00+00:00");
        var (ticketId, priorAgent) = await SeedAssignedTicketAsync(ctx, nowUtc);

        var newAgent = Guid.NewGuid();
        var leadId = Guid.NewGuid();
        var publisher = new RecordingPublisher();
        var handler = BuildHandler(ctx, publisher, nowUtc);

        var result = await handler.HandleAsync(new ReassignTicketCommand(
            TicketId: ticketId,
            LeadActorId: leadId,
            TargetAgentId: newAgent,
            JustificationNote: "Reassign for workload balancing — original agent on leave."), default);

        result.Success.Should().BeTrue();
        result.PriorAgentId.Should().Be(priorAgent);
        result.AssignmentId.Should().NotBeNull();

        await using var verify = _fx.NewContext();
        var ticket = await verify.Tickets.AsNoTracking().FirstAsync(t => t.Id == ticketId);
        ticket.AssignedAgentId.Should().Be(newAgent);

        var assignments = await verify.Assignments.AsNoTracking()
            .Where(a => a.TicketId == ticketId)
            .OrderBy(a => a.AssignedAtUtc)
            .ToListAsync();
        assignments.Should().HaveCount(2);
        assignments[0].AgentId.Should().Be(priorAgent);
        assignments[0].SupersededAtUtc.Should().NotBeNull();
        assignments[0].SupersededReason.Should().Be(TicketAssignmentSupersededReason.Reassigned);
        assignments[1].AgentId.Should().Be(newAgent);
        assignments[1].SupersededAtUtc.Should().BeNull();
        assignments[1].JustificationNote.Should().StartWith("Reassign for workload balancing");

        publisher.Notifications.OfType<TicketReassigned>().Should().HaveCount(1);
        var ev = publisher.Notifications.OfType<TicketReassigned>().Single();
        ev.PriorAgentId.Should().Be(priorAgent);
        ev.NewAgentId.Should().Be(newAgent);
        ev.JustificationNote.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Reassign_Rejects_When_Justification_Too_Short()
    {
        await using var ctx = _fx.NewContext();
        await Truncate.AllAsync(ctx);

        var nowUtc = DateTimeOffset.Parse("2026-05-06T16:30:00+00:00");
        var (ticketId, _) = await SeedAssignedTicketAsync(ctx, nowUtc);
        var publisher = new RecordingPublisher();
        var handler = BuildHandler(ctx, publisher, nowUtc);

        var result = await handler.HandleAsync(new ReassignTicketCommand(
            ticketId, Guid.NewGuid(), Guid.NewGuid(), "short"), default);

        result.Success.Should().BeFalse();
        result.ReasonCode.Should().Be(TicketReasonCode.ReassignJustificationRequired);
        publisher.Notifications.OfType<TicketReassigned>().Should().BeEmpty();
    }

    [Fact]
    public async Task Reassign_To_Same_Agent_Is_NoOp_Idempotent()
    {
        await using var ctx = _fx.NewContext();
        await Truncate.AllAsync(ctx);

        var nowUtc = DateTimeOffset.Parse("2026-05-06T17:00:00+00:00");
        var (ticketId, priorAgent) = await SeedAssignedTicketAsync(ctx, nowUtc);
        var publisher = new RecordingPublisher();
        var handler = BuildHandler(ctx, publisher, nowUtc);

        var result = await handler.HandleAsync(new ReassignTicketCommand(
            ticketId, Guid.NewGuid(), priorAgent,
            "Idempotent retry — already assigned to this agent."), default);

        result.Success.Should().BeTrue();

        await using var verify = _fx.NewContext();
        (await verify.Assignments.CountAsync(a => a.TicketId == ticketId)).Should().Be(1,
            "no new assignment row should be added when target equals current agent");
        publisher.Notifications.OfType<TicketReassigned>().Should().BeEmpty();
    }

    private static async Task<(Guid ticketId, Guid priorAgent)> SeedAssignedTicketAsync(
        SupportDbContext ctx, DateTimeOffset nowUtc)
    {
        var ticketId = Guid.NewGuid();
        var priorAgent = Guid.NewGuid();
        ctx.Tickets.Add(new SupportTicket
        {
            Id = ticketId,
            CustomerId = Guid.NewGuid(),
            MarketCode = "SA",
            Locale = "en",
            Category = "general_question",
            Priority = "normal",
            State = TicketStateNames.InProgress,
            Subject = "Reassign test",
            Body = "Body",
            AssignedAgentId = priorAgent,
            FirstResponseTargetMinutesSnapshot = 240,
            ResolutionTargetMinutesSnapshot = 2880,
            FirstResponseDueUtc = nowUtc.AddMinutes(240),
            ResolutionDueUtc = nowUtc.AddMinutes(2880),
            CreatedAtUtc = nowUtc.AddMinutes(-30),
            UpdatedAtUtc = nowUtc.AddMinutes(-30),
        });
        ctx.Assignments.Add(new TicketAssignment
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            AgentId = priorAgent,
            AssignmentKind = TicketAssignmentKind.SelfClaim,
            AssignedByActorId = priorAgent,
            AssignedAtUtc = nowUtc.AddMinutes(-30),
        });
        await ctx.SaveChangesAsync();
        return (ticketId, priorAgent);
    }

    private static ReassignTicketHandler BuildHandler(
        SupportDbContext ctx, IPublisher publisher, DateTimeOffset nowUtc) =>
        new(ctx, publisher, new FakeTimeProvider(nowUtc));

    private sealed class RecordingPublisher : IPublisher
    {
        public List<INotification> Notifications { get; } = new();

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            if (notification is INotification n)
            {
                lock (Notifications) Notifications.Add(n);
            }
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
        {
            lock (Notifications) Notifications.Add(notification);
            return Task.CompletedTask;
        }
    }
}
