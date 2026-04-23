namespace BackendApi.Modules.Identity.Customer.ChangePassword;

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
