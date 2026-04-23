using FluentValidation;

namespace BackendApi.Modules.Identity.Customer.ConfirmEmail;

public sealed class ConfirmEmailRequestValidator : AbstractValidator<ConfirmEmailRequest>
{
    public ConfirmEmailRequestValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
    }
}
