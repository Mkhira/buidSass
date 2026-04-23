using FluentValidation;

namespace BackendApi.Modules.Identity.Admin.SignIn;

public sealed class AdminSignInRequestValidator : AbstractValidator<AdminSignInRequest>
{
    public AdminSignInRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}
