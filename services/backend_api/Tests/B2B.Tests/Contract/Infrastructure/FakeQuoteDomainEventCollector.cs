using System.Collections.Concurrent;
using BackendApi.Modules.Shared;
using MediatR;

namespace B2B.Tests.Contract.Infrastructure;

/// <summary>
/// Test-only sink that captures every spec 021 quote-lifecycle domain event.
/// Implements <see cref="INotificationHandler{T}"/> for each <c>QuoteDomainEvents</c>
/// record so MediatR's notification fan-out delivers events here when registered as a
/// transient handler against each event type (see <c>B2BApiFactory.ConfigureWebHost</c>).
///
/// Thread-safe: <see cref="ConcurrentBag{T}"/> backs the underlying store. Test methods
/// can call <see cref="Clear"/> between fixtures (xUnit class fixtures are shared across
/// test methods; the collector is the single state that needs resetting).
/// </summary>
public sealed class FakeQuoteDomainEventCollector :
    INotificationHandler<QuoteRequested>,
    INotificationHandler<QuotePublished>,
    INotificationHandler<QuotePendingApprover>,
    INotificationHandler<QuoteAccepted>,
    INotificationHandler<QuoteRejected>,
    INotificationHandler<QuoteApproverRejected>,
    INotificationHandler<QuoteExpired>,
    INotificationHandler<QuoteWithdrawn>
{
    private readonly ConcurrentBag<INotification> _events = new();

    public IReadOnlyList<INotification> All => _events.ToArray();

    public IReadOnlyList<T> OfType<T>() where T : INotification =>
        _events.OfType<T>().ToArray();

    public void Clear()
    {
        while (_events.TryTake(out _)) { }
    }

    public Task Handle(QuoteRequested notification, CancellationToken cancellationToken)
    {
        _events.Add(notification);
        return Task.CompletedTask;
    }

    public Task Handle(QuotePublished notification, CancellationToken cancellationToken)
    {
        _events.Add(notification);
        return Task.CompletedTask;
    }

    public Task Handle(QuotePendingApprover notification, CancellationToken cancellationToken)
    {
        _events.Add(notification);
        return Task.CompletedTask;
    }

    public Task Handle(QuoteAccepted notification, CancellationToken cancellationToken)
    {
        _events.Add(notification);
        return Task.CompletedTask;
    }

    public Task Handle(QuoteRejected notification, CancellationToken cancellationToken)
    {
        _events.Add(notification);
        return Task.CompletedTask;
    }

    public Task Handle(QuoteApproverRejected notification, CancellationToken cancellationToken)
    {
        _events.Add(notification);
        return Task.CompletedTask;
    }

    public Task Handle(QuoteExpired notification, CancellationToken cancellationToken)
    {
        _events.Add(notification);
        return Task.CompletedTask;
    }

    public Task Handle(QuoteWithdrawn notification, CancellationToken cancellationToken)
    {
        _events.Add(notification);
        return Task.CompletedTask;
    }
}
