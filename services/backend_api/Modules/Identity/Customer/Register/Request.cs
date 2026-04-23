namespace BackendApi.Modules.Identity.Customer.Register;

public sealed record RegisterRequest(
    string Email,
    string Phone,
    string Password,
    string MarketCode,
    string Locale,
    string DisplayName);

public sealed record RegisterAcceptedResponse(string Status);
