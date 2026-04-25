using BackendApi.Modules.Orders.Persistence;
using BackendApi.Modules.Orders.Primitives;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Orders.Tests.Integration;

/// <summary>
/// SC-002 — order-number uniqueness fuzz. 1k concurrent <see cref="OrderNumberSequencer.NextAsync"/>
/// calls across two markets in the same yyyymm must produce zero collisions; the per-(market,
/// yyyymm) Postgres sequence is the source of truth.
///
/// The full SC-002 envelope is 10k concurrent — 1k here proves the lock + sequence pattern
/// while keeping the testcontainer test under a few seconds for routine CI.
/// </summary>
public sealed class OrderNumberSequencerCollisionTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("orders_seq_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCommand("-c", "max_connections=300")
        .WithCleanUp(true)
        .Build();

    private string ConnectionString => $"{_postgres.GetConnectionString()};Maximum Pool Size=300";

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var ctx = NewContext();
        await ctx.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private OrdersDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<OrdersDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new OrdersDbContext(options);
    }

    [Fact]
    public async Task ConcurrentNextAsync_ProducesUniqueNumbersAcrossMarkets()
    {
        // Use a fixed instant so all calls hit the same per-(market, yyyymm) sequence.
        var instant = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);

        const int callsPerMarket = 500;
        var markets = new[] { "KSA", "EG" };

        var tasks = new List<Task<string>>();
        foreach (var market in markets)
        {
            for (var i = 0; i < callsPerMarket; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await using var ctx = NewContext();
                    var sequencer = new OrderNumberSequencer(ctx);
                    return await sequencer.NextAsync(market, instant, CancellationToken.None);
                }));
            }
        }

        var numbers = await Task.WhenAll(tasks);

        // No collisions across the entire run.
        numbers.Should().OnlyHaveUniqueItems();
        numbers.Should().AllSatisfy(n => n.Should().MatchRegex("^ORD-(KSA|EG)-202604-\\d{6}$"));

        // Each market's sequence is dense [1..callsPerMarket].
        foreach (var market in markets)
        {
            var marketNumbers = numbers.Where(n => n.StartsWith($"ORD-{market}-", StringComparison.Ordinal)).ToList();
            marketNumbers.Should().HaveCount(callsPerMarket);
            var seqs = marketNumbers
                .Select(n => int.Parse(n.AsSpan(n.LastIndexOf('-') + 1), System.Globalization.CultureInfo.InvariantCulture))
                .OrderBy(x => x)
                .ToList();
            seqs.First().Should().Be(1);
            seqs.Last().Should().Be(callsPerMarket);
        }
    }

    [Fact]
    public async Task NextAsync_DifferentMonth_GetsFreshSequence()
    {
        var apr = new DateTimeOffset(2026, 4, 15, 10, 0, 0, TimeSpan.Zero);
        var may = new DateTimeOffset(2026, 5, 15, 10, 0, 0, TimeSpan.Zero);

        await using var ctx = NewContext();
        var sequencer = new OrderNumberSequencer(ctx);

        var aprFirst = await sequencer.NextAsync("KSA", apr, CancellationToken.None);
        var mayFirst = await sequencer.NextAsync("KSA", may, CancellationToken.None);

        aprFirst.Should().EndWith("000001"); // fresh April sequence
        mayFirst.Should().EndWith("000001"); // fresh May sequence (separate)
        aprFirst.Should().Contain("202604");
        mayFirst.Should().Contain("202605");
    }
}
