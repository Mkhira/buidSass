using FluentValidation;

namespace BackendApi.Modules.Identity.Admin.RevokeAdminSession;

public sealed class RevokeAdminSessionRequestValidator : AbstractValidator<RevokeAdminSessionRequest>
{
    public RevokeAdminSessionRequestValidator()
    {
        RuleFor(x => x.AccountId).NotEmpty();
        RuleFor(x => x.SessionId).NotEmpty();
    }
}
