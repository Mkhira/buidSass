using FluentValidation;

namespace BackendApi.Modules.Identity.Customer.RequestPasswordReset;

public sealed class RequestPasswordResetRequestValidator : AbstractValidator<RequestPasswordResetRequest>
{
    public RequestPasswordResetRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
    }
}
