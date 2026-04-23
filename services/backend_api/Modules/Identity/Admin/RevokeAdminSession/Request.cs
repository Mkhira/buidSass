namespace BackendApi.Modules.Identity.Admin.RevokeAdminSession;

public sealed record RevokeAdminSessionRequest(Guid AccountId, Guid SessionId);
