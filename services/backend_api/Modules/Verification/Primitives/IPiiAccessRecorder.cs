namespace BackendApi.Modules.Verification.Primitives;

/// <summary>
/// Single chokepoint for FR-015a-e PII-access compliance per spec 020 research §R13.
/// Every read of a regulator identifier or document body MUST flow through this
/// recorder so the <c>verification.pii_access</c> audit-event row is written.
/// Internal-only — never re-exported from <c>Modules/Shared/</c>.
/// </summary>
public interface IPiiAccessRecorder
{
    /// <summary>
    /// Records a single PII-access event. Idempotent if the same
    /// (verificationId, documentId?, kind, surface, actor, request) tuple is
    /// recorded twice within the same request — the platform audit middleware
    /// dedupes on a request-correlation token.
    /// </summary>
    Task RecordAsync(
        PiiAccessKind kind,
        Guid verificationId,
        Guid? documentId,
        CancellationToken ct);
}

/// <summary>
/// What kind of PII was accessed. Maps 1:1 to the <c>kind</c> field on the
/// <c>verification.pii_access</c> audit event payload (data-model §5).
/// </summary>
public enum PiiAccessKind
{
    LicenseNumberRead,
    DocumentBodyRead,
    DocumentMetadataRead,
}

public static class PiiAccessKindExtensions
{
    public static string ToWireValue(this PiiAccessKind kind) => kind switch
    {
        PiiAccessKind.LicenseNumberRead => "LicenseNumberRead",
        PiiAccessKind.DocumentBodyRead => "DocumentBodyRead",
        PiiAccessKind.DocumentMetadataRead => "DocumentMetadataRead",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
