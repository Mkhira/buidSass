namespace BackendApi.Modules.Shared;

/// <summary>
/// Spec 007-b cross-module hook (data-model §7).
/// Spec 021 publishes <see cref="B2BCompanySuspendedEvent"/> when a B2B company is suspended.
/// Pricing subscribes to mark referencing BusinessPricingRows with <c>company_link_broken=true</c>.
/// </summary>
public interface IB2BCompanySuspendedSubscriber
{
    Task HandleAsync(B2BCompanySuspendedEvent e, CancellationToken ct);
}

public interface IB2BCompanySuspendedPublisher
{
    Task PublishAsync(B2BCompanySuspendedEvent e, CancellationToken ct);
}

public sealed record B2BCompanySuspendedEvent(
    Guid CompanyId,
    DateTimeOffset SuspendedAtUtc,
    Guid ActorId,
    string ReasonNote);
