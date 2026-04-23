using FluentValidation;

namespace BackendApi.Modules.Identity.Customer.Register;

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Phone).NotEmpty();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(10);
        RuleFor(x => x.MarketCode).NotEmpty();
        RuleFor(x => x.Locale)
            .NotEmpty()
            .Must(locale => locale is "ar" or "en")
            .WithMessage("Locale must be either 'ar' or 'en'.");
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(200);
    }
}
