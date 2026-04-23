using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Identity.Primitives;

public sealed class ConsoleEmailDispatcher(ILogger<ConsoleEmailDispatcher> logger) : IIdentityEmailDispatcher
{
    private readonly ILogger<ConsoleEmailDispatcher> _logger = logger;

    public Task DispatchAsync(IdentityEmailDispatchRequest request, CancellationToken cancellationToken)
    {
        _ = cancellationToken;

#if DEV_OTP_SINK
        _logger.LogInformation(
            "DEV identity email dispatched. MessageId={MessageId}, Surface={Surface}, Purpose={Purpose}, Destination={Destination}, Token={Token}, CorrelationId={CorrelationId}",
            request.MessageId,
            request.Surface,
            request.Purpose,
            request.Destination,
            request.Token,
            request.CorrelationId);
#else
        _logger.LogInformation(
            "Identity email accepted by dev dispatcher. MessageId={MessageId}, Surface={Surface}, Purpose={Purpose}, Destination={Destination}, CorrelationId={CorrelationId}",
            request.MessageId,
            request.Surface,
            request.Purpose,
            request.Destination,
            request.CorrelationId);
#endif

        return Task.CompletedTask;
    }
}
