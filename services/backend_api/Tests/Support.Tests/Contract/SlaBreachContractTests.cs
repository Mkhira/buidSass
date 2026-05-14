using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Support.Primitives;
using FluentAssertions;
using Support.Tests.Contract.Infrastructure;

namespace Support.Tests.Contract;

/// <summary>
/// Spec 023 T083 — HTTP contract for the US4 lead surface:
/// <c>POST /api/admin/support-tickets/{ticketId}/sla-override</c> and
/// <c>POST /api/admin/support-tickets/{ticketId}/reassign</c>. Asserts the
/// US4 Acceptance Scenarios reachable from the HTTP layer:
///   1. Reassign-justification-required (FR-038).
///   2. Override SLA — resolution must exceed first-response (FR-021).
///   3. Override SLA — happy-path recomputes deadlines, returns prior +
///      new target minutes.
///   4. Reassign — non-lead actor → 403.
///   5. Override SLA — missing justification → 400.
///
/// First-response vs resolution breach DETECTION (the worker side of US4) is
/// covered by <c>tests/Support.Tests/Integration/SlaBreachWatchWorkerTests</c>
/// — that surface is non-HTTP and belongs in integration coverage.
/// </summary>
[Collection(nameof(SupportApiCollection))]
public sealed class SlaBreachContractTests
{
    private readonly SupportApiFactory _factory;

    public SlaBreachContractTests(SupportApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Reassign_missing_justification_returns_400_with_reassign_justification_required()
    {
        await _factory.ResetAsync();
        var ticketId = await _factory.SeedTicketAsync(
            Guid.NewGuid(),
            state: TicketStateNames.InProgress,
            assignedAgentId: Guid.NewGuid());

        var leadClient = _factory.AuthenticatedLeadClient(Guid.NewGuid());
        var resp = await leadClient.PostAsJsonAsync(
            $"/api/admin/support-tickets/{ticketId}/reassign",
            new { target_agent_id = Guid.NewGuid(), justification_note = "" });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await resp.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("reasonCode").GetString()
            .Should().Be(TicketReasonCode.ReassignJustificationRequired);
    }

    [Fact]
    public async Task Override_sla_resolution_not_exceeding_first_response_returns_400()
    {
        await _factory.ResetAsync();
        var ticketId = await _factory.SeedTicketAsync(Guid.NewGuid());
        var leadClient = _factory.AuthenticatedLeadClient(Guid.NewGuid());

        var resp = await leadClient.PostAsJsonAsync(
            $"/api/admin/support-tickets/{ticketId}/sla-override",
            new
            {
                first_response_target_minutes = 240,
                resolution_target_minutes = 60, // INVALID: less than first_response
                justification_note = "Reducing targets per customer agreement.",
            });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await resp.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("reasonCode").GetString()
            .Should().Be(TicketReasonCode.SlaOverrideResolutionMustExceedFirstResponse);
    }

    [Fact]
    public async Task Override_sla_returns_200_with_prior_and_new_target_minutes_and_recomputed_dues()
    {
        await _factory.ResetAsync();
        var ticketId = await _factory.SeedTicketAsync(Guid.NewGuid());

        var leadClient = _factory.AuthenticatedLeadClient(Guid.NewGuid());
        var resp = await leadClient.PostAsJsonAsync(
            $"/api/admin/support-tickets/{ticketId}/sla-override",
            new
            {
                first_response_target_minutes = 30,
                resolution_target_minutes = 720,
                justification_note = "Escalated by lead for VIP customer.",
            });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("new_first_response_target_minutes").GetInt32().Should().Be(30);
        body.GetProperty("new_resolution_target_minutes").GetInt32().Should().Be(720);
        body.GetProperty("prior_first_response_target_minutes").GetInt32().Should().Be(240);
        body.GetProperty("prior_resolution_target_minutes").GetInt32().Should().Be(2880);
        body.GetProperty("new_first_response_due_utc").ValueKind.Should().Be(JsonValueKind.String);
        body.GetProperty("new_resolution_due_utc").ValueKind.Should().Be(JsonValueKind.String);
    }

    [Fact]
    public async Task Reassign_with_only_support_agent_permission_returns_403()
    {
        await _factory.ResetAsync();
        var ticketId = await _factory.SeedTicketAsync(
            Guid.NewGuid(),
            state: TicketStateNames.InProgress,
            assignedAgentId: Guid.NewGuid());

        // Only support.agent, NOT support.lead — should be rejected.
        var agentClient = _factory.AuthenticatedAgentClient(Guid.NewGuid());
        var resp = await agentClient.PostAsJsonAsync(
            $"/api/admin/support-tickets/{ticketId}/reassign",
            new
            {
                target_agent_id = Guid.NewGuid(),
                justification_note = "Reassigning for coverage.",
            });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await resp.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("reasonCode").GetString()
            .Should().Be(TicketReasonCode.QueueForbidden);
    }

    [Fact]
    public async Task Override_sla_missing_justification_returns_400()
    {
        await _factory.ResetAsync();
        var ticketId = await _factory.SeedTicketAsync(Guid.NewGuid());
        var leadClient = _factory.AuthenticatedLeadClient(Guid.NewGuid());

        var resp = await leadClient.PostAsJsonAsync(
            $"/api/admin/support-tickets/{ticketId}/sla-override",
            new
            {
                first_response_target_minutes = 30,
                resolution_target_minutes = 720,
                justification_note = "",
            });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await resp.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("reasonCode").GetString()
            .Should().Be(TicketReasonCode.SlaOverrideJustificationRequired);
    }
}
