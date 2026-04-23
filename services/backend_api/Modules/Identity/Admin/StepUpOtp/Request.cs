namespace BackendApi.Modules.Identity.Admin.StepUpOtp;

public sealed record StepUpOtpRequest(string Purpose);

public sealed record StepUpOtpAcceptedResponse(Guid ChallengeId);
