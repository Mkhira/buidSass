using MediatR;

namespace BackendApi.Modules.Shared;

/// <summary>
/// Thin abstraction over the in-process notification bus that spec 022's
/// handlers use to fire <see cref="ReviewDomainEvents"/>. Production binding
/// wraps <see cref="IPublisher"/> from MediatR; tests bind a collector. Keeps
/// MediatR off the handler signatures so the bus can swap later without a
/// per-handler refactor.
/// </summary>
public interface IReviewDomainEventPublisher
{
    Task PublishAsync<T>(T notification, CancellationToken ct) where T : INotification;
}
