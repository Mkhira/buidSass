namespace BackendApi.Modules.Identity.Primitives;

public sealed class NotConfiguredEmailDispatcher : IIdentityEmailDispatcher
{
    public Task DispatchAsync(IdentityEmailDispatchRequest request, CancellationToken cancellationToken)
    {
        _ = request;
        _ = cancellationToken;
        throw new IdentityDeliveryNotConfiguredException("email");
    }
}
