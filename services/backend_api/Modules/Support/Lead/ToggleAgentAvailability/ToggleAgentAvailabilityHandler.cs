using BackendApi.Modules.Shared;
using BackendApi.Modules.Support.Entities;
using BackendApi.Modules.Support.Persistence;
using BackendApi.Modules.Support.Primitives;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Support.Lead.ToggleAgentAvailability;

/// <summary>
/// Spec 023 Phase 10 T121 — flips an agent's <c>is_on_call</c> boolean for a
/// given market per FR-019a (Clarification Q1). Idempotent — toggle to the
/// already-set value is a no-op (returns the existing row unchanged).
/// </summary>
public sealed class ToggleAgentAvailabilityHandler
{
    private readonly SupportDbContext _db;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _clock;

    public ToggleAgentAvailabilityHandler(SupportDbContext db, IPublisher publisher, TimeProvider clock)
    {
        _db = db;
        _publisher = publisher;
        _clock = clock;
    }

    public async Task<ToggleAgentAvailabilityResult> HandleAsync(
        ToggleAgentAvailabilityCommand cmd, CancellationToken ct)
    {
        if (cmd.AgentId == Guid.Empty)
        {
            return Failure(TicketReasonCode.TargetAgentNotInMarket, "agent_id is required.");
        }
        if (string.IsNullOrWhiteSpace(cmd.MarketCode))
        {
            return Failure(TicketReasonCode.MarketInvalid, "market_code is required.");
        }

        var nowUtc = _clock.GetUtcNow();
        var row = await _db.AgentAvailability
            .FirstOrDefaultAsync(r => r.AgentId == cmd.AgentId
                                   && r.MarketCode == cmd.MarketCode, ct);

        bool changed;
        if (row is null)
        {
            row = new SupportAgentAvailability
            {
                AgentId = cmd.AgentId,
                MarketCode = cmd.MarketCode,
                IsOnCall = cmd.IsOnCall,
                LastToggledAtUtc = nowUtc,
                LastToggledByActorId = cmd.LeadActorId,
            };
            _db.AgentAvailability.Add(row);
            changed = true;
        }
        else if (row.IsOnCall != cmd.IsOnCall)
        {
            row.IsOnCall = cmd.IsOnCall;
            row.LastToggledAtUtc = nowUtc;
            row.LastToggledByActorId = cmd.LeadActorId;
            changed = true;
        }
        else
        {
            // Idempotent — no DB write, no event.
            return new ToggleAgentAvailabilityResult(
                Success: true,
                IsOnCall: row.IsOnCall,
                LastToggledAtUtc: row.LastToggledAtUtc,
                ReasonCode: null,
                Detail: "No-op — availability already at requested value.");
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Failure(TicketReasonCode.VersionConflict,
                "Availability row was modified concurrently; retry the request.");
        }

        if (changed)
        {
            await _publisher.Publish(new TicketAgentAvailabilityChanged(
                AgentId: cmd.AgentId,
                MarketCode: cmd.MarketCode,
                IsOnCall: cmd.IsOnCall,
                OccurredAtUtc: nowUtc), ct);
        }

        return new ToggleAgentAvailabilityResult(
            Success: true,
            IsOnCall: row.IsOnCall,
            LastToggledAtUtc: row.LastToggledAtUtc,
            ReasonCode: null,
            Detail: null);
    }

    private static ToggleAgentAvailabilityResult Failure(string reasonCode, string detail) =>
        new(false, null, null, reasonCode, detail);
}
