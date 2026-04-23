namespace BackendApi.Modules.Identity.Customer.CompletePasswordReset;

public sealed record CompletePasswordResetRequest(string Token, string NewPassword);
