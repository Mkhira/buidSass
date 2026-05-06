using BackendApi.Modules.Shared;
using BackendApi.Modules.Support.Entities;
using BackendApi.Modules.Support.Persistence;
using BackendApi.Modules.Support.Primitives;
using BackendApi.Modules.Support.Workers;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Support.Tests.Infrastructure;

namespace Support.Tests.Integration;

/// <summary>
/// Spec 023 US4 (T084) — integration tests for the SLA breach watch worker
/// over Testcontainers Postgres. Covers SC-005 (breach detected within 60s
/// of deadline) and SC-006 (idempotency on re-tick).
/// </summary>
[Collection(nameof(SupportPostgresCollection))]
public sealed class SlaBreachWatchWorkerTests
{
    private readonly SupportPostgresFixture _fx;

    public SlaBreachWatchWorkerTests(SupportPostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task Worker_Detects_FirstResponse_Breach_And_Stamps_Acknowledgment()
    {
        await using var ctx = _fx.NewContext();
        await TruncateAsync(ctx);

        var nowUtc = DateTimeOffset.Parse("2026-05-06T12:00:00+00:00");
        var ticket = await SeedTicketAsync(ctx, nowUtc.AddMinutes(-10), firstResponseTargetMinutes: 5);

        var collector = new TestPublisher();
        var worker = BuildWorker(collector, nowUtc);

        await worker.ExecuteOnceAsync(default);

        await using var verify = _fx.NewContext();
        var breaches = await verify.SlaBreachEvents
            .Where(e => e.TicketId == ticket.Id)
            .ToListAsync();

        breaches.Should().HaveCount(1);
        breaches.Single().BreachKind.Should().Be(TicketSlaBreachKind.FirstResponse);

        var refreshed = await verify.Tickets.AsNoTracking()
            .FirstAsync(t => t.Id == ticket.Id);
        refreshed.BreachAcknowledgedAtFirstResponse.Should().NotBeNull();
        refreshed.BreachAcknowledgedAtResolution.Should().BeNull();

        collector.Notifications.OfType<TicketSlaBreachedFirstResponse>()
            .Should().HaveCount(1)
            .And.OnlyContain(n => n.TicketId == ticket.Id);
    }

    [Fact]
    public async Task Worker_Is_Idempotent_On_Re_Tick()
    {
        await using var ctx = _fx.NewContext();
        await TruncateAsync(ctx);

        var nowUtc = DateTimeOffset.Parse("2026-05-06T12:30:00+00:00");
        var ticket = await SeedTicketAsync(ctx, nowUtc.AddMinutes(-30), firstResponseTargetMinutes: 5);

        var collector = new TestPublisher();
        var worker = BuildWorker(collector, nowUtc);

        await worker.ExecuteOnceAsync(default);
        await worker.ExecuteOnceAsync(default);
        await worker.ExecuteOnceAsync(default);

        await using var verify = _fx.NewContext();
        var breachCount = await verify.SlaBreachEvents
            .CountAsync(e => e.TicketId == ticket.Id
                          && e.BreachKind == TicketSlaBreachKind.FirstResponse);

        breachCount.Should().Be(1, "the worker must be idempotent across re-ticks");
        collector.Notifications.OfType<TicketSlaBreachedFirstResponse>()
            .Should().HaveCount(1);
    }

    [Fact]
    public async Task Worker_Does_Not_Breach_When_Deadline_In_Future()
    {
        await using var ctx = _fx.NewContext();
        await TruncateAsync(ctx);

        var nowUtc = DateTimeOffset.Parse("2026-05-06T13:00:00+00:00");
        var ticket = await SeedTicketAsync(ctx, nowUtc, firstResponseTargetMinutes: 240);

        var collector = new TestPublisher();
        var worker = BuildWorker(collector, nowUtc);

        await worker.ExecuteOnceAsync(default);

        await using var verify = _fx.NewContext();
        (await verify.SlaBreachEvents.CountAsync(e => e.TicketId == ticket.Id))
            .Should().Be(0);
        (await verify.Tickets.AsNoTracking().FirstAsync(t => t.Id == ticket.Id))
            .BreachAcknowledgedAtFirstResponse.Should().BeNull();
    }

    [Fact]
    public async Task Worker_Detects_Resolution_Breach_Independently()
    {
        await using var ctx = _fx.NewContext();
        await TruncateAsync(ctx);

        var nowUtc = DateTimeOffset.Parse("2026-05-06T14:00:00+00:00");
        // Both deadlines elapsed; expect both breach kinds.
        var ticket = await SeedTicketAsync(ctx, nowUtc.AddDays(-3),
            firstResponseTargetMinutes: 5, resolutionTargetMinutes: 60);

        var collector = new TestPublisher();
        var worker = BuildWorker(collector, nowUtc);

        await worker.ExecuteOnceAsync(default);

        await using var verify = _fx.NewContext();
        var breaches = await verify.SlaBreachEvents
            .Where(e => e.TicketId == ticket.Id)
            .ToListAsync();

        breaches.Should().HaveCount(2);
        breaches.Select(b => b.BreachKind).Should().Contain(new[]
        {
            TicketSlaBreachKind.FirstResponse,
            TicketSlaBreachKind.Resolution,
        });

        collector.Notifications.OfType<TicketSlaBreachedFirstResponse>().Should().HaveCount(1);
        collector.Notifications.OfType<TicketSlaBreachedResolution>().Should().HaveCount(1);
    }

    [Fact]
    public async Task Worker_Skips_Closed_Tickets()
    {
        await using var ctx = _fx.NewContext();
        await TruncateAsync(ctx);

        var nowUtc = DateTimeOffset.Parse("2026-05-06T15:00:00+00:00");
        var ticket = await SeedTicketAsync(ctx, nowUtc.AddHours(-2),
            firstResponseTargetMinutes: 5, state: TicketStateNames.Closed);

        var collector = new TestPublisher();
        var worker = BuildWorker(collector, nowUtc);

        await worker.ExecuteOnceAsync(default);

        await using var verify = _fx.NewContext();
        (await verify.SlaBreachEvents.CountAsync(e => e.TicketId == ticket.Id))
            .Should().Be(0);
    }

    private SlaBreachWatchWorker BuildWorker(TestPublisher publisher, DateTimeOffset nowUtc)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(nowUtc));
        services.AddSingleton<IPublisher>(publisher);
        services.AddSingleton(_fx);
        services.AddScoped(_ => _fx.NewContext());
        var sp = services.BuildServiceProvider();

