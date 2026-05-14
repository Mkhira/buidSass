using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Shared;
using BackendApi.Modules.Support.Primitives;
using FluentAssertions;
using Support.Tests.Contract.Infrastructure;

namespace Support.Tests.Contract;

/// <summary>
/// Spec 023 T053 — HTTP contract for <c>POST /api/customer/support-tickets/</c>
/// (US1 Acceptance Scenarios 1–6). Asserts wire shape, status codes,
/// problem-details <c>reasonCode</c> extension, and the linked-entity
/// ownership / consistency rules wired through <c>MarketCodeResolver</c>.
/// Rate-limit (FR-010 / SC-009) is platform-level middleware not yet wired
/// into this slice — the dedicated rate-limit case lives in
/// <c>tests/Support.Tests/Integration/CustomerRateLimitTests.cs</c> (T145).
/// </summary>
[Collection(nameof(SupportApiCollection))]
public sealed class OpenTicketContractTests
{
    private readonly SupportApiFactory _factory;

    public OpenTicketContractTests(SupportApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Unauthenticated_request_returns_401_problem_details()
    {
        await _factory.ResetAsync();
        var client = _factory.CreateClient();

        var resp = await client.PostAsJsonAsync("/api/customer/support-tickets/", new
        {
            category = TicketCategoryNames.GeneralQuestion,
            priority = TicketPriorityNames.Normal,
            locale = "en",
            subject = "Where is my order?",
            body = "I haven't received my order yet.",
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Standalone_ticket_returns_201_with_state_open_and_sla_snapshot()
    {
        await _factory.ResetAsync();
        var customerId = Guid.NewGuid();
        var client = AuthenticatedCustomerClient(customerId, market: "SA");

        var resp = await client.PostAsJsonAsync("/api/customer/support-tickets/", new
        {
            category = TicketCategoryNames.GeneralQuestion,
            priority = TicketPriorityNames.Normal,
            locale = "en",
            subject = "How do I update my address?",
            body = "I need to update the shipping address on file.",
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("ticket_id").GetGuid().Should().NotBeEmpty();
        body.GetProperty("state").GetString().Should().Be(TicketStateNames.Open);
        body.GetProperty("market_code").GetString().Should().Be("SA");
        body.GetProperty("first_response_due_utc").ValueKind.Should().Be(JsonValueKind.String);
        body.GetProperty("resolution_due_utc").ValueKind.Should().Be(JsonValueKind.String);
    }

    [Fact]
    public async Task Linked_entity_not_owned_returns_403_with_linked_entity_not_owned_reason()
    {
        await _factory.ResetAsync();
        var customerId = Guid.NewGuid();
        var orderLineId = Guid.NewGuid();
        // Stage an order_line owned by a DIFFERENT customer; the resolver returns
        // OwnedByActor=false → handler emits 403 / linked_entity_not_owned.
        _factory.OrderContract.Stage("order_line", new LinkedEntityReadResult(
            LinkedEntityId: orderLineId,
            MarketCode: "SA",
            OwnedByActor: false,
            VendorId: null,
            DisplaySummary: null));

        var client = AuthenticatedCustomerClient(customerId, market: "SA");
        var resp = await client.PostAsJsonAsync("/api/customer/support-tickets/", new
        {
            category = TicketCategoryNames.OrderIssue,
            priority = TicketPriorityNames.Normal,
            locale = "en",
            subject = "Order delivered to wrong address",
            body = "The order was delivered to the wrong building.",
            linkedEntityKind = "order_line",
            linkedEntityId = orderLineId,
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await resp.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("reasonCode").GetString()
            .Should().Be(TicketReasonCode.LinkedEntityNotOwned);
    }

    [Fact]
    public async Task Category_kind_inconsistent_returns_400_with_kind_inconsistent_reason()
    {
        await _factory.ResetAsync();
        var customerId = Guid.NewGuid();
        // ReviewDispute category requires a linked entity of kind "review", not "order".
        var orderId = Guid.NewGuid();
        _factory.OrderContract.Stage("order", new LinkedEntityReadResult(
            LinkedEntityId: orderId,
            MarketCode: "SA",
            OwnedByActor: true,
            VendorId: null,
            DisplaySummary: "Order #ABC"));

        var client = AuthenticatedCustomerClient(customerId, market: "SA");
        var resp = await client.PostAsJsonAsync("/api/customer/support-tickets/", new
        {
            category = TicketCategoryNames.ReviewDispute,
            priority = TicketPriorityNames.Normal,
            locale = "en",
            subject = "Review was hidden incorrectly",
            body = "My review on this product was hidden but it was a real purchase.",
            linkedEntityKind = "order",
            linkedEntityId = orderId,
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await resp.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("reasonCode").GetString()
            .Should().Be(TicketReasonCode.LinkedEntityKindInconsistent);
    }

    [Fact]
    public async Task Priority_not_customer_selectable_returns_400()
    {
        await _factory.ResetAsync();
        var customerId = Guid.NewGuid();
        var client = AuthenticatedCustomerClient(customerId, market: "SA");

        var resp = await client.PostAsJsonAsync("/api/customer/support-tickets/", new
        {
            category = TicketCategoryNames.GeneralQuestion,
            priority = TicketPriorityNames.Urgent, // customers may only pick low/normal
            locale = "en",
            subject = "Question",
            body = "I'd like to ask a question.",
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await resp.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("reasonCode").GetString()
            .Should().Be(TicketReasonCode.PriorityNotCustomerSelectable);
    }

    [Fact]
    public async Task Subject_required_returns_400_with_subject_required_reason()
    {
        await _factory.ResetAsync();
        var customerId = Guid.NewGuid();
        var client = AuthenticatedCustomerClient(customerId, market: "SA");

        var resp = await client.PostAsJsonAsync("/api/customer/support-tickets/", new
        {
            category = TicketCategoryNames.GeneralQuestion,
            priority = TicketPriorityNames.Normal,
            locale = "en",
            subject = "",
            body = "Body is here, subject is empty.",
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await resp.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("reasonCode").GetString()
            .Should().Be(TicketReasonCode.SubjectRequired);
    }

    private HttpClient AuthenticatedCustomerClient(Guid customerId, string market)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Customer-Id", customerId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Market", market);
        return client;
    }
}
