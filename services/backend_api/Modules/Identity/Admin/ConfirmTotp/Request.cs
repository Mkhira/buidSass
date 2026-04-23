namespace BackendApi.Modules.Identity.Admin.ConfirmTotp;

public sealed record ConfirmTotpRequest(string PartialAuthToken, Guid FactorId, string Code);
