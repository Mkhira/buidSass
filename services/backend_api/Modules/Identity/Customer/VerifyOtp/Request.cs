namespace BackendApi.Modules.Identity.Customer.VerifyOtp;

public sealed record VerifyOtpRequest(Guid ChallengeId, string Identifier, string Code);
