namespace BackendApi.Modules.Shared.Testing;

/// <summary>
/// Spec 007-b T038 / research §R3 verification harness.
///
/// Test publisher that fans out <see cref="B2BCompanySuspendedEvent"/> to all
/// in-process <see cref="IB2BCompanySuspendedSubscriber"/> instances registered
/// in the test's DI container. Used by Pricing integration tests that exercise
/// the <c>B2BCompanySuspendedHandler</c> "company_link_broken=true" cascade
/// without coupling to spec 021's real publisher.
/// </summary>
public sealed class FakeB2BCompanySuspendedPublisher : IB2BCompanySuspendedPublisher
{
    private readonly IEnumerable<IB2BCompanySuspendedSubscriber> _subscribers;

    public FakeB2BCompanySuspendedPublisher(IEnumerable<IB2BCompanySuspendedSubscriber> subscribers)
    {
        _subscribers = subscribers;
    }

    public async Task PublishAsync(B2BCompanySuspendedEvent e, CancellationToken ct)
    {
        foreach (var subscriber in _subscribers)
        {
            await subscriber.HandleAsync(e, ct);
        }
    }
}
