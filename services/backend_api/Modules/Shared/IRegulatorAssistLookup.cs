namespace BackendApi.Modules.Shared;

/// <summary>
/// Optional lookup against an external regulator (e.g., SCFHS, Egyptian Medical
/// Syndicate) to assist a reviewer's manual decision. Per spec 020 FR-016a /
/// FR-016b:
/// <list type="bullet">
///   <item>NEVER blocks a state transition.</item>
///   <item>NEVER auto-decides — always advisory.</item>
///   <item>V1 ships <see cref="NullRegulatorAssistLookup"/> returning <c>null</c>;
///         Phase 1.5+ may swap a real adapter without contract changes.</item>
/// </list>
/// </summary>
public interface IRegulatorAssistLookup
{
    /// <summary>
    /// Returns the regulator's view of an identifier or <c>null</c> when the
    /// regulator is unreachable, the identifier was not found, or the
    /// implementation is the V1 null-default.
    /// </summary>
    Task<RegulatorAssistResult?> LookupAsync(
        string marketCode,
        string regulatorIdentifier,
        CancellationToken ct);
}

/// <param name="RegisterFound">True when the regulator confirms the identifier exists.</param>
/// <param name="Status">Provider-dependent — typically "active", "suspended", "expired".</param>
/// <param name="IssuedDate">When the credential was issued (UTC date).</param>
/// <param name="ExpiryDate">Regulator-side expiry (UTC date) — informational only; spec 020 owns its own expiry.</param>
/// <param name="FullNameInRegister">For reviewer cross-check; surfaced in the admin queue but never persisted on the verification row (PII boundary).</param>
public sealed record RegulatorAssistResult(
    bool RegisterFound,
    string? Status,
    DateOnly? IssuedDate,
    DateOnly? ExpiryDate,
    string? FullNameInRegister);

/// <summary>
/// V1 default DI binding. Always returns <c>null</c>. The reviewer UI surfaces
/// "no assistive lookup available" when the result is null.
/// </summary>
public sealed class NullRegulatorAssistLookup : IRegulatorAssistLookup
{
    public Task<RegulatorAssistResult?> LookupAsync(
        string marketCode,
        string regulatorIdentifier,
        CancellationToken ct)
        => Task.FromResult<RegulatorAssistResult?>(null);
}
