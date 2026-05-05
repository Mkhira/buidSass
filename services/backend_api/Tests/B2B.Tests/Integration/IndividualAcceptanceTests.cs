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
/// Spec 021 T075 (US2) — FR-027 + Clarifications Q1 — codifies the individual-customer
/// acceptance path. An individual quote (no <c>company_id</c>) MUST go directly from
/// <c>revised → accepted</c> when the buyer submits acceptance — there is no
/// approver-queue analogue outside the company workflow, so the
/// <c>pending-approver</c> state never enters this path.
///
/// Behavioral contract assertions:
/// <list type="bullet">
///   <item>State transitions DIRECTLY to <c>accepted</c> (skipping
///         <c>pending-approver</c>).</item>
///   <item>The conversion handler (<see cref="BackendApi.Modules.Shared.IOrderFromQuoteHandler"/>)
///         is invoked exactly once with <c>InvoiceBilling=false</c> — individual
///         customers have no business-invoicing path in V1 (FR-027).</item>
///   <item>The <see cref="BackendApi.Modules.Shared.QuoteAccepted"/> domain event
///         fans out exactly once with the resulting order id.</item>
///   <item>No <see cref="BackendApi.Modules.Shared.QuotePendingApprover"/> event is
///         published — the approver-routing branch must never engage on this path.</item>
/// </list>
/// </summary>
public sealed class IndividualAcceptanceTests : IClassFixture<B2BApiFactory>
{
    private readonly B2BApiFactory _factory;

    public IndividualAcceptanceTests(B2BApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Individual_quote_accepts_directly_skipping_pending_approver_with_invoice_billing_false()
    {
        _factory.ResetStubState();

        var customerId = Guid.NewGuid();
        var quoteId = await SeedRevisedIndividualQuoteAsync(customerId);

        var client = _factory.CreateClient();
        AuthenticateAs(client, customerId, market: "ksa");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.PostAsJsonAsync(
            $"/api/customer/quotes/{quoteId}/submit-acceptance",
            new
            {
                po_warning_acknowledged = false,
                tax_preview_drift_acknowledged = false,
            });

        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "FR-027 + Clarifications Q1: individual quotes accept directly without an approver queue");

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("state").GetString().Should().Be("accepted",
            "the routing fork on a CompanyId IS NULL quote MUST go directly to `accepted` — the `pending-approver` branch is COMPANY-only");
        body.TryGetProperty("decided_at", out var decidedAt).Should().BeTrue();
        decidedAt.ValueKind.Should().NotBe(JsonValueKind.Null,
            "direct-accept path stamps DecidedAt at the same instant as TerminalAt");
        body.TryGetProperty("terminal_at", out var terminalAt).Should().BeTrue();
        terminalAt.ValueKind.Should().NotBe(JsonValueKind.Null,
            "the accepted state is terminal — TerminalAt MUST be set");
        body.TryGetProperty("order_id", out var orderId).Should().BeTrue();
        orderId.ValueKind.Should().NotBe(JsonValueKind.Null,
            "conversion fires on the direct-accept path — order_id is plumbed back into the response");

        // ---------- Conversion invariants ----------
        _factory.OrderFromQuoteHandler.Invocations.Should().HaveCount(1,
            "the conversion handler MUST be invoked exactly once on a successful direct-accept");
        var conversion = _factory.OrderFromQuoteHandler.Invocations[0];
        conversion.QuoteId.Should().Be(quoteId);
        conversion.CustomerId.Should().Be(customerId);
        conversion.CompanyId.Should().BeNull(
            "individual quotes carry no company id into the order");
        conversion.CompanyBranchId.Should().BeNull();
        conversion.MarketCode.Should().Be("ksa");
        conversion.InvoiceBilling.Should().BeFalse(
            "FR-027: individual customers have no business-invoicing path — the converted order MUST carry invoice_billing=false");

        // ---------- Domain events ----------
        var pendingApproverEvents = _factory.EventCollector
            .OfType<BackendApi.Modules.Shared.QuotePendingApprover>()
            .ToList();
        pendingApproverEvents.Should().BeEmpty(
            "the pending-approver branch is COMPANY-only — individual quotes MUST NOT emit QuotePendingApprover");

        var accepted = _factory.EventCollector
            .OfType<BackendApi.Modules.Shared.QuoteAccepted>()
            .ToList();
        accepted.Should().HaveCount(1,
            "QuoteAccepted MUST fan out exactly once on the direct-accept path");
        accepted[0].QuoteId.Should().Be(quoteId);
        accepted[0].CompanyId.Should().BeNull();
        accepted[0].MarketCode.Should().Be("ksa");

        // ---------- Persisted state ----------
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<B2BDbContext>();
        var persisted = await db.Quotes.FindAsync(quoteId);
        persisted.Should().NotBeNull();
        persisted!.State.Should().Be("accepted");
        persisted.CompanyId.Should().BeNull();
        persisted.InvoiceBilling.Should().BeFalse(
            "individual quote.invoice_billing remains false through the acceptance commit");
        persisted.DecidedBy.Should().Be(customerId,
            "direct-accept attributes the decision to the customer-owner");
    }

    /// <summary>
    /// Seeds a fresh individual quote in <c>revised</c> state — the only legal
    /// source state for SubmitAcceptance per the QuoteStateMachine. No company,
    /// no branch, <c>invoice_billing=false</c> (matches the T064 / T076 default
    /// for individual customers).
    /// </summary>
    private async Task<Guid> SeedRevisedIndividualQuoteAsync(Guid customerId)
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
            State = "revised",
            RequestedAt = DateTimeOffset.UtcNow.AddHours(-2),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(13),
            DecidedAt = null,
            DecidedBy = null,
            TerminalAt = null,
            TerminalReason = null,
            PoNumber = null,
            InvoiceBilling = false,
            CustomerSuppliedMessageJson = null,
            InternalNote = null,
            ApproverRejectionNote = null,
            // §2.2 individual-quote shape: cart snapshot is null; product id carries the line.
            OriginatingCartSnapshotJson = null,
            OriginatingProductId = Guid.NewGuid(),
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
