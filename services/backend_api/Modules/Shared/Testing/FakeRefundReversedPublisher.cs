namespace BackendApi.Modules.Shared.Testing;

/// <summary>
/// Test publisher for <see cref="RefundReversedEvent"/> — fans out to every
/// registered <see cref="IRefundReversedSubscriber"/>. See
/// <see cref="FakeRefundCompletedPublisher"/> for the symmetric companion.
/// </summary>
public sealed class FakeRefundReversedPublisher : IRefundReversedPublisher
{
    private readonly IEnumerable<IRefundReversedSubscriber> _subscribers;

    public FakeRefundReversedPublisher(IEnumerable<IRefundReversedSubscriber> subscribers)
    {
        _subscribers = subscribers;
    }

    public async Task PublishAsync(RefundReversedEvent evt, CancellationToken ct)
    {
        foreach (var subscriber in _subscribers)
        {
            await subscriber.OnRefundReversedAsync(evt, ct);
        }
    }
}
