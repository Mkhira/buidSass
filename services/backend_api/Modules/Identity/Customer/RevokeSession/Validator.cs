using FluentValidation;

namespace BackendApi.Modules.Identity.Customer.RevokeSession;

public sealed class RevokeSessionRequestValidator : AbstractValidator<RevokeSessionRequest>
{
    public RevokeSessionRequestValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
    }
}
