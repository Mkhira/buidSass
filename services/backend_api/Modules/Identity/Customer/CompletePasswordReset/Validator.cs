using FluentValidation;

namespace BackendApi.Modules.Identity.Customer.CompletePasswordReset;

public sealed class CompletePasswordResetRequestValidator : AbstractValidator<CompletePasswordResetRequest>
{
    public CompletePasswordResetRequestValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(10);
    }
}
