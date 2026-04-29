namespace BackendApi.Modules.Verification.Authorization;

/// <summary>
/// Permission constants for the Verification module. These appear as
/// <c>[RequirePermission(VerificationPermissions.X)]</c> on slice endpoints and
/// are wired into role bindings by spec 015 (admin-foundation) on its PR.
/// </summary>
/// <remarks>
/// Permission scope per spec 020 research §R9:
/// <list type="bullet">
///   <item><see cref="Review"/> — open the queue, read summary, decide.</item>
///   <item><see cref="Revoke"/> — revoke an active approval.</item>
///   <item><see cref="ReadPii"/> — open the regulator identifier and document body
///         (every read is recorded via <c>IPiiAccessRecorder</c>).</item>
///   <item><see cref="ReadSummary"/> — admin customers + admin support read the
///         non-PII summary (state, market, profession class, decided-at) for cross-
///         context investigation.</item>
/// </list>
/// </remarks>
public static class VerificationPermissions
{
    public const string Review = "verification.review";
    public const string Revoke = "verification.revoke";
    public const string ReadPii = "verification.read_pii";
    public const string ReadSummary = "verification.read_summary";
}
