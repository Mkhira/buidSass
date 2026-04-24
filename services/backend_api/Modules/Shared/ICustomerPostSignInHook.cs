namespace BackendApi.Modules.Shared;

/// <summary>
/// A hook invoked after a successful customer sign-in. Lives in `Shared` so both Identity (the
/// producer) and consumer modules (e.g. Cart for login-merge) can reference it without forming
/// a module dependency cycle. Failures inside a hook MUST NOT abort the sign-in — implementations
/// log and swallow.
/// </summary>
public interface ICustomerPostSignInHook
{
    Task OnSignedInAsync(CustomerPostSignInContext context, CancellationToken cancellationToken);
}

public sealed record CustomerPostSignInContext(
    Guid AccountId,
    string MarketCode,
    string? CartToken);
