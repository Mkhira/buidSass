using FluentValidation;

namespace BackendApi.Modules.Identity.Admin.ListAdminMfaFactors;

public sealed class ListAdminMfaFactorsRequestValidator : AbstractValidator<ListAdminMfaFactorsRequest>
{
    public ListAdminMfaFactorsRequestValidator()
    {
        RuleFor(x => x.AccountId).NotEmpty();
    }
}
