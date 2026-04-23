using BackendApi.Modules.Identity.Admin.Common;

namespace BackendApi.Modules.Identity.Admin.SignIn;

public sealed record AdminSignInRequest(string Email, string Password);
public sealed record AdminSignInResponse(AdminMfaChallengeEnvelope? MfaChallenge, AdminAuthSessionResponse? AuthSession);
public sealed record AdminMfaChallengeEnvelope(Guid ChallengeId, string Kind);
