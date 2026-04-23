namespace BackendApi.Modules.Identity.Admin.EnrollTotp;

public sealed record EnrollTotpRequest(string PartialAuthToken);
public sealed record EnrollTotpResponse(Guid FactorId, string OtpauthUri, IReadOnlyList<string> RecoveryCodes);
