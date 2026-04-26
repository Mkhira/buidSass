using BackendApi.Modules.TaxInvoices.Persistence;
using BackendApi.Modules.TaxInvoices.Primitives;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace TaxInvoices.Tests.Integration;

/// <summary>SC-003 — invoice-number uniqueness fuzz against a real Postgres testcontainer.
/// 1000 concurrent NextAsync calls across two markets in the same yyyymm produce zero
/// collisions and a dense [1..N] sequence per market.</summary>
public sealed class InvoiceNumberCollisionTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("inv_seq_test")
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

    private InvoicesDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<InvoicesDbContext>().UseNpgsql(ConnectionString).Options;
        return new InvoicesDbContext(options);
    }

    [Fact]
    public async Task ConcurrentNextAsync_ProducesUniqueNumbersAcrossMarkets()
    {
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
                    return await new InvoiceNumberSequencer(ctx).NextAsync(market, instant, CancellationToken.None);
                }));
            }
        }
        var numbers = await Task.WhenAll(tasks);
        numbers.Should().OnlyHaveUniqueItems();
        numbers.Should().AllSatisfy(n => n.Should().MatchRegex("^INV-(KSA|EG)-202604-\\d{6}$"));

        foreach (var market in markets)
        {
            var marketNumbers = numbers.Where(n => n.StartsWith($"INV-{market}-", StringComparison.Ordinal)).ToList();
            marketNumbers.Should().HaveCount(callsPerMarket);
            var seqs = marketNumbers
                .Select(n => int.Parse(n.AsSpan(n.LastIndexOf('-') + 1), System.Globalization.CultureInfo.InvariantCulture))
                .OrderBy(x => x).ToList();
            seqs.First().Should().Be(1);
            seqs.Last().Should().Be(callsPerMarket);
        }
    }
}
