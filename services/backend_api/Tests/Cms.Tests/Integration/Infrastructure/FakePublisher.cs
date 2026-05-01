using System.Collections.Concurrent;
using MediatR;

namespace Cms.Tests.Integration.Infrastructure;

public sealed class FakePublisher : IPublisher
{
    public ConcurrentBag<INotification> Events { get; } = new();

    public Task Publish(object notification, CancellationToken cancellationToken = default)
    {
        if (notification is INotification n) Events.Add(n);
        return Task.CompletedTask;
    }

    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        Events.Add(notification);
        return Task.CompletedTask;
    }
}
