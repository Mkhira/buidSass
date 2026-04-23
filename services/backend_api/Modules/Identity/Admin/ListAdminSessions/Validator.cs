using FluentValidation;

namespace BackendApi.Modules.Identity.Admin.ListAdminSessions;

public sealed class ListAdminSessionsRequestValidator : AbstractValidator<ListAdminSessionsRequest>
{
    public ListAdminSessionsRequestValidator()
    {
        RuleFor(x => x.AccountId).NotEmpty();
    }
}
