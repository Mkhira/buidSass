using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Notifications.Workers;

/// <summary>
/// T030 — OTP-priority queue worker. Pulls only OTP rows so OTP delivery is
/// not blocked by marketing-campaign send waves (BR-15 priority isolation).
/// Inherits the BR-4 retry policy from <see cref="DispatchWorker"/> verbatim;
/// the override on <see cref="IncludesOtp"/> swaps the EventKind filter.
/// </summary>
public sealed class OtpDispatchWorker : DispatchWorker
{
    public OtpDispatchWorker(
        IServiceScopeFactory scopes,
        ILoggerFactory loggerFactory,
        TimeProvider clock) : base(scopes, loggerFactory, clock)
    {
    }

    protected override bool IncludesOtp => true;
}
