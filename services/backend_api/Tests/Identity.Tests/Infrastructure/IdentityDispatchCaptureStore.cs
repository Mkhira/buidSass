using BackendApi.Modules.Identity.Primitives;

namespace Identity.Tests.Infrastructure;

public sealed class IdentityDispatchCaptureStore
{
    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, string> _otpCodesByChallenge = [];
    private readonly List<IdentityEmailDispatchRequest> _emailMessages = [];

    public void Clear()
    {
        lock (_lock)
        {
            _otpCodesByChallenge.Clear();
            _emailMessages.Clear();
        }
    }

    public void CaptureOtp(OtpChallengeDispatchRequest request)
    {
        lock (_lock)
        {
            _otpCodesByChallenge[request.ChallengeId] = request.Code;
        }
    }

    public void CaptureEmail(IdentityEmailDispatchRequest request)
    {
        lock (_lock)
        {
            _emailMessages.Add(request);
        }
    }

    public string RequireOtpCode(Guid challengeId)
    {
        lock (_lock)
        {
            if (_otpCodesByChallenge.TryGetValue(challengeId, out var code))
            {
                return code;
            }
        }

        throw new InvalidOperationException($"No OTP code captured for challenge '{challengeId}'.");
    }

    public string RequireLatestEmailToken(string destination, string purpose)
    {
        lock (_lock)
        {
            var token = _emailMessages
                .LastOrDefault(x => x.Destination.Equals(destination, StringComparison.OrdinalIgnoreCase)
                                    && x.Purpose.Equals(purpose, StringComparison.OrdinalIgnoreCase))
                ?.Token;

            if (!string.IsNullOrWhiteSpace(token))
            {
                return token;
            }
        }

        throw new InvalidOperationException(
            $"No email token captured for destination '{destination}' and purpose '{purpose}'.");
    }

    public int CountEmailDispatches(string destination, string purpose)
    {
        lock (_lock)
        {
            return _emailMessages.Count(
                x => x.Destination.Equals(destination, StringComparison.OrdinalIgnoreCase)
                     && x.Purpose.Equals(purpose, StringComparison.OrdinalIgnoreCase));
        }
    }
}

public sealed class TestOtpChallengeDispatcher(IdentityDispatchCaptureStore store) : IOtpChallengeDispatcher
{
    private readonly IdentityDispatchCaptureStore _store = store;

    public Task DispatchAsync(OtpChallengeDispatchRequest request, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        _store.CaptureOtp(request);
        return Task.CompletedTask;
    }
}

public sealed class TestIdentityEmailDispatcher(IdentityDispatchCaptureStore store) : IIdentityEmailDispatcher
{
    private readonly IdentityDispatchCaptureStore _store = store;

    public Task DispatchAsync(IdentityEmailDispatchRequest request, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        _store.CaptureEmail(request);
        return Task.CompletedTask;
    }
}
