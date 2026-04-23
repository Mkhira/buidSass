using FluentValidation;

namespace BackendApi.Modules.Identity.Customer.RefreshSession;

public sealed class RefreshSessionRequestValidator : AbstractValidator<RefreshSessionRequest>
{
    public RefreshSessionRequestValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty();
    }
}
