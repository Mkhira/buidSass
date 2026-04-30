using BackendApi.Modules.Shared;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Reviews.Hooks;

/// <summary>
/// Production binding for <see cref="IReviewDomainEventPublisher"/> — fans
/// out to MediatR's <see cref="IPublisher"/>. Per FR-038, notification
/// failures MUST NOT roll back the lifecycle transition; we catch + log.
/// </summary>
public sealed class MediatRReviewDomainEventPublisher : IReviewDomainEventPublisher
{
    private readonly IPublisher _bus;
    private readonly ILogger<MediatRReviewDomainEventPublisher> _logger;

    public MediatRReviewDomainEventPublisher(
        IPublisher bus,
        ILogger<MediatRReviewDomainEventPublisher> logger)
    {
        _bus = bus;
        _logger = logger;
    }

    public async Task PublishAsync<T>(T notification, CancellationToken ct) where T : INotification
    {
        try
        {
            await _bus.Publish(notification, ct);
        }
        catch (Exception ex)
        {
            // FR-038 — never block the lifecycle on notification success.
            _logger.LogWarning(ex,
                "Review domain event {EventType} failed to publish; lifecycle transition is unaffected.",
                typeof(T).Name);
        }
    }
}

/// <summary>
/// No-op fallback. Registered alongside the MediatR binding via TryAdd so
/// tests / dev environments without MediatR registered can resolve safely.
/// </summary>
public sealed class NullReviewDomainEventPublisher : IReviewDomainEventPublisher
{
    public Task PublishAsync<T>(T notification, CancellationToken ct) where T : INotification =>
        Task.CompletedTask;
}
