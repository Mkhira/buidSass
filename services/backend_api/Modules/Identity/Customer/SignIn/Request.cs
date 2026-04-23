namespace BackendApi.Modules.Identity.Customer.SignIn;

public sealed record CustomerSignInRequest(string Identifier, string Password);
