using BackendApi.Modules.Support.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Support.Tests.Integration;

/// <summary>
/// Shared truncation helper for the Testcontainers Postgres-backed integration
/// tests. Truncates every <c>support.*</c> mutable table (NOT the seeded
/// reference-data tables) between tests so each test has a clean slate
/// without paying migration cost.
/// </summary>
internal static class Truncate
{
    public static Task AllAsync(SupportDbContext ctx) =>
        ctx.Database.ExecuteSqlRawAsync(@"
            TRUNCATE TABLE
                support.ticket_sla_breach_events,
                support.ticket_messages,
                support.ticket_attachments,
                support.ticket_links,
                support.ticket_assignments,
                support.tickets
            CASCADE");
}
