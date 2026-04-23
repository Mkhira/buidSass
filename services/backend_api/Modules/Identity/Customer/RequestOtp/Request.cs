namespace BackendApi.Modules.Identity.Customer.RequestOtp;

public sealed record RequestOtpRequest(string Phone, string Purpose);

public sealed record RequestOtpAcceptedResponse(Guid ChallengeId);
