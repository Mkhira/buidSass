using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.B2B.Entities;
using BackendApi.Modules.B2B.Persistence;
using BackendApi.Modules.Shared;
using B2B.Tests.Contract.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace B2B.Tests.Integration;

/// <summary>
/// Spec 021 T061a — FR-045 / SC-010 rate-limit enforcement.
///
/// Two scenarios per spec 021 quickstart §4 + tasks T061a:
/// <list type="bullet">
///   <item><b>Per-customer cap</b>: a single customer firing 11 quote requests
///         within an hour gets the 11th rejected with
///         <c>429 quote.rate_limit_exceeded</c> + <c>retry_after_seconds</c>
///         in the body. Default cap from
///         <c>quote_market_schemas.rate_limit_per_customer_per_hour=10</c>.</item>
///   <item><b>Per-company cap</b>: a single company aggregating 51 quote
///         requests within an hour from N distinct buyers gets the 51st
///         rejected the same way. Default cap from
///         <c>quote_market_schemas.rate_limit_per_company_per_hour=50</c>.
///         Verifies that the per-company bucket is keyed by
///         <c>company_id</c>, not <c>customer_id</c>: no single buyer hits 10
///         requests, but the aggregate trips the company cap. The active
///         schema row sets the cap; the limiter is the
///         in-process <see cref="BackendApi.Modules.B2B.RateLimit.QuoteRequestRateLimiter"/>.</item>
/// </list>
///
/// We use real <see cref="BackendApi.Modules.B2B.RateLimit.QuoteRequestRateLimiter"/>
/// state (registered once as a singleton in <c>B2BModule</c>) to exercise the
/// production sliding-window code path. <see cref="B2BApiFactory.Time"/> is the
/// host-registered <see cref="Microsoft.Extensions.Time.Testing.FakeTimeProvider"/>
/// the limiter resolves; advancing it past one hour decays the bucket so a
/// follow-up request after the window MUST succeed (proving sliding-window vs.
/// fixed-window semantics).
///
/// Each <see cref="Fact"/> uses a fresh <see cref="Guid"/> for the customer /
/// company id so the rate limiter's per-key bucket is empty regardless of test
/// ordering. <see cref="B2BApiFactory.ResetStubState"/> rewinds the clock to
/// the fixture baseline and clears stub state but does NOT clear the rate
/// limiter (singleton, not exposed via the factory). Per-test fresh ids are
/// the cleanest way to avoid bleed between tests.
/// </summary>
public sealed class RateLimitEnforcementTests : IClassFixture<B2BApiFactory>
{
    private const string Route = "/api/customer/quotes/from-cart";
    private readonly B2BApiFactory _factory;

