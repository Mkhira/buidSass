namespace BackendApi.Modules.Support.Lead.ToggleAgentAvailability;

public sealed record ToggleAgentAvailabilityCommand(
    Guid AgentId,
    string MarketCode,
    bool IsOnCall,
    Guid LeadActorId);

public sealed record ToggleAgentAvailabilityResult(
    bool Success,
    bool? IsOnCall,
    DateTimeOffset? LastToggledAtUtc,
    string? ReasonCode,
    string? Detail);
