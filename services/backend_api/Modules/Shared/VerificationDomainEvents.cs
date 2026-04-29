namespace BackendApi.Modules.Shared;

/// <summary>
/// In-process domain events emitted by spec 020 (Verification). Subscribed by
/// spec 025 (Notifications) once it lands; spec 020's state-write path MUST NOT
/// block on subscriber success (FR-034).
/// </summary>
public static class VerificationDomainEvents
{
    public sealed record VerificationApproved(
        Guid VerificationId,
        Guid CustomerId,
        string MarketCode,
        string LocaleHint);

    public sealed record VerificationRejected(
        Guid VerificationId,
        Guid CustomerId,
        string MarketCode,
        string Reason,
        string LocaleHint);

    public sealed record VerificationInfoRequested(
        Guid VerificationId,
        Guid CustomerId,
        string MarketCode,
        string Reason,
        string LocaleHint);

    public sealed record VerificationRevoked(
        Guid VerificationId,
        Guid CustomerId,
        string MarketCode,
        string Reason,
        string LocaleHint);

    public sealed record VerificationExpired(
        Guid VerificationId,
        Guid CustomerId,
        string MarketCode,
        string LocaleHint);

    public sealed record VerificationReminderDue(
        Guid VerificationId,
        Guid CustomerId,
        string MarketCode,
        int WindowDays,
        DateTimeOffset ExpiresAt,
        string LocaleHint);

    public sealed record VerificationSuperseded(
        Guid PriorVerificationId,
        Guid NewVerificationId,
        Guid CustomerId,
        string MarketCode);

    public sealed record VerificationVoided(
        Guid VerificationId,
        Guid CustomerId,
        string Reason);
}
