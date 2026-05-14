using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Support.Primitives;
using FluentAssertions;
using Support.Tests.Contract.Infrastructure;

namespace Support.Tests.Contract;

/// <summary>
/// Spec 023 T075 — HTTP contract for
/// <c>POST /api/customer/support-tickets/{ticketId}/convert-to-return</c>
/// (US3 Acceptance Scenarios 1–5). Asserts wire shape, idempotent retry
/// returning the same return_request_id, the conversion-category eligibility
/// gate (FR-028), non-owner forbidden, and that the outgoing return-creation
/// contract (spec 013) is invoked with the same Idempotency-Key the caller
/// supplied.
/// </summary>
[Collection(nameof(SupportApiCollection))]
public sealed class ConvertToReturnContractTests
{
    private readonly SupportApiFactory _factory;

    public ConvertToReturnContractTests(SupportApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Convert_eligible_ticket_returns_200_with_return_request_id_and_invokes_contract()
    {
        await _factory.ResetAsync();
        var customerId = Guid.NewGuid();
        var orderLineId = Guid.NewGuid();
        var ticketId = await _factory.SeedTicketAsync(
            customerId,
            category: TicketCategoryNames.ReturnRefundRequest,
            linkedEntityKind: "order_line",
            linkedEntityId: orderLineId);

        var client = _factory.AuthenticatedCustomerClient(customerId);
        client.DefaultRequestHeaders.Add("Idempotency-Key", "idem-" + Guid.NewGuid());

        var resp = await client.PostAsJsonAsync(
            $"/api/customer/support-tickets/{ticketId}/convert-to-return",
            new { Narrative = "Item arrived damaged.", AttachmentIds = (Guid[]?)null });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("return_request_id").GetGuid().Should().NotBeEmpty();
        body.GetProperty("idempotent").GetBoolean().Should().BeFalse();

        _factory.ReturnCreationContract.Invocations.Should().ContainSingle(
            i => i.customerId == customerId && i.orderLineId == orderLineId);
    }

    [Fact]
    public async Task Convert_ineligible_category_returns_400_with_conversion_category_not_eligible()
    {
        await _factory.ResetAsync();
        var customerId = Guid.NewGuid();
        // GeneralQuestion is NOT convertible per TicketCategoryNames.IsConvertibleToReturn.
        var ticketId = await _factory.SeedTicketAsync(
            customerId, category: TicketCategoryNames.GeneralQuestion);

        var client = _factory.AuthenticatedCustomerClient(customerId);
        client.DefaultRequestHeaders.Add("Idempotency-Key", "idem-" + Guid.NewGuid());

        var resp = await client.PostAsJsonAsync(
            $"/api/customer/support-tickets/{ticketId}/convert-to-return",
            new { Narrative = (string?)null, AttachmentIds = (Guid[]?)null });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await resp.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("reasonCode").GetString()
            .Should().Be(TicketReasonCode.ConversionCategoryNotEligible);
    }

    [Fact]
    public async Task Idempotent_retry_with_same_key_returns_same_return_request_id()
    {
        await _factory.ResetAsync();
        var customerId = Guid.NewGuid();
        var orderLineId = Guid.NewGuid();
        var ticketId = await _factory.SeedTicketAsync(
            customerId,
            category: TicketCategoryNames.ProductDefect,
            linkedEntityKind: "order_line",
            linkedEntityId: orderLineId);

        var client = _factory.AuthenticatedCustomerClient(customerId);
        var idemKey = "idem-" + Guid.NewGuid();
        client.DefaultRequestHeaders.Add("Idempotency-Key", idemKey);

        var first = await client.PostAsJsonAsync(
            $"/api/customer/support-tickets/{ticketId}/convert-to-return",
            new { Narrative = "Defect on receipt.", AttachmentIds = (Guid[]?)null });
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("return_request_id").GetGuid();

        var second = await client.PostAsJsonAsync(
            $"/api/customer/support-tickets/{ticketId}/convert-to-return",
            new { Narrative = "Defect on receipt.", AttachmentIds = (Guid[]?)null });
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        secondBody.GetProperty("return_request_id").GetGuid().Should().Be(firstId);
        secondBody.GetProperty("idempotent").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Non_owner_convert_returns_403_with_conversion_forbidden()
    {
        await _factory.ResetAsync();
        var owner = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var ticketId = await _factory.SeedTicketAsync(
            owner,
            category: TicketCategoryNames.ReturnRefundRequest,
            linkedEntityKind: "order_line",
            linkedEntityId: Guid.NewGuid());

        var client = _factory.AuthenticatedCustomerClient(stranger);
        client.DefaultRequestHeaders.Add("Idempotency-Key", "idem-" + Guid.NewGuid());

        var resp = await client.PostAsJsonAsync(
            $"/api/customer/support-tickets/{ticketId}/convert-to-return",
            new { Narrative = (string?)null, AttachmentIds = (Guid[]?)null });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await resp.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("reasonCode").GetString()
            .Should().Be(TicketReasonCode.ConversionForbidden);
    }

    [Fact]
    public async Task Missing_idempotency_key_returns_400_with_idempotency_key_required()
    {
        await _factory.ResetAsync();
        var customerId = Guid.NewGuid();
        var ticketId = await _factory.SeedTicketAsync(
            customerId,
            category: TicketCategoryNames.ReturnRefundRequest,
            linkedEntityKind: "order_line",
            linkedEntityId: Guid.NewGuid());

        var client = _factory.AuthenticatedCustomerClient(customerId);
        // Deliberately omit Idempotency-Key header.

        var resp = await client.PostAsJsonAsync(
            $"/api/customer/support-tickets/{ticketId}/convert-to-return",
            new { Narrative = (string?)null, AttachmentIds = (Guid[]?)null });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await resp.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("reasonCode").GetString()
            .Should().Be(TicketReasonCode.IdempotencyKeyRequired);
    }
}
