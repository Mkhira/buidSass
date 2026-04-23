using FluentValidation;

namespace BackendApi.Modules.Identity.Admin.InviteAdmin;

public sealed class InviteAdminRequestValidator : AbstractValidator<InviteAdminRequest>
{
    public InviteAdminRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.RoleCode).NotEmpty();
    }
}
