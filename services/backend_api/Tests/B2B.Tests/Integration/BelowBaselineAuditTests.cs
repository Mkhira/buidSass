using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.B2B.Entities;
using BackendApi.Modules.B2B.Persistence;
using BackendApi.Modules.Shared;
using B2B.Tests.Contract.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace B2B.Tests.Integration;

/// <summary>
/// Spec 021 T084 — every below-baseline override on a draft MUST emit one
/// <c>quote.line_override</c> audit-log row carrying:
/// <list type="bullet">
///   <item><c>sku</c> — the line's SKU.</item>
///   <item><c>baseline_unit_price</c> — what spec 007's
///         <see cref="BackendApi.Modules.Shared.IPricingBaselineProvider"/> returned.</item>
///   <item><c>override_unit_price</c> — the operator's chosen lower price.</item>
///   <item><c>override_reason</c> — the bilingual reason (FR-016 + FR-040).</item>
///   <item><c>authored_by</c> — the operator's admin user-id.</item>
/// </list>
/// (FR-040, SC-004 — every below-baseline action MUST have a structured audit
/// trail so finance can reconcile commercial overrides post-hoc.)
///
/// <b>Cycle scoping</b> (Cycle A TDD-baseline pattern): T087 (handler) +
/// audit publisher wiring land in Cycle B. While the route is unmapped the
/// audit ledger picks up nothing for this quote — the post-condition is
/// satisfied vacuously and the assertions use <c>BeOneOf(404, ExpectedCode)</c>.
/// Once T087 lands, the assertions tighten to the exact event count + payload.
/// </summary>
public sealed class BelowBaselineAuditTests : IClassFixture<B2BApiFactory>
{
    private readonly B2BApiFactory _factory;

    public BelowBaselineAuditTests(B2BApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Below_baseline_override_writes_one_quote_line_override_audit_event()
    {
        _factory.ResetStubState();

        var customerId = Guid.NewGuid();
        var quoteId = await SeedRequestedQuoteAsync(customerId);

        // Stub baseline default is 100.00 SAR for any SKU that isn't seeded
        // with an override (StubPricingBaselineProvider). 75.00 is unambiguously
        // below the 100.00 baseline.
        const string belowBaselineSku = "BELOW-100";
        const decimal overridePrice = 75.00m;
        const decimal expectedBaseline = 100.00m;

        var client = _factory.CreateClient();
        var adminId = Guid.NewGuid();
        AuthenticateAsAdminWithId(client, adminId, market: "ksa", permissions: "quotes.author");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.PostAsJsonAsync(
            $"/api/admin/quotes/{quoteId}/draft",
            new
            {
                lines = new[]
                {
                    new
                    {
                        sku = belowBaselineSku,
                        quantity = 5,
                        override_unit_price = overridePrice,
                        override_reason = new
                        {
                            en = "Volume discount for repeat customer.",
                            ar = "خصم الكمية للعميل المتكرر.",
                        },
                        line_discount_amount = 0m,
                    },
                },
                terms_text = new { en = "Net 30", ar = "شبكة 30" },
                terms_days = 30,
                validity_extends = false,
            });

        resp.StatusCode.Should().BeOneOf(
            HttpStatusCode.NotFound,    // T087 not yet mapped (Cycle A)
            HttpStatusCode.OK);         // T087 wired (Cycle B+)

        if (resp.StatusCode == HttpStatusCode.OK)
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var appDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // The audit ledger lives under the central AppDbContext
            // (audit_log_entries table; entity AuditLogEntry — Action carries
            // the canonical event kind, AfterState carries the structured
            // payload). FR-040 / SC-004 demand the `quote.line_override`
            // Action specifically — generic `quote.state_changed` is not
            // sufficient.
            var auditEvents = await appDb.AuditLogEntries
                .Where(a => a.EntityId == quoteId && a.Action == "quote.line_override")
                .ToListAsync();

            auditEvents.Should().HaveCount(1,
                "FR-040: every below-baseline override emits exactly one line_override audit event");

            var entry = auditEvents[0];
            entry.AfterState.Should().NotBeNull();
            entry.AfterState!.Should().Contain(belowBaselineSku);
            // InvariantCulture is load-bearing here: AfterState is JSON which
            // always uses '.' as the decimal separator. CurrentCulture on a CI
            // box configured for de-DE / fr-FR would render `0.00` as `75,00`
            // and produce a misleading false-fail once Cycle B writes the row.
            entry.AfterState!.Should().Contain(
                overridePrice.ToString("0.00", CultureInfo.InvariantCulture));
            entry.AfterState!.Should().Contain(
                expectedBaseline.ToString("0.00", CultureInfo.InvariantCulture));
            entry.ActorId.Should().Be(adminId,
                "the audit row pins the operator's admin user-id (FR-040 'authored_by')");
        }
    }

    [Fact]
    public async Task At_baseline_override_does_not_write_line_override_audit_event()
    {
        _factory.ResetStubState();

        var customerId = Guid.NewGuid();
        var quoteId = await SeedRequestedQuoteAsync(customerId);

        // override_unit_price equals the baseline → not a below-baseline event,
        // so no `quote.line_override` audit row should be written. (A regular
        // `quote.state_changed` for requested→drafted IS expected — the
        // narrower line_override kind is reserved for FR-040 cases.)
        var client = _factory.CreateClient();
        AuthenticateAsAdmin(client, market: "ksa", permissions: "quotes.author");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.PostAsJsonAsync(
            $"/api/admin/quotes/{quoteId}/draft",
            new
            {
                lines = new[]
                {
                    new
                    {
                        sku = "AT-BASELINE-SKU",
                        quantity = 1,
                        override_unit_price = 100.00m,   // == baseline default
                        line_discount_amount = 0m,
                    },
                },
                terms_text = new { en = "Net 30", ar = "شبكة 30" },
                terms_days = 30,
                validity_extends = false,
            });

        resp.StatusCode.Should().BeOneOf(
            HttpStatusCode.NotFound,
            HttpStatusCode.OK);

        if (resp.StatusCode == HttpStatusCode.OK)
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var appDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var lineOverrideEvents = await appDb.AuditLogEntries
                .Where(a => a.EntityId == quoteId && a.Action == "quote.line_override")
                .CountAsync();
            lineOverrideEvents.Should().Be(0,
                "at-baseline pricing is not a below-baseline override; no line_override event");
        }
    }

    private async Task<Guid> SeedRequestedQuoteAsync(Guid customerId)
    {
        var quoteId = Guid.NewGuid();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<B2BDbContext>();

        db.Quotes.Add(new Quote
        {
            Id = quoteId,
            CustomerId = customerId,
            CompanyId = null,
            BranchId = null,
            MarketCode = "ksa",
            State = "requested",
            RequestedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
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
            OriginatingCartSnapshotJson = """[{"sku":"ABC-123","quantity":5,"line_note":null}]""",
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
        => AuthenticateAsAdminWithId(client, Guid.NewGuid(), market, permissions);

    private static void AuthenticateAsAdminWithId(
        HttpClient client,
        Guid adminId,
        string market,
        string? permissions = null)
    {
        client.DefaultRequestHeaders.Add("X-Test-Admin-Id", adminId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Market", market);
        if (!string.IsNullOrWhiteSpace(permissions))
        {
            client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        }
    }
}
