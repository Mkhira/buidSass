namespace BackendApi.Modules.AuditLog;

public sealed record AuditEvent(
    Guid ActorId,
    string ActorRole,
    string Action,
    string EntityType,
    Guid EntityId,
    object? BeforeState,
    object? AfterState,
    string? Reason
);
