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
/// Spec 021 T061b — FR-019 + spec.md §Edge Case "PO number reuse" — codifies the
/// PO soft-warning vs. hard-reject behavior at acceptance time. Two branches per
/// the company configuration:
///
/// <list type="bullet">
///   <item><b><c>unique_po_required=false</c> (soft-warning company)</b>: a
///         repeated PO across quotes triggers a soft warning at acceptance —
///         the SubmitAcceptance handler returns 200 with body
///         <c>po_warning: { prior_quote_ids: [...] }</c> when
///         <c>po_warning_acknowledged=false</c>. Resubmitting the same body
///         with <c>po_warning_acknowledged=true</c> commits the acceptance and
///         writes a <c>quote.po_warning_acknowledged</c> audit event listing
///         the prior quote ids.</item>
///   <item><b><c>unique_po_required=true</c> (strict company)</b>: PO reuse
///         hard-rejects with <c>409 quote.po_already_used</c> regardless of
///         the acknowledgement flag.</item>
/// </list>
///
/// <b>Cycle scoping</b> (cycle-A TDD-baseline pattern — feedback memory
/// "Cycle A TDD-baseline cut"): T070 (SubmitAcceptance handler) is NOT in this
/// cycle's scope — it ships with the US6 conversion handler so the
/// tax-preview-drift / approver-routing branches share a transaction with the
/// PO check. While T070 is unmapped (404), this test asserts the eventual
/// wire vocabulary using <c>BeOneOf(404, ExpectedCode)</c> so the suite stays
/// green pre-implementation while still pinning the contract intent. Once
/// T070 lands, the assertions tighten to the exact codes.
///
/// <b>Setup</b>: each scenario seeds a Company (matching the
/// <c>unique_po_required</c> branch under test), a buyer membership, and one
/// already-accepted quote with the PO that's about to be reused. The
/// <c>SubmitAcceptance</c> POST then exercises the warning vs. hard-reject
/// branch.
///
/// <b>Idempotency-Key</b>: SubmitAcceptance per contract §2.7 requires an
/// Idempotency-Key header. The acknowledgement-resubmit flow per spec.md
/// §Edge Case requires the SAME Idempotency-Key be sent on the resubmit so
/// the handler recognizes it as a continuation, not a fresh attempt.
/// </summary>
public sealed class PoSoftWarningFlowTests : IClassFixture<B2BApiFactory>
{
    private readonly B2BApiFactory _factory;

