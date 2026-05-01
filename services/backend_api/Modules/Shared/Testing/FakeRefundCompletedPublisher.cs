namespace BackendApi.Modules.Shared.Testing;

/// <summary>
/// Test publisher that fans out <see cref="RefundCompletedEvent"/> to all
/// in-process <see cref="IRefundCompletedSubscriber"/> instances registered
/// in the test's DI container. Used by integration tests that exercise spec
/// 022's <c>RefundCompletedHandler</c> auto-hide cascade without coupling
/// to the spec 013 publisher.
/// </summary>
public sealed class FakeRefundCompletedPublisher : IRefundCompletedPublisher
{
    private readonly IEnumerable<IRefundCompletedSubscriber> _subscribers;

    public FakeRefundCompletedPublisher(IEnumerable<IRefundCompletedSubscriber> subscribers)
    {
        _subscribers = subscribers;
    }

    public async Task PublishAsync(RefundCompletedEvent evt, CancellationToken ct)
    {
        foreach (var subscriber in _subscribers)
        {
            await subscriber.OnRefundCompletedAsync(evt, ct);
        }
    }
}
