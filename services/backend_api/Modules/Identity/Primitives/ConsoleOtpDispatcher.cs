using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Identity.Primitives;

public sealed class ConsoleOtpDispatcher(ILogger<ConsoleOtpDispatcher> logger) : IOtpChallengeDispatcher
{
    private readonly ILogger<ConsoleOtpDispatcher> _logger = logger;

    public Task DispatchAsync(OtpChallengeDispatchRequest request, CancellationToken cancellationToken)
    {
        _ = cancellationToken;

#if DEV_OTP_SINK
        _logger.LogInformation(
            "DEV OTP challenge dispatched. ChallengeId={ChallengeId}, Surface={Surface}, Purpose={Purpose}, Destination={Destination}, Code={Code}, CorrelationId={CorrelationId}",
            request.ChallengeId,
            request.Surface,
            request.Purpose,
            request.Destination,
            request.Code,
            request.CorrelationId);
#else
        _logger.LogInformation(
            "OTP challenge accepted by dev dispatcher. ChallengeId={ChallengeId}, Surface={Surface}, Purpose={Purpose}, Destination={Destination}, CorrelationId={CorrelationId}",
            request.ChallengeId,
            request.Surface,
            request.Purpose,
            request.Destination,
            request.CorrelationId);
#endif
        return Task.CompletedTask;
    }
}
