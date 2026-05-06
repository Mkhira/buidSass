using BackendApi.Features.Seeding;
using BackendApi.Features.Seeding.Datasets;
using BackendApi.Modules.B2B.Persistence;
using BackendApi.Modules.B2B.Seeding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace B2B.Tests.Integration;

/// <summary>
/// Spec 021 task T145 — verifies the dev seeder is dev-gated, populates every
/// state-machine surface (companies / memberships / invitations / quotes /
/// templates), and is idempotent across re-runs.
/// </summary>
public sealed class B2BDevDataSeederTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("b2b_dev_seeder")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCleanUp(true)
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var ctx = NewContext();
        await ctx.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task Seeds_full_surface_in_development()
    {
        await RunDevSeederAsync(envName: Environments.Development);

        await using var ctx = NewContext();
        (await ctx.Companies.CountAsync()).Should().Be(3);
        (await ctx.CompanyBranches.CountAsync()).Should().Be(2);
        (await ctx.CompanyMemberships.CountAsync()).Should().Be(6);
        (await ctx.CompanyInvitations.CountAsync()).Should().Be(4);
        (await ctx.Quotes.CountAsync()).Should().Be(8);
        (await ctx.RepeatOrderTemplates.CountAsync()).Should().Be(2);

        var quoteStates = await ctx.Quotes.Select(q => q.State).ToListAsync();
        quoteStates.Should().BeEquivalentTo(new[]
        {
            "requested", "drafted", "revised", "pending-approver",
            "accepted", "rejected", "expired", "withdrawn",
        });

        var invitationStates = await ctx.CompanyInvitations.Select(i => i.State).ToListAsync();
        invitationStates.Should().BeEquivalentTo(new[] { "pending", "accepted", "declined", "expired" });
    }

    [Fact]
    public async Task Idempotent_across_repeated_runs()
    {
        await RunDevSeederAsync(envName: Environments.Development);
        await RunDevSeederAsync(envName: Environments.Development);
        await RunDevSeederAsync(envName: Environments.Development);

        await using var ctx = NewContext();
        (await ctx.Companies.CountAsync()).Should().Be(3, "re-runs MUST converge");
        (await ctx.Quotes.CountAsync()).Should().Be(8, "re-runs MUST converge");
    }

    [Fact]
    public async Task Skips_non_development_environments()
    {
        await RunDevSeederAsync(envName: Environments.Production);

        await using var ctx = NewContext();
        (await ctx.Companies.CountAsync()).Should().Be(0,
            "non-Development envs MUST short-circuit the dev seeder");
    }

    private B2BDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<B2BDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new B2BDbContext(options);
    }

    private async Task RunDevSeederAsync(string envName)
    {
        var services = new ServiceCollection();
        services.AddDbContext<B2BDbContext>(o => o.UseNpgsql(_postgres.GetConnectionString()));
        await using var sp = services.BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();

        var seeder = new B2BDevDataSeeder();
        var ctx = new SeedContext(
            Db: null!,
            Services: scope.ServiceProvider,
            Size: DatasetSize.Small,
            Env: new TestEnv { EnvironmentName = envName },
            Logger: NullLogger.Instance);
        await seeder.ApplyAsync(ctx, CancellationToken.None);
    }

    private sealed class TestEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "B2B.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