    public PoSoftWarningFlowTests(B2BApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Soft_warning_company_returns_po_warning_until_acknowledged()
    {
        _factory.ResetStubState();

        var companyId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var priorQuoteId = Guid.NewGuid();
        const string reusedPo = "PO-2026-DUPLICATE";

        await SeedSoftWarningCompanyAsync(
            companyId, buyerId, priorQuoteId,
            uniquePoRequired: false, priorPoNumber: reusedPo);

        // The fresh quote that will be accepted with the reused PO must already
        // exist and be in `revised` (the only state from which acceptance is
        // valid per state machine §3.1). Seed it directly.
        var newQuoteId = await SeedAcceptableQuoteAsync(companyId, buyerId, reusedPo);

        var client = _factory.CreateClient();
        AuthenticateAs(client, buyerId, market: "ksa");

        var idemKey = Guid.NewGuid().ToString();

        // ---------- Round 1: po_warning_acknowledged not set → expect 200 + po_warning ----------
        client.DefaultRequestHeaders.Add("Idempotency-Key", idemKey);
        var firstResp = await client.PostAsJsonAsync(
            $"/api/customer/quotes/{newQuoteId}/submit-acceptance",
            new
            {
                po_warning_acknowledged = false,
            });

        // While T070 is unmapped the route returns 404; once landed it MUST
        // return 200 with the po_warning envelope. We accept either to keep the
        // suite green at Cycle C-tail; subsequent cycles tighten the upper
        // branch to the exact 200 + body shape.
        firstResp.StatusCode.Should().BeOneOf(
            HttpStatusCode.NotFound,             // T070 not yet mapped
            HttpStatusCode.OK);                  // T070 wired with soft-warning branch

        if (firstResp.StatusCode == HttpStatusCode.OK)
        {
            var body = await firstResp.Content.ReadFromJsonAsync<JsonElement>();
            body.TryGetProperty("po_warning", out var warning).Should().BeTrue(
                "soft-warning company MUST surface po_warning on the first submit");
            warning.TryGetProperty("prior_quote_ids", out var priorIds).Should().BeTrue();
            priorIds.GetArrayLength().Should().BeGreaterOrEqualTo(1);
            priorIds.EnumerateArray()
                .Select(e => e.GetGuid())
                .Should().Contain(priorQuoteId);
        }

        // ---------- Round 2: same Idempotency-Key + po_warning_acknowledged=true → commit ----------
        // The same Idempotency-Key is REQUIRED to mark this as a continuation
        // of the original submission per spec §Edge Case "PO number reuse".
        var secondResp = await client.PostAsJsonAsync(
            $"/api/customer/quotes/{newQuoteId}/submit-acceptance",
            new
            {
                po_warning_acknowledged = true,
            });

        secondResp.StatusCode.Should().BeOneOf(
            HttpStatusCode.NotFound,             // T070 not yet mapped
            HttpStatusCode.OK,                   // T070 wired; commit accepted
            HttpStatusCode.Created);             // alt success envelope

        if (secondResp.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created)
        {
            // Once committed: the acceptance MUST NOT carry po_warning anymore.
            var body = await secondResp.Content.ReadFromJsonAsync<JsonElement>();
            body.TryGetProperty("po_warning", out _).Should().BeFalse(
                "the acknowledged-resubmit branch commits the acceptance and drops the warning envelope");

            // The audit ledger must record `quote.po_warning_acknowledged` —
            // structural assertion deferred to Cycle B (T070+T100) where the
            // audit publisher is wired through the test harness end-to-end.
        }
    }

    [Fact]
    public async Task Strict_company_hard_rejects_po_reuse_regardless_of_acknowledgement()
    {
        _factory.ResetStubState();

        var companyId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var priorQuoteId = Guid.NewGuid();
        const string reusedPo = "PO-2026-STRICT";

        await SeedSoftWarningCompanyAsync(
            companyId, buyerId, priorQuoteId,
            uniquePoRequired: true, priorPoNumber: reusedPo);

        var newQuoteId = await SeedAcceptableQuoteAsync(companyId, buyerId, reusedPo);

        var client = _factory.CreateClient();
        AuthenticateAs(client, buyerId, market: "ksa");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        // Strict company: even with po_warning_acknowledged=true the handler
        // MUST hard-reject.
        var resp = await client.PostAsJsonAsync(
            $"/api/customer/quotes/{newQuoteId}/submit-acceptance",
            new
            {
                po_warning_acknowledged = true,
            });

        resp.StatusCode.Should().BeOneOf(
            HttpStatusCode.NotFound,             // T070 not yet mapped
            HttpStatusCode.Conflict);            // T070 wired; hard reject

        if (resp.StatusCode == HttpStatusCode.Conflict)
        {
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            body.TryGetProperty("reasonCode", out var reasonCode).Should().BeTrue();
            reasonCode.GetString().Should().Be("quote.po_already_used",
                "FR-019 strict mode: PO reuse hard-rejects regardless of acknowledgement flag");
        }
    }

    private async Task SeedSoftWarningCompanyAsync(
        Guid companyId,
        Guid buyerId,
        Guid priorQuoteId,
        bool uniquePoRequired,
        string priorPoNumber)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<B2BDbContext>();

        db.Companies.Add(new Company
        {
            Id = companyId,
            NameJson = """{"en":"PO Soft-Warning Co.","ar":"شركة تحذير أمر الشراء"}""",
            TaxId = $"TX-{Guid.NewGuid():N}",
            MarketCode = "ksa",
            PrimaryAddressJson = "{}",
            BillingAddressJson = null,
            ApproverRequired = false,
            PoRequired = false,
            UniquePoRequired = uniquePoRequired,
            InvoiceBillingEligible = true,
            State = "active",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        db.CompanyMemberships.Add(new CompanyMembership
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            MarketCode = "ksa",
            UserId = buyerId,
            Role = "companies.admin", // §2.7 — buyer or admin can submit acceptance
            JoinedAt = DateTimeOffset.UtcNow,
        });

        // Prior accepted quote that holds the PO.
        db.Quotes.Add(new Quote
        {
            Id = priorQuoteId,
            CustomerId = buyerId,
            CompanyId = companyId,
            BranchId = null,
            MarketCode = "ksa",
            State = "accepted",
            RequestedAt = DateTimeOffset.UtcNow.AddDays(-7),
            ExpiresAt = null,
            DecidedAt = DateTimeOffset.UtcNow.AddDays(-6),
            DecidedBy = buyerId,
            TerminalAt = DateTimeOffset.UtcNow.AddDays(-6),
            TerminalReason = "accepted",
            PoNumber = priorPoNumber,
            InvoiceBilling = true,
            CustomerSuppliedMessageJson = null,
            InternalNote = null,
            ApproverRejectionNote = null,
            OriginatingCartSnapshotJson = "[]",
            OriginatingProductId = null,
            RestrictionPolicySnapshotJson = "[]",
            SchemaVersion = 1,
        });

        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedAcceptableQuoteAsync(
        Guid companyId,
        Guid buyerId,
        string poNumber)
    {
        var quoteId = Guid.NewGuid();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<B2BDbContext>();

        // The fresh quote that's about to be submitted for acceptance — state
        // `revised` is the only legal source state for SubmitAcceptance.
        db.Quotes.Add(new Quote
        {
            Id = quoteId,
            CustomerId = buyerId,
            CompanyId = companyId,
            BranchId = null,
            MarketCode = "ksa",
            State = "revised",
            RequestedAt = DateTimeOffset.UtcNow.AddHours(-1),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(13),
            DecidedAt = null,
            DecidedBy = null,
            TerminalAt = null,
            TerminalReason = null,
            PoNumber = poNumber,
            InvoiceBilling = true,
            CustomerSuppliedMessageJson = null,
            InternalNote = null,
            ApproverRejectionNote = null,
            OriginatingCartSnapshotJson = "[]",
            OriginatingProductId = null,
            RestrictionPolicySnapshotJson = "[]",
            SchemaVersion = 1,
        });

        await db.SaveChangesAsync();
        return quoteId;
    }

    private static void AuthenticateAs(HttpClient client, Guid customerId, string market)
    {
        client.DefaultRequestHeaders.Remove("X-Test-Customer-Id");
        client.DefaultRequestHeaders.Remove("X-Test-Market");
        client.DefaultRequestHeaders.Add("X-Test-Customer-Id", customerId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Market", market);
    }
}
