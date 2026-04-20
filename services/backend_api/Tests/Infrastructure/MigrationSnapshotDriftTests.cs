using BackendApi.Modules.Shared;
using Microsoft.EntityFrameworkCore;

namespace backend_api.Tests.Infrastructure;

[Collection("PostgresCollection")]
public sealed class MigrationSnapshotDriftTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Applied_Migrations_Match_Compiled_Snapshot()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;

        await using var db = new AppDbContext(options);

        // Fixture already ran MigrateAsync on InitializeAsync; assert no drift.
        var pending = await db.Database.GetPendingMigrationsAsync();
        Assert.Empty(pending);
    }
}