        var options = Options.Create(new SupportWorkerOptions());
        return new SlaBreachWatchWorker(
            sp.GetRequiredService<IServiceScopeFactory>(),
            options,
            sp.GetRequiredService<TimeProvider>(),
            NullLogger<SlaBreachWatchWorker>.Instance);
    }

    private static async Task<SupportTicket> SeedTicketAsync(
        SupportDbContext ctx,
        DateTimeOffset createdAt,
        int firstResponseTargetMinutes,
        int resolutionTargetMinutes = 2880,
        string state = TicketStateNames.Open)
    {
        var ticket = new SupportTicket
        {
            Id = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            MarketCode = "SA",
            Locale = "en",
            Category = "general_question",
            Priority = "normal",
            State = state,
            Subject = "SLA breach test",
            Body = "Body",
            FirstResponseTargetMinutesSnapshot = firstResponseTargetMinutes,
            ResolutionTargetMinutesSnapshot = resolutionTargetMinutes,
            FirstResponseDueUtc = createdAt.AddMinutes(firstResponseTargetMinutes),
            ResolutionDueUtc = createdAt.AddMinutes(resolutionTargetMinutes),
            CreatedAtUtc = createdAt,
            UpdatedAtUtc = createdAt,
        };
        ctx.Tickets.Add(ticket);
        await ctx.SaveChangesAsync();
        return ticket;
    }

    private static async Task TruncateAsync(SupportDbContext ctx)
    {
        await ctx.Database.ExecuteSqlRawAsync(@"
            TRUNCATE TABLE
                support.ticket_sla_breach_events,
                support.ticket_messages,
                support.ticket_attachments,
                support.ticket_links,
                support.ticket_assignments,
                support.tickets
            CASCADE");
    }

    private sealed class TestPublisher : IPublisher
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
