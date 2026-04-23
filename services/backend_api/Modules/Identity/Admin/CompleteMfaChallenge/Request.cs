namespace BackendApi.Modules.Identity.Admin.CompleteMfaChallenge;

public sealed record CompleteMfaChallengeRequest(Guid ChallengeId, string Kind, string Code);
