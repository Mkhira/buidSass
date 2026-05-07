namespace BackendApi.Modules.Shared.Testing;

/// <summary>
/// Spec 007-b T038 / research §R3 verification harness.
///
/// Test publisher that fans out <see cref="CatalogSkuArchivedEvent"/> to all
/// in-process <see cref="ICatalogSkuArchivedSubscriber"/> instances registered
/// in the test's DI container. Used by Pricing integration tests that exercise
/// the <c>CatalogSkuArchivedHandler</c> "applies_to_broken=true" cascade
/// without coupling to spec 005's real publisher.
/// </summary>
public sealed class FakeCatalogSkuArchivedPublisher : ICatalogSkuArchivedPublisher
{
    private readonly IEnumerable<ICatalogSkuArchivedSubscriber> _subscribers;

    public FakeCatalogSkuArchivedPublisher(IEnumerable<ICatalogSkuArchivedSubscriber> subscribers)
    {
        _subscribers = subscribers;
    }

    public async Task PublishAsync(CatalogSkuArchivedEvent e, CancellationToken ct)
    {
        foreach (var subscriber in _subscribers)
        {
            await subscriber.HandleAsync(e, ct);
        }
    }
}
