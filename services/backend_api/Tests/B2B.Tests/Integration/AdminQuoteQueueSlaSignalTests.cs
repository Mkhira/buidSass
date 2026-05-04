using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.B2B.Entities;
using BackendApi.Modules.B2B.Persistence;
using B2B.Tests.Contract.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace B2B.Tests.Integration;

/// <summary>
/// Spec 021 T084c — admin queue (<c>GET /api/admin/quotes</c>, contract §4.1)
/// MUST tag every <c>requested</c> row with an <c>sla_signal</c> derived from
/// the row's age in business days against the active
/// <see cref="QuoteMarketSchema"/>'s thresholds:
/// <list type="bullet">
///   <item><c>ok</c> when <c>age_business_days &lt; sla_warning_business_days</c></item>
///   <item><c>warning</c> when
///         <c>sla_warning_business_days ≤ age_business_days &lt; sla_decision_business_days</c></item>
///   <item><c>breach</c> when <c>age_business_days ≥ sla_decision_business_days</c></item>
/// </list>
/// Defaults from the seeded KSA schema: <c>sla_warning_business_days=1</c>,
/// <c>sla_decision_business_days=2</c> (FR-014 + data-model §2.9). The clock
/// is wall-clock from <c>requested_at</c> with NO pause states.
///
/// Approach: seed three <c>requested</c> quotes at distinct
/// <c>requested_at</c> offsets relative to the harness <c>FakeTimeProvider</c>
/// (0 / 1 / 3 business days old) and assert each row's <c>sla_signal</c>
/// against the corresponding cohort. The fake clock means no real-time waits.
///
/// <b>Cycle scoping</b>: T085 (ListQuoteQueue handler) lands in Cycle B.
/// While the route is unmapped (404), this test pins the eventual contract
/// using <c>BeOneOf(404, ExpectedCode)</c>. The seeding pattern + cohort
/// mapping is the durable contract — once T085 lands and the assertions run,
/// the cohort-to-signal mapping is a regression-grade invariant.
/// </summary>
public sealed class AdminQuoteQueueSlaSignalTests : IClassFixture<B2BApiFactory>
{
    private readonly B2BApiFactory _factory;

    public AdminQuoteQueueSlaSignalTests(B2BApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Three_age_cohorts_map_to_ok_warning_and_breach_signals()
    {
        _factory.ResetStubState();

        // Pin the fake clock to a deterministic weekday. The KSA / EG working
        // week is Sun–Thu (BusinessDayCalculator.WeekendDays = { Fri, Sat }),
        // so anchoring "now" on a Wednesday means the -1d / -3d cohorts both
        // land on business days regardless of when CI runs:
        //   • now      = Wed 12:00 UTC          → business day  (cohort: ok,    age=0)
        //   • -1d -1m  = Tue ~12:00 UTC         → business day  (cohort: warn,  age=1)
        //   • -3d      = Sun 12:00 UTC          → business day  (cohort: breach,age=3)
        // Without this pin, a CI run on a Sunday would put -1d on Saturday
        // (weekend) and the warning cohort's age_business_days would collapse
        // to 0, flipping the signal to ok and breaking the test.
        var anchorWednesday = new DateTimeOffset(2026, 5, 6, 12, 0, 0, TimeSpan.Zero);
        if (anchorWednesday.DayOfWeek != DayOfWeek.Wednesday)
        {
            throw new InvalidOperationException(
                "Test invariant: anchor must be a Wednesday so -1d / -3d are weekdays.");
        }
        _factory.Time.SetUtcNow(anchorWednesday);
        var now = _factory.Time.GetUtcNow();

        // Three cohorts. With the Sun–Thu workweek and an empty holidays list,
        // each AddDays(-N) below resolves to a business day (verified above).
        var freshQuoteId = await SeedRequestedQuoteAsync(
            requestedAt: now);                                 // 0 business days → ok
        var warningQuoteId = await SeedRequestedQuoteAsync(
            requestedAt: now.AddDays(-1).AddMinutes(-1));      // ≥1 < 2 → warning
        var breachQuoteId = await SeedRequestedQuoteAsync(
            requestedAt: now.AddDays(-3));                     // ≥2 → breach

        var client = _factory.CreateClient();
        AuthenticateAsAdmin(client, market: "ksa", permissions: "quotes.review");

        // Default page size is large enough to return all three on one page.
        var resp = await client.GetAsync("/api/admin/quotes?page_size=100");

        resp.StatusCode.Should().BeOneOf(
            HttpStatusCode.NotFound,    // T085 not yet mapped (Cycle A)
            HttpStatusCode.OK);         // T085 wired (Cycle B+)

        if (resp.StatusCode == HttpStatusCode.OK)
        {
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            body.TryGetProperty("items", out var items).Should().BeTrue();

            // Build an index by id for assertion clarity.
            var byId = items.EnumerateArray()
                .Where(row => row.TryGetProperty("id", out _))
                .ToDictionary(
                    row => Guid.Parse(row.GetProperty("id").GetString()!),
                    row => row);

            byId.Should().ContainKey(freshQuoteId);
            byId.Should().ContainKey(warningQuoteId);
            byId.Should().ContainKey(breachQuoteId);

            byId[freshQuoteId].GetProperty("sla_signal").GetString()
                .Should().Be("ok",
                    "age_business_days=0 < sla_warning_business_days=1 → ok");
            byId[warningQuoteId].GetProperty("sla_signal").GetString()
                .Should().Be("warning",
                    "1 ≤ age_business_days < 2 → warning");
            byId[breachQuoteId].GetProperty("sla_signal").GetString()
                .Should().Be("breach",
                    "age_business_days ≥ sla_decision_business_days=2 → breach");

            // Fake-clock is pinned to a Wednesday with cohorts seeded at
            // -0d / -1d / -3d. With KSA Sun-Thu workweek and the pinned
            // anchor, every offset lands on a business day, so the exact
            // business-day ages are deterministic: 0 / 1 / 3.
            byId[freshQuoteId].GetProperty("age_business_days").GetInt32()
                .Should().Be(0);
            byId[warningQuoteId].GetProperty("age_business_days").GetInt32()
                .Should().Be(1);
            byId[breachQuoteId].GetProperty("age_business_days").GetInt32()
                .Should().Be(3);
        }
    }

    private async Task<Guid> SeedRequestedQuoteAsync(DateTimeOffset requestedAt)
    {
        var quoteId = Guid.NewGuid();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<B2BDbContext>();

        db.Quotes.Add(new Quote
        {
            Id = quoteId,
            CustomerId = Guid.NewGuid(),
            CompanyId = null,
            BranchId = null,
            MarketCode = "ksa",
            State = "requested",
            RequestedAt = requestedAt,
            ExpiresAt = null,
            CurrentVersionId = null,
            DecidedAt = null,
            DecidedBy = null,
            TerminalAt = null,
            TerminalReason = null,
            PoNumber = null,
            InvoiceBilling = false,
            CustomerSuppliedMessageJson = null,
            InternalNote = null,
            ApproverRejectionNote = null,
            OriginatingCartSnapshotJson = "[]",
            OriginatingProductId = null,
            RestrictionPolicySnapshotJson = "{}",
            SchemaVersion = 1,
        });

        await db.SaveChangesAsync();
        return quoteId;
    }

    private static void AuthenticateAsAdmin(
        HttpClient client,
        string market,
        string? permissions = null)
    {
        client.DefaultRequestHeaders.Add("X-Test-Admin-Id", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("X-Test-Market", market);
        if (!string.IsNullOrWhiteSpace(permissions))
        {
            client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        }
    }
}