    public RateLimitEnforcementTests(B2BApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Per_customer_cap_eleventh_request_within_an_hour_returns_429()
    {
        _factory.ResetStubState();

        var customerId = Guid.NewGuid();
        var client = _factory.CreateClient();
        AuthenticateAs(client, customerId, market: "ksa");

        // Default per-customer cap is 10/hour (B2BReferenceDataSeeder seeds KSA v1
        // with rate_limit_per_customer_per_hour=10). The 11th request inside the
        // sliding window MUST trip 429.
        for (var i = 0; i < 10; i++)
        {
            SeedCart(customerId);
            using var idemReq = NewIdempotentRequest(client);
            using var resp = await client.PostAsJsonAsync(Route, new { });

            // Every request 1..10 is allowed by the limiter; downstream checks may
            // surface 201 / 400 / 422 depending on the surface state. We DO NOT
            // assert success vs failure here — only that the request was admitted
            // (i.e. the limiter did not return 429).
            resp.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
                $"request #{i + 1} of 10 should be inside the per-customer cap");
        }

        // Eleventh request — bucket is full; limiter MUST reject before any
        // handler logic touches the cart.
        SeedCart(customerId);
        using (NewIdempotentRequest(client))
        {
            using var rejected = await client.PostAsJsonAsync(Route, new { });
            rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

            var body = await rejected.Content.ReadFromJsonAsync<JsonElement>();
            body.TryGetProperty("reasonCode", out var reasonCode).Should().BeTrue();
            reasonCode.GetString().Should().Be("quote.rate_limit_exceeded");

            body.TryGetProperty("retry_after_seconds", out var retryAfter).Should().BeTrue(
                "FR-045 requires retry_after_seconds in the 429 body");
            retryAfter.GetInt32().Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public async Task Per_customer_window_releases_after_one_hour_advance()
    {
        _factory.ResetStubState();

        var customerId = Guid.NewGuid();
        var client = _factory.CreateClient();
        AuthenticateAs(client, customerId, market: "ksa");

        // Fill the bucket: 10 successful admits inside the same window.
        for (var i = 0; i < 10; i++)
        {
            SeedCart(customerId);
            using (NewIdempotentRequest(client))
            {
                using var fillResp = await client.PostAsJsonAsync(Route, new { });
            }
        }

        // 11th immediately tripped:
        SeedCart(customerId);
        using (NewIdempotentRequest(client))
        {
            using var blocked = await client.PostAsJsonAsync(Route, new { });
            blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        }

        // Advance the fake clock past the 1-hour window so the limiter trims
        // every prior timestamp; the next request MUST succeed past the limiter.
        _factory.Time.Advance(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(1));

        SeedCart(customerId);
        using (NewIdempotentRequest(client))
        {
            using var afterWindow = await client.PostAsJsonAsync(Route, new { });
            afterWindow.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
                "the sliding-window limiter MUST decay entries older than the configured window (1h)");
        }
    }

    [Fact]
    public async Task Per_company_cap_aggregates_across_distinct_buyers()
    {
        _factory.ResetStubState();

        // Seed a company + several buyer memberships so 51 cross-buyer requests
        // can hit the per-company bucket without any single buyer breaching the
        // per-customer cap (= 10).
        var companyId = Guid.NewGuid();
        var buyers = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid()).ToArray();

        await SeedCompanyAndMembershipsAsync(companyId, buyers);

        // Each buyer makes ~7 requests; that's 8 * 7 = 56 (well under 10/buyer
        // and over 50/company). The 51st aggregate MUST trip the company bucket.
        var attemptCount = 0;
        HttpStatusCode? rateLimitedAt = null;
        foreach (var buyerId in buyers)
        {
            var client = _factory.CreateClient();
            AuthenticateAs(client, buyerId, market: "ksa");

            for (var j = 0; j < 7; j++)
            {
                attemptCount++;
                SeedCart(buyerId);
                using (NewIdempotentRequest(client))
                {
                    using var resp = await client.PostAsJsonAsync(Route, new
                    {
                        company_id = companyId.ToString(),
                    });

                    if (resp.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        // First 429 inside the company bucket — capture the
                        // attempt index and assert the wire shape, then exit
                        // both loops.
                        rateLimitedAt = resp.StatusCode;
                        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
                        body.TryGetProperty("reasonCode", out var reasonCode).Should().BeTrue();
                        reasonCode.GetString().Should().Be("quote.rate_limit_exceeded");
                        body.TryGetProperty("retry_after_seconds", out var retryAfter).Should().BeTrue(
                            "FR-045 requires retry_after_seconds in the 429 body");
                        retryAfter.GetInt32().Should().BeGreaterThan(0,
                            "retry_after_seconds MUST be a positive hint — a zero/negative "
                            + "value would be a useless retry guidance and silently regresses FR-045");
                        goto done;
                    }
                }
            }
        }
done:

        rateLimitedAt.Should().Be(HttpStatusCode.TooManyRequests,
            "the per-company bucket (cap 50/hour) MUST trip before the cross-buyer total reaches 56");

        // `attemptCount` is incremented BEFORE the request is dispatched, so
        // when the breach lands the counter has already counted the rejected
        // request. The strict invariant is therefore `>= 51`: at least 50
        // admits MUST have preceded the rejection, and the rejection itself
        // is the 51st (or later) attempt. Customer-bucket refunds may push
        // the breach further (52nd, 53rd, ...), so the lower-bound form
        // matches the in-process limiter's tolerance. (CodeRabbit Round 1 —
        // off-by-one fix to make the assertion truly load-bearing.)
        attemptCount.Should().BeGreaterOrEqualTo(51,
            "company-bucket cap is 50/hour — the breach MUST come at or after "
            + "the 51st overall attempt (50 admits + the rejected request)");
    }

    private void SeedCart(Guid customerId)
    {
        // Cart snapshot is single-shot — re-seed before every request so the
        // request reaches the rate-limit gate (rather than short-circuiting at
        // cart_empty).
        _factory.CartSnapshotProvider.SnapshotsByCustomer[customerId] = new[]
        {
            new CartSnapshotLine(Sku: "RATE-LIMIT-TEST-SKU", Quantity: 1, LineNote: null),
        };
    }

    private async Task SeedCompanyAndMembershipsAsync(Guid companyId, IReadOnlyList<Guid> buyerIds)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<B2BDbContext>();

        db.Companies.Add(new Company
        {
            Id = companyId,
            NameJson = """{"en":"Test Co.","ar":"شركة الاختبار"}""",
            TaxId = $"TX-{Guid.NewGuid():N}",
            MarketCode = "ksa",
            PrimaryAddressJson = "{}",
            BillingAddressJson = null,
            ApproverRequired = false,
            PoRequired = false,
            UniquePoRequired = false,
            InvoiceBillingEligible = true,
            State = "active",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        foreach (var buyerId in buyerIds)
        {
            db.CompanyMemberships.Add(new CompanyMembership
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                MarketCode = "ksa",
                UserId = buyerId,
                Role = "buyer",
                JoinedAt = DateTimeOffset.UtcNow,
            });
        }

        await db.SaveChangesAsync();
    }

    private static void AuthenticateAs(HttpClient client, Guid customerId, string market)
    {
        client.DefaultRequestHeaders.Remove("X-Test-Customer-Id");
        client.DefaultRequestHeaders.Remove("X-Test-Market");
        client.DefaultRequestHeaders.Add("X-Test-Customer-Id", customerId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Market", market);
    }

    private static IDisposable NewIdempotentRequest(HttpClient client)
    {
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return new HeaderScope(client, "Idempotency-Key");
    }

    private sealed class HeaderScope : IDisposable
    {
        private readonly HttpClient _client;
        private readonly string _header;
        public HeaderScope(HttpClient client, string header)
        {
            _client = client;
            _header = header;
        }
        public void Dispose() => _client.DefaultRequestHeaders.Remove(_header);
    }
}
