using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Support.Tests.Infrastructure;

namespace Support.Tests.Integration;

/// <summary>
/// T051 — verifies the migration creates all 9 tables in the <c>support</c>
/// schema and that the <c>UX_ticket_assignments_active_per_ticket</c> partial
/// unique index actually rejects a second active-assignment insert.
/// </summary>
[Collection(nameof(SupportPostgresCollection))]
public sealed class SupportSchemaSmokeTests
{
    private readonly SupportPostgresFixture _fx;

    public SupportSchemaSmokeTests(SupportPostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task Migration_Creates_All_Nine_Tables()
    {
        await using var ctx = _fx.NewContext();
        await using var conn = (NpgsqlConnection)ctx.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            @"SELECT table_name FROM information_schema.tables
              WHERE table_schema = 'support' ORDER BY table_name", conn);

        var tables = new List<string>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }

        tables.Should().Contain(new[]
        {
            "tickets", "ticket_messages", "ticket_attachments", "ticket_links",
            "ticket_assignments", "ticket_sla_breach_events",
            "sla_policies", "support_market_schemas", "agent_availability",
        });
    }

    [Fact]
    public async Task Active_Assignment_Per_Ticket_Is_Unique()
    {
        await using var ctx = _fx.NewContext();
        var ticketId = Guid.NewGuid();
        var marketCode = "SA";
        var nowUtc = DateTimeOffset.UtcNow;

        ctx.Tickets.Add(new BackendApi.Modules.Support.Entities.SupportTicket
        {
            Id = ticketId,
            CustomerId = Guid.NewGuid(),
            MarketCode = marketCode,
            Locale = "en",
            Category = "general_question",
            Priority = "normal",
            State = "open",
            Subject = "Smoke",
            Body = "Smoke body",
            FirstResponseTargetMinutesSnapshot = 240,
            ResolutionTargetMinutesSnapshot = 2880,
            FirstResponseDueUtc = nowUtc.AddMinutes(240),
            ResolutionDueUtc = nowUtc.AddMinutes(2880),
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        });

        ctx.Assignments.Add(new BackendApi.Modules.Support.Entities.TicketAssignment
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            AgentId = Guid.NewGuid(),
            AssignmentKind = "self_claim",
            AssignedAtUtc = nowUtc,
        });

        await ctx.SaveChangesAsync();

        ctx.Assignments.Add(new BackendApi.Modules.Support.Entities.TicketAssignment
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            AgentId = Guid.NewGuid(),
            AssignmentKind = "lead_reassignment",
            JustificationNote = "Reassign without superseding the prior — should fail",
            AssignedAtUtc = nowUtc.AddSeconds(1),
        });

        var act = async () => await ctx.SaveChangesAsync();
        var ex = await act.Should().ThrowAsync<DbUpdateException>();
        ex.Which.InnerException.Should().BeOfType<PostgresException>();
        ((PostgresException)ex.Which.InnerException!).SqlState.Should().Be("23505");
    }
}
