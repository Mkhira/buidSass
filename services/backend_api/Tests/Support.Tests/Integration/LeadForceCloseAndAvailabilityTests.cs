using BackendApi.Modules.Shared;
using BackendApi.Modules.Support.Entities;
using BackendApi.Modules.Support.Lead.ForceCloseTicket;
using BackendApi.Modules.Support.Lead.ToggleAgentAvailability;
using BackendApi.Modules.Support.Persistence;
using BackendApi.Modules.Support.Primitives;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Support.Tests.Infrastructure;

namespace Support.Tests.Integration;

[Collection(nameof(SupportPostgresCollection))]
public sealed class LeadForceCloseAndAvailabilityTests
{
    private readonly SupportPostgresFixture _fx;
    public LeadForceCloseAndAvailabilityTests(SupportPostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task ForceClose_Closes_NonClosed_Ticket_From_Any_State()
    {
        await using var ctx = _fx.NewContext();
        await Truncate.AllAsync(ctx);

        var nowUtc = DateTimeOffset.Parse("2026-05-06T20:00:00+00:00");
        var ticketId = await SeedTicketAsync(ctx, nowUtc, TicketStateNames.WaitingCustomer);

        var publisher = new RecordingPublisher();
        var handler = new ForceCloseTicketHandler(ctx, publisher, new FakeTimeProvider(nowUtc));

        var result = await handler.HandleAsync(new ForceCloseTicketCommand(
            ticketId, Guid.NewGuid(),
            "Force-close: customer unreachable for 30 days; all paths exhausted."), default);

        result.Success.Should().BeTrue();
        result.PriorState.Should().Be(TicketStateNames.WaitingCustomer);

        await using var verify = _fx.NewContext();
        var ticket = await verify.Tickets.AsNoTracking().FirstAsync(t => t.Id == ticketId);
        ticket.State.Should().Be(TicketStateNames.Closed);
        ticket.ClosedAtUtc.Should().Be(nowUtc);

        publisher.Notifications.OfType<TicketClosed>().Should().HaveCount(1);
        publisher.Notifications.OfType<TicketClosed>().Single()
            .TriggeredBy.Should().Be(TicketTriggerKind.LeadForceClose);
    }

    [Fact]
    public async Task ForceClose_Is_Idempotent_On_Closed_Ticket()
    {
        await using var ctx = _fx.NewContext();
        await Truncate.AllAsync(ctx);

        var nowUtc = DateTimeOffset.Parse("2026-05-06T20:30:00+00:00");
        var ticketId = await SeedTicketAsync(ctx, nowUtc.AddDays(-2), TicketStateNames.Closed,
            closedAtUtc: nowUtc.AddDays(-2));

        var publisher = new RecordingPublisher();
        var handler = new ForceCloseTicketHandler(ctx, publisher, new FakeTimeProvider(nowUtc));

        var result = await handler.HandleAsync(new ForceCloseTicketCommand(
            ticketId, Guid.NewGuid(),
            "Idempotent retry: ticket already closed earlier."), default);

        result.Success.Should().BeTrue();
        result.PriorState.Should().Be(TicketStateNames.Closed);
        result.Detail.Should().Contain("already closed");
        publisher.Notifications.OfType<TicketClosed>().Should().BeEmpty();
    }

    [Fact]
    public async Task ForceClose_Rejects_Short_Reason()
    {
        await using var ctx = _fx.NewContext();
        await Truncate.AllAsync(ctx);

        var nowUtc = DateTimeOffset.Parse("2026-05-06T20:45:00+00:00");
        var ticketId = await SeedTicketAsync(ctx, nowUtc, TicketStateNames.InProgress);

        var publisher = new RecordingPublisher();
        var handler = new ForceCloseTicketHandler(ctx, publisher, new FakeTimeProvider(nowUtc));

        var result = await handler.HandleAsync(new ForceCloseTicketCommand(
            ticketId, Guid.NewGuid(), "tooshort"), default);

        result.Success.Should().BeFalse();
        result.ReasonCode.Should().Be(TicketReasonCode.ForceCloseReasonRequired);
    }

    [Fact]
    public async Task ToggleAgentAvailability_Inserts_Then_Toggles_Then_NoOps()
    {
        await using var ctx = _fx.NewContext();
        await Truncate.AllAsync(ctx);

        var nowUtc = DateTimeOffset.Parse("2026-05-06T21:00:00+00:00");
        var publisher = new RecordingPublisher();
        var handler = new ToggleAgentAvailabilityHandler(ctx, publisher, new FakeTimeProvider(nowUtc));

        var agentId = Guid.NewGuid();
        var leadId = Guid.NewGuid();

        // First toggle: insert.
        var first = await handler.HandleAsync(new ToggleAgentAvailabilityCommand(
            agentId, "SA", IsOnCall: true, leadId), default);
        first.Success.Should().BeTrue();
        first.IsOnCall.Should().BeTrue();

        // Second toggle: flip to false.
        var second = await handler.HandleAsync(new ToggleAgentAvailabilityCommand(
            agentId, "SA", IsOnCall: false, leadId), default);
        second.Success.Should().BeTrue();
        second.IsOnCall.Should().BeFalse();

        // Third toggle: re-set to false — no-op (no event, no detail).
        var third = await handler.HandleAsync(new ToggleAgentAvailabilityCommand(
            agentId, "SA", IsOnCall: false, leadId), default);
        third.Success.Should().BeTrue();
        third.Detail.Should().Contain("No-op");

        publisher.Notifications.OfType<TicketAgentAvailabilityChanged>().Should().HaveCount(2,
            "no-op call must NOT emit an event");
    }

    private static async Task<Guid> SeedTicketAsync(
        SupportDbContext ctx,
        DateTimeOffset nowUtc,
        string state,
        DateTimeOffset? closedAtUtc = null)
    {
        var ticketId = Guid.NewGuid();
        ctx.Tickets.Add(new SupportTicket
        {
            Id = ticketId,
            CustomerId = Guid.NewGuid(),
            MarketCode = "SA",
            Locale = "en",
            Category = "general_question",
            Priority = "normal",
            State = state,
            Subject = "Force-close test",
            Body = "Body",
            FirstResponseTargetMinutesSnapshot = 240,
            ResolutionTargetMinutesSnapshot = 2880,
            FirstResponseDueUtc = nowUtc.AddMinutes(240),
            ResolutionDueUtc = nowUtc.AddMinutes(2880),
            ClosedAtUtc = closedAtUtc,
            CreatedAtUtc = nowUtc.AddMinutes(-30),
            UpdatedAtUtc = nowUtc.AddMinutes(-30),
        });
        await ctx.SaveChangesAsync();
        return ticketId;
    }

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
