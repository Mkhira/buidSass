namespace BackendApi.Modules.Identity.Primitives;

public interface IIdentityEmailDispatcher
{
    Task DispatchAsync(IdentityEmailDispatchRequest request, CancellationToken cancellationToken);
}

public sealed record IdentityEmailDispatchRequest(
    Guid MessageId,
    SurfaceKind Surface,
    string Destination,
    string Purpose,
    string Token,
    string CorrelationId);
