using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Support.Entities;
using BackendApi.Modules.Support.Persistence;
using BackendApi.Modules.Support.Primitives;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Support.Tests.Contract.Infrastructure;

namespace Support.Tests.Contract;

/// <summary>
/// Spec 023 T066 — HTTP contract for <c>GET /api/admin/support-tickets/queue</c>
/// + <c>POST /{ticketId}/claim</c> + <c>POST /{ticketId}/reassign</c>
/// (US2 Acceptance Scenarios 1–5). Asserts wire shape, status codes,
/// problem-details <c>reasonCode</c> extension, agent / lead permission
/// gating, optimistic-concurrency claim conflict, default sort, and the
/// audit-trail row produced by a lead reassignment.
/// </summary>
[Collection(nameof(SupportApiCollection))]
public sealed class AgentQueueContractTests
{
    private readonly SupportApiFactory _factory;

    public AgentQueueContractTests(SupportApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Queue_filters_apply_and_response_includes_items_and_total()
    {
        await _factory.ResetAsync();
        var customerSa = Guid.NewGuid();
        var customerEg = Guid.NewGuid();
        await _factory.SeedTicketAsync(customerSa, market: "SA", category: TicketCategoryNames.OrderIssue);
        await _factory.SeedTicketAsync(customerSa, market: "SA", category: TicketCategoryNames.PaymentIssue);
        await _factory.SeedTicketAsync(customerEg, market: "EG", category: TicketCategoryNames.OrderIssue);

        var client = _factory.AuthenticatedAgentClient(Guid.NewGuid());
        var resp = await client.GetAsync("/api/admin/support-tickets/queue?market_code=SA&category=order_issue");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("items").GetArrayLength().Should().Be(1);
        body.GetProperty("total").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Queue_returns_403_for_actor_without_support_agent_permission()
    {
        await _factory.ResetAsync();
        var client = _factory.CreateClient();
        // Authenticate but withhold any support.* permission.
        client.DefaultRequestHeaders.Add("X-Test-Admin-Id", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("X-Test-Market", "SA");

        var resp = await client.GetAsync("/api/admin/support-tickets/queue");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await resp.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("reasonCode").GetString()
            .Should().Be(TicketReasonCode.QueueForbidden);
    }

    [Fact]
    public async Task Concurrent_claim_returns_409_assignment_conflict_to_the_loser()
    {
        await _factory.ResetAsync();
        var customerId = Guid.NewGuid();
        var ticketId = await _factory.SeedTicketAsync(customerId);

        var agentA = Guid.NewGuid();
        var agentB = Guid.NewGuid();
        var clientA = _factory.AuthenticatedAgentClient(agentA);
        var clientB = _factory.AuthenticatedAgentClient(agentB);

        var first = await clientA.PostAsync(
            $"/api/admin/support-tickets/{ticketId}/claim", content: null);
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await clientB.PostAsync(
            $"/api/admin/support-tickets/{ticketId}/claim", content: null);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await second.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("reasonCode").GetString()
            .Should().Be(TicketReasonCode.AssignmentConflict);
    }

    [Fact]
    public async Task Queue_default_sort_orders_by_oldest_unassigned_first()
    {
        await _factory.ResetAsync();
        var customerId = Guid.NewGuid();
        var older = await _factory.SeedTicketAsync(customerId);
        // Advance the clock so the second ticket has a strictly later
        // CreatedAtUtc; SeedTicketAsync stamps from FakeTimeProvider.
        _factory.Clock.Advance(TimeSpan.FromMinutes(5));
        var newer = await _factory.SeedTicketAsync(customerId);

        var client = _factory.AuthenticatedAgentClient(Guid.NewGuid());
        var resp = await client.GetAsync("/api/admin/support-tickets/queue");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = (await resp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items");
        items.GetArrayLength().Should().BeGreaterThanOrEqualTo(2);
        // First two rows: older then newer (default sort = oldest unassigned first).
        var firstId = items[0].GetProperty("id").GetGuid();
        var secondId = items[1].GetProperty("id").GetGuid();
        firstId.Should().Be(older);
        secondId.Should().Be(newer);
    }

    [Fact]
    public async Task Lead_reassignment_writes_audit_assignment_row_with_lead_kind_and_justification()
    {
        await _factory.ResetAsync();
        var customerId = Guid.NewGuid();
        var priorAgent = Guid.NewGuid();
        var targetAgent = Guid.NewGuid();
        var ticketId = await _factory.SeedTicketAsync(
            customerId, state: TicketStateNames.InProgress, assignedAgentId: priorAgent);

        var leadClient = _factory.AuthenticatedLeadClient(Guid.NewGuid());
        var resp = await leadClient.PostAsJsonAsync(
            $"/api/admin/support-tickets/{ticketId}/reassign",
            new
            {
                target_agent_id = targetAgent,
                justification_note = "Reassigning to specialist for B2B account context.",
            });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("assignment_id").GetGuid().Should().NotBeEmpty();
        body.GetProperty("prior_agent_id").GetGuid().Should().Be(priorAgent);

        // Verify a new TicketAssignment row was appended with kind=lead_reassignment.
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportDbContext>();
        var assignments = await db.Assignments
            .Where(a => a.TicketId == ticketId)
            .OrderBy(a => a.AssignedAtUtc)
            .ToListAsync();
        assignments.Should().HaveCount(2);
        assignments.Last().AgentId.Should().Be(targetAgent);
        assignments.Last().AssignmentKind.Should().Be(TicketAssignmentKind.LeadReassignment);
        assignments.Last().JustificationNote.Should().NotBeNullOrWhiteSpace();
    }
}
