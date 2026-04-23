namespace BackendApi.Modules.Identity.Primitives;

public interface IOtpChallengeDispatcher
{
    Task DispatchAsync(OtpChallengeDispatchRequest request, CancellationToken cancellationToken);
}

public sealed record OtpChallengeDispatchRequest(
    Guid ChallengeId,
    SurfaceKind Surface,
    string Destination,
    string Purpose,
    string Code,
    string CorrelationId);
