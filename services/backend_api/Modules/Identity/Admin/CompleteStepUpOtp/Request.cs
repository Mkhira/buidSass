namespace BackendApi.Modules.Identity.Admin.CompleteStepUpOtp;

public sealed record CompleteStepUpOtpRequest(Guid ChallengeId, string Code);

public sealed record CompleteStepUpOtpResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    DateTimeOffset StepUpValidUntil);
