using System.Collections.Concurrent;
using MediatR;

namespace BackendApi.Modules.Shared.Testing;

/// <summary>
/// In-process collector for <see cref="IReviewDomainEventPublisher"/> that
/// captures every published <see cref="INotification"/> for later assertion.
/// Thread-safe; suitable for both serial test methods and parallel-fanout
/// scenarios (the threshold-transition path can race during concurrent
/// reports).
/// </summary>
public sealed class FakeReviewDomainEventCollector : IReviewDomainEventPublisher
{
    private readonly ConcurrentBag<INotification> _events = new();

    public Task PublishAsync<T>(T notification, CancellationToken ct) where T : INotification
    {
        _events.Add(notification);
        return Task.CompletedTask;
    }

    public IReadOnlyList<INotification> All => _events.ToArray();

    public IReadOnlyList<T> OfType<T>() where T : INotification =>
        _events.OfType<T>().ToArray();

    public void Clear()
    {
        while (_events.TryTake(out _)) { }
    }
}
