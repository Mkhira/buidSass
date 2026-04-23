using FluentValidation;

namespace BackendApi.Modules.Identity.Customer.SetLocale;

public sealed class SetLocaleRequestValidator : AbstractValidator<SetLocaleRequest>
{
    public SetLocaleRequestValidator()
    {
        RuleFor(x => x.Locale)
            .NotEmpty()
            .Must(locale => locale is "ar" or "en")
            .WithMessage("Locale must be either 'ar' or 'en'.");
    }
}
