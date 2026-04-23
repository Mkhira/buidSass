using FluentValidation;

namespace BackendApi.Modules.Identity.Customer.SignIn;

public sealed class CustomerSignInRequestValidator : AbstractValidator<CustomerSignInRequest>
{
    public CustomerSignInRequestValidator()
    {
        RuleFor(x => x.Identifier).NotEmpty().MaximumLength(320);
        RuleFor(x => x.Password).NotEmpty();
    }
}
