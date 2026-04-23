using FluentValidation;

namespace BackendApi.Modules.Identity.Customer.SignOut;

public sealed class SignOutRequestValidator : AbstractValidator<SignOutRequest>
{
    public SignOutRequestValidator()
    {
        RuleFor(x => x.RefreshToken)
            .MaximumLength(2048)
            .When(x => !string.IsNullOrWhiteSpace(x.RefreshToken));
    }
}
