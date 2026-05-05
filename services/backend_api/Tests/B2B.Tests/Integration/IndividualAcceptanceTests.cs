using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.B2B.Entities;
using BackendApi.Modules.B2B.Persistence;
using B2B.Tests.Contract.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace B2B.Tests.Integration;

/// <summary>
/// Spec 021 T075 (US2) — verifies the FR-027 individual-customer acceptance branch:
/// an individual quote (no <c>company_id</c>) submitted for acceptance MUST go DIRECT
/// to <c>accepted</c> with NO <c>pending-approver</c> step and the conversion call
/// MUST set <c>invoice_billing=false</c> regardless of any company-eligibility flag.
///
/// The full US2 round trip per spec.md independent test: individual customer
/// requests a quote from a product (T076), admin authors + publishes (US3 — out of
/// this test's scope; we seed the <c>revised</c> state directly), customer accepts
/// directly. The conversion stub is the load-bearing assertion surface — we read
/// back the captured <see cref="BackendApi.Modules.Shared.QuoteConversionRequest"/>
/// and confirm <c>InvoiceBilling=false</c>.
///
/// T077 belt-and-suspenders: the SubmitAcceptanceHandler hard-pins
/// <c>InvoiceBilling=false</c> when <c>CompanyId IS NULL</c>, so even if the Quote
/// row's column drifts due to a future schema change, the conversion call cannot
/// silently flip an individual quote into the invoice-billing branch.
/// </summary>
public sealed class IndividualAcceptanceTests : IClassFixture<B2BApiFactory>
{
    private readonly B2BApiFactory _factory;

    public IndividualAcceptanceTests(B2BApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Individual_quote_accepts_directly_with_invoice_billing_false_and_no_approver_step()
    {
        _factory.ResetStubState();

        var customerId = Guid.NewGuid();
        var quoteId = await SeedIndividualRevisedQuoteAsync(customerId);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Customer-Id", customerId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Market", "ksa");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.PostAsJsonAsync(
            $"/api/customer/quotes/{quoteId}/submit-acceptance",
            new { });

        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "individual quote on `revised` accepts directly per FR-027");

        // The handler MUST have transitioned the quote to `accepted`, NOT
        // `pending-approver` (no approver step for individual quotes).
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<B2BDbContext>();
            var refreshed = await db.Quotes.AsNoTracking().FirstAsync(q => q.Id == quoteId);
            refreshed.State.Should().Be("accepted",
                "FR-027: individual quotes skip approver routing entirely");
            refreshed.TerminalReason.Should().Be("accepted");

            // No pending-approver transition row should exist for this quote.
            var transitions = await db.QuoteStateTransitions
                .AsNoTracking()
                .Where(t => t.QuoteId == quoteId)
                .Select(t => t.NewState)
                .ToListAsync();
            transitions.Should().NotContain("pending-approver",
                "individual quotes MUST NOT pass through the approver-required state");
        }

        // The conversion stub captured exactly one call with InvoiceBilling=false.
        // T077 hard-pins this regardless of any company-eligibility column drift.
        _factory.OrderFromQuoteHandler.Invocations.Should().HaveCount(1);
        var conversionCall = _factory.OrderFromQuoteHandler.Invocations[0];
        conversionCall.CompanyId.Should().BeNull();
        conversionCall.InvoiceBilling.Should().BeFalse(
            "T077: individual quotes MUST NEVER convert to an invoice-billed order");
    }

    private async Task<Guid> SeedIndividualRevisedQuoteAsync(Guid customerId)
    {
        var quoteId = Guid.NewGuid();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<B2BDbContext>();

        db.Quotes.Add(new Quote
        {
            Id = quoteId,
            CustomerId = customerId,
            CompanyId = null, // individual customer — load-bearing for FR-027
            BranchId = null,
            MarketCode = "ksa",
            State = "revised",
            RequestedAt = DateTimeOffset.UtcNow.AddHours(-1),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(13),
            DecidedAt = null,
            DecidedBy = null,
            TerminalAt = null,
            TerminalReason = null,
            PoNumber = null,
            InvoiceBilling = false, // matches the request-time flag (no company → false)
            CustomerSuppliedMessageJson = null,
            InternalNote = null,
            ApproverRejectionNote = null,
            OriginatingCartSnapshotJson = "[]",
            OriginatingProductId = Guid.NewGuid(), // from-product origin
            RestrictionPolicySnapshotJson = "[]",
            SchemaVersion = 1,
        });

        await db.SaveChangesAsync();
        return quoteId;
    }
}
