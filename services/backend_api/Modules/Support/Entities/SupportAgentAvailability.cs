namespace BackendApi.Modules.Support.Entities;

/// <summary>
/// V1 minimal availability per (agent_id, market_code) — just an
/// <c>is_on_call</c> boolean (Clarification Q1, FR-019a). Full shift planner
/// deferred to Phase 1.5.
/// </summary>
public sealed class SupportAgentAvailability
{
    public Guid AgentId { get; set; }
    public string MarketCode { get; set; } = string.Empty;
    public bool IsOnCall { get; set; }
    public DateTimeOffset LastToggledAtUtc { get; set; }
    public Guid? LastToggledByActorId { get; set; }
}
