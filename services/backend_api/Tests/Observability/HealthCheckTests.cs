using BackendApi.Modules.Observability.HealthChecks;
using BackendApi.Modules.Shared;
using backend_api.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;

namespace backend_api.Tests.Observability;

public sealed class HealthCheckTests
{
    [Fact]
    public async Task DbConnectivityCheck_Returns_Healthy_When_Db_Reachable()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        var check = new DbConnectivityCheck(db, new DbConnectivityProbeGate());

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var result = await check.CheckHealthAsync(new HealthCheckContext(), cts.Token);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task StorageReachabilityCheck_Returns_Healthy_Within_500ms()
    {
        var check = new StorageReachabilityCheck();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var result = await check.CheckHealthAsync(new HealthCheckContext(), cts.Token);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task DbConnectivityCheck_Returns_Unhealthy_When_Db_Is_Unreachable()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=missing;Username=missing;Password=missing;Timeout=1;Command Timeout=1")
            .Options;

        await using var db = new AppDbContext(options);
        var check = new DbConnectivityCheck(db, new DbConnectivityProbeGate());

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var result = await check.CheckHealthAsync(new HealthCheckContext(), cts.Token);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }
}

[Collection("PostgresCollection")]
public sealed class HealthEndpointIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public HealthEndpointIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HealthEndpoint_WhenDatabaseRunning_Returns200_Within500ms()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new BackendApiFactory(_fixture.ConnectionString);
        using var client = factory.CreateClient();

        var stopwatch = Stopwatch.StartNew();
        var response = await client.GetAsync("/health");
        stopwatch.Stop();

        Assert.Equal(200, (int)response.StatusCode);
        Assert.True(stopwatch.ElapsedMilliseconds < 500, $"Expected <500ms, got {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task HealthEndpoint_WhenDatabasePaused_Returns503_Within500ms()
    {
        await _fixture.ResetDatabaseAsync();
        await _fixture.PauseAsync();

        try
        {
            await using var factory = new BackendApiFactory(_fixture.ConnectionString);
            using var client = factory.CreateClient();

            var stopwatch = Stopwatch.StartNew();
            var response = await client.GetAsync("/health");
            stopwatch.Stop();

            Assert.Equal(503, (int)response.StatusCode);
            Assert.True(stopwatch.ElapsedMilliseconds < 500, $"Expected <500ms, got {stopwatch.ElapsedMilliseconds}ms");
        }
        finally
        {
            await _fixture.UnpauseAsync();
        }
    }

    private sealed class BackendApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = connectionString,
                });
            });

            return base.CreateHost(builder);
        }
    }
}
