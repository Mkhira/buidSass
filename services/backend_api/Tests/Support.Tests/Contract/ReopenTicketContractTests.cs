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
/// Spec 023 T101 — HTTP contract for
/// <c>POST /api/customer/support-tickets/{ticketId}/reopen</c>
/// (US6 Acceptance Scenarios 1–5). Asserts:
///   1. Happy path: Resolved ticket → in_progress, reopen_count++, SLA deadlines reset.
///   2. Closed-terminal: rejecting a closed ticket → 409 / closed_terminal.
///   3. Window-closed: ResolvedAtUtc outside per-market window → 409 / reopen_window_closed.
///   4. Market-disabled: ReopenWindowDays / MaxReopenCount set to 0 → 409 / reopen_disabled_for_market.
///   5. Count-exceeded: ReopenCount == MaxReopenCount → 409 / reopen_count_exceeded.
/// </summary>
[Collection(nameof(SupportApiCollection))]
public sealed class ReopenTicketContractTests
{
    private readonly SupportApiFactory _factory;

    public ReopenTicketContractTests(SupportApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Reopen_resolved_ticket_returns_200_and_resets_sla_deadlines()
    {
        await _factory.ResetAsync();
        await ResetMarketSchemaToDefaultsAsync();
        var customerId = Guid.NewGuid();
        var nowUtc = _factory.Clock.GetUtcNow();
        var ticketId = await _factory.SeedTicketAsync(
            customerId,
            state: TicketStateNames.Resolved,
            resolvedAtUtc: nowUtc.AddDays(-1),
            reopenCount: 0,
            firstResponseDueUtc: nowUtc.AddMinutes(-100),
            resolutionDueUtc: nowUtc.AddMinutes(-50));

        var client = _factory.AuthenticatedCustomerClient(customerId);
        var resp = await client.PostAsJsonAsync(
            $"/api/customer/support-tickets/{ticketId}/reopen",
            new { Body = "The issue came back, please reopen." });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("new_state").GetString().Should().Be(TicketStateNames.InProgress);
        body.GetProperty("reopen_count").GetInt32().Should().Be(1);

        // SLA deadlines recomputed from snapshot — must be strictly after `nowUtc`.
        var firstResponseDue = body.GetProperty("first_response_due_utc").GetDateTimeOffset();
        firstResponseDue.Should().BeAfter(nowUtc);
    }

    [Fact]
    public async Task Reopen_closed_ticket_returns_409_with_closed_terminal()
    {
        await _factory.ResetAsync();
        await ResetMarketSchemaToDefaultsAsync();
        var customerId = Guid.NewGuid();
        var nowUtc = _factory.Clock.GetUtcNow();
        var ticketId = await _factory.SeedTicketAsync(
            customerId,
            state: TicketStateNames.Closed,
            closedAtUtc: nowUtc.AddDays(-1));

        var client = _factory.AuthenticatedCustomerClient(customerId);
        var resp = await client.PostAsJsonAsync(
            $"/api/customer/support-tickets/{ticketId}/reopen",
            new { Body = (string?)null });

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await resp.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("reasonCode").GetString()
            .Should().Be(TicketReasonCode.ClosedTerminal);
    }

    [Fact]
    public async Task Reopen_outside_window_returns_409_with_reopen_window_closed()
    {
        await _factory.ResetAsync();
        await ResetMarketSchemaToDefaultsAsync();
        var customerId = Guid.NewGuid();
        var nowUtc = _factory.Clock.GetUtcNow();
        // Resolved 30 days ago vs default ReopenWindowDays = 14.
        var ticketId = await _factory.SeedTicketAsync(
            customerId,
            state: TicketStateNames.Resolved,
            resolvedAtUtc: nowUtc.AddDays(-30));

        var client = _factory.AuthenticatedCustomerClient(customerId);
        var resp = await client.PostAsJsonAsync(
            $"/api/customer/support-tickets/{ticketId}/reopen",
            new { Body = (string?)null });

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await resp.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("reasonCode").GetString()
            .Should().Be(TicketReasonCode.ReopenWindowClosed);
    }

    [Fact]
    public async Task Reopen_when_market_disabled_returns_409_with_reopen_disabled_for_market()
    {
        await _factory.ResetAsync();
        await SetMarketSchemaAsync("SA", reopenWindowDays: 0, maxReopenCount: 3);
        try
        {
            var customerId = Guid.NewGuid();
            var nowUtc = _factory.Clock.GetUtcNow();
            var ticketId = await _factory.SeedTicketAsync(
                customerId,
                state: TicketStateNames.Resolved,
                resolvedAtUtc: nowUtc.AddDays(-1));

            var client = _factory.AuthenticatedCustomerClient(customerId);
            var resp = await client.PostAsJsonAsync(
                $"/api/customer/support-tickets/{ticketId}/reopen",
                new { Body = (string?)null });

            resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var problem = await resp.Content.ReadFromJsonAsync<JsonElement>();
            problem.GetProperty("reasonCode").GetString()
                .Should().Be(TicketReasonCode.ReopenDisabledForMarket);
        }
        finally
        {
            await ResetMarketSchemaToDefaultsAsync();
        }
    }

    [Fact]
    public async Task Reopen_with_count_at_cap_returns_409_with_reopen_count_exceeded()
    {
        await _factory.ResetAsync();
        await ResetMarketSchemaToDefaultsAsync();
        var customerId = Guid.NewGuid();
        var nowUtc = _factory.Clock.GetUtcNow();
        // Default MaxReopenCount = 3 → already-3 ticket should reject.
        var ticketId = await _factory.SeedTicketAsync(
            customerId,
            state: TicketStateNames.Resolved,
            resolvedAtUtc: nowUtc.AddDays(-1),
            reopenCount: 3);

        var client = _factory.AuthenticatedCustomerClient(customerId);
        var resp = await client.PostAsJsonAsync(
            $"/api/customer/support-tickets/{ticketId}/reopen",
            new { Body = (string?)null });

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await resp.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("reasonCode").GetString()
            .Should().Be(TicketReasonCode.ReopenCountExceeded);
    }

    private async Task SetMarketSchemaAsync(string marketCode, int reopenWindowDays, int maxReopenCount)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportDbContext>();
        var schema = await db.MarketSchemas.FirstOrDefaultAsync(s => s.MarketCode == marketCode);
        if (schema is null)
        {
            schema = new SupportMarketSchema { MarketCode = marketCode };
            db.MarketSchemas.Add(schema);
        }
        schema.ReopenWindowDays = reopenWindowDays;
        schema.MaxReopenCount = maxReopenCount;
        schema.AutoAssignmentEnabled = false;
        schema.AutoCloseAfterResolvedDays = 7;
        schema.AttachmentMaxPerTicket = 10;
        schema.AttachmentMaxSizeMb = 10;
        schema.AttachmentCumulativeMaxMb = 50;
        schema.AllowedMimeTypes = schema.AllowedMimeTypes.Length == 0
            ? new[] { "application/pdf", "image/jpeg", "image/png" }
            : schema.AllowedMimeTypes;
        schema.UpdatedAtUtc = _factory.Clock.GetUtcNow();
        await db.SaveChangesAsync();
    }

    private Task ResetMarketSchemaToDefaultsAsync() =>
        SetMarketSchemaAsync("SA", reopenWindowDays: 14, maxReopenCount: 3);
}
