namespace BackendApi.Modules.Shared;

/// <summary>
/// Subscriber contract for spec 004 (Identity) account-lifecycle events. Spec 004
/// publishes; spec 020's <c>AccountLifecycleHandler</c> subscribes (and can be
/// reused by future specs).
///
/// All implementations MUST be idempotent — an event may be redelivered after
/// crash recovery or transient bus failures.
/// </summary>
public interface ICustomerAccountLifecycleSubscriber
{
    Task OnAccountLockedAsync(CustomerAccountLocked evt, CancellationToken ct);
    Task OnAccountDeletedAsync(CustomerAccountDeleted evt, CancellationToken ct);
    Task OnMarketChangedAsync(CustomerMarketChanged evt, CancellationToken ct);
}

/// <summary>
/// Customer was locked (transient — distinct from deletion). Spec 020 voids any
/// non-terminal verification on receipt; spec 022/023 mute notifications.
/// </summary>
public sealed record CustomerAccountLocked(Guid CustomerId, string Reason, DateTimeOffset OccurredAt);

/// <summary>
/// Customer was hard-deleted. Spec 020 voids; entity rows are RETAINED so audit
/// integrity holds (PII fields are scheduled for purge per market retention).
/// </summary>
public sealed record CustomerAccountDeleted(Guid CustomerId, DateTimeOffset OccurredAt);

/// <summary>
/// Customer's market-of-record changed (rare — moving residence). Spec 020 voids
/// the prior-market verification because eligibility scope is per market.
/// </summary>
public sealed record CustomerMarketChanged(
    Guid CustomerId,
    string FromMarket,
    string ToMarket,
    Guid ChangedBy,
    DateTimeOffset OccurredAt);
