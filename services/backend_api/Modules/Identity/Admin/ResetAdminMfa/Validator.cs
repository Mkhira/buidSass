using FluentValidation;

namespace BackendApi.Modules.Identity.Admin.ResetAdminMfa;

public sealed class ResetAdminMfaRequestValidator : AbstractValidator<ResetAdminMfaRequest>
{
    public ResetAdminMfaRequestValidator()
    {
        RuleFor(x => x.AccountId).NotEmpty();
    }
}
