namespace BackendApi.Modules.Identity.Admin.AcceptInvitation;

public sealed record AcceptInvitationRequest(string Token, string NewPassword);
public sealed record AcceptInvitationResponse(string PartialAuthToken);
