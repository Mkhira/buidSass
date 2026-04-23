namespace BackendApi.Modules.Identity.Primitives;

public sealed class NotConfiguredOtpDispatcher : IOtpChallengeDispatcher
{
    public Task DispatchAsync(OtpChallengeDispatchRequest request, CancellationToken cancellationToken)
    {
        _ = request;
        _ = cancellationToken;
        throw new IdentityDeliveryNotConfiguredException("otp");
    }
}
