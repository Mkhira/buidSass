namespace BackendApi.Modules.Pricing.Primitives;

public sealed record ExplanationRow(
    string Layer,
    string? RuleId,
    string? RuleKind,
    long AppliedMinor,
    string? ReasonCode);
