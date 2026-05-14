using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Support.Primitives;
using FluentAssertions;
using Support.Tests.Contract.Infrastructure;

namespace Support.Tests.Contract;

/// <summary>
/// Spec 023 T092 — HTTP contract for
/// <c>POST /api/admin/support-tickets/{ticketId}/internal-notes</c>
/// (US5 Acceptance Scenarios 1–5). Asserts:
///   1. Successful agent post returns 200 + message_id.
///   2. Internal-note rows are stripped from the customer-facing read
///      (FR-014); `GET /api/customer/support-tickets/{id}` MUST NOT surface
///      `internal_note` rows in `messages`.
///   3. Customer attempts to call the admin endpoint are 401/403.
///   4. Body-required validation returns 400 / message_body_required.
///   5. Agent posting on a closed ticket returns 409 / closed_terminal.
///
/// The audit-row + immutability assertions (US5 scenarios 4 + 5) are covered
/// by handler-level coverage; the contract test surfaces what's reachable
/// over HTTP.
/// </summary>
[Collection(nameof(SupportApiCollection))]
public sealed class InternalNoteContractTests
{
    private readonly SupportApiFactory _factory;

    public InternalNoteContractTests(SupportApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Agent_posts_internal_note_returns_200_with_message_id()
    {
        await _factory.ResetAsync();
        var ticketId = await _factory.SeedTicketAsync(Guid.NewGuid());
        var agentClient = _factory.AuthenticatedAgentClient(Guid.NewGuid());

        var resp = await agentClient.PostAsJsonAsync(
            $"/api/admin/support-tickets/{ticketId}/internal-notes",
            new { Body = "B2B account contact prefers WhatsApp follow-up." });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("message_id").GetGuid().Should().NotBeEmpty();
    }

    [Fact]
    public async Task Customer_read_does_not_surface_internal_note_rows()
    {
        await _factory.ResetAsync();
        var customerId = Guid.NewGuid();
        var ticketId = await _factory.SeedTicketAsync(customerId);

        // Agent posts an internal note.
        var agentClient = _factory.AuthenticatedAgentClient(Guid.NewGuid());
        var post = await agentClient.PostAsJsonAsync(
            $"/api/admin/support-tickets/{ticketId}/internal-notes",
            new { Body = "Sensitive operational note — do not share with customer." });
        post.StatusCode.Should().Be(HttpStatusCode.OK);

        // Customer reads their ticket. Server must strip internal_note rows.
        var customerClient = _factory.AuthenticatedCustomerClient(customerId);
        var get = await customerClient.GetAsync(
            $"/api/customer/support-tickets/{ticketId}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await get.Content.ReadFromJsonAsync<JsonElement>();
        var messages = body.GetProperty("messages");
        foreach (var m in messages.EnumerateArray())
        {
            m.GetProperty("kind").GetString()
                .Should().NotBe(TicketMessageKindNames.InternalNote,
                    "internal_note must never reach the customer-facing read");
        }
    }

    [Fact]
    public async Task Unauthenticated_internal_note_post_returns_401()
    {
        await _factory.ResetAsync();
        var ticketId = await _factory.SeedTicketAsync(Guid.NewGuid());
        var anonClient = _factory.CreateClient();

        var resp = await anonClient.PostAsJsonAsync(
            $"/api/admin/support-tickets/{ticketId}/internal-notes",
            new { Body = "Should be rejected." });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Empty_body_returns_400_with_message_body_required()
    {
        await _factory.ResetAsync();
        var ticketId = await _factory.SeedTicketAsync(Guid.NewGuid());
        var agentClient = _factory.AuthenticatedAgentClient(Guid.NewGuid());

        var resp = await agentClient.PostAsJsonAsync(
            $"/api/admin/support-tickets/{ticketId}/internal-notes",
            new { Body = "" });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await resp.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("reasonCode").GetString()
            .Should().Be(TicketReasonCode.MessageBodyRequired);
    }

    [Fact]
    public async Task Closed_ticket_returns_409_with_closed_terminal()
    {
        await _factory.ResetAsync();
        var nowUtc = _factory.Clock.GetUtcNow();
        var ticketId = await _factory.SeedTicketAsync(
            Guid.NewGuid(),
            state: TicketStateNames.Closed,
            closedAtUtc: nowUtc.AddDays(-1));

        var agentClient = _factory.AuthenticatedAgentClient(Guid.NewGuid());
        var resp = await agentClient.PostAsJsonAsync(
            $"/api/admin/support-tickets/{ticketId}/internal-notes",
            new { Body = "Cannot post — ticket closed." });

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await resp.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("reasonCode").GetString()
            .Should().Be(TicketReasonCode.ClosedTerminal);
    }
}
