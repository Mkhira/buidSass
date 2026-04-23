using FluentValidation;

namespace BackendApi.Modules.Identity.Admin.ChangeAdminRole;

public sealed class ChangeAdminRoleRequestValidator : AbstractValidator<ChangeAdminRoleRequest>
{
    public ChangeAdminRoleRequestValidator()
    {
        RuleFor(x => x.RoleCode).NotEmpty();
        RuleFor(x => x.MarketCode).NotEmpty();
    }
}
