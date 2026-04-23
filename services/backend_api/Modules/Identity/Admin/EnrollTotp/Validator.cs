using FluentValidation;

namespace BackendApi.Modules.Identity.Admin.EnrollTotp;

public sealed class EnrollTotpRequestValidator : AbstractValidator<EnrollTotpRequest>
{
    public EnrollTotpRequestValidator()
    {
        RuleFor(x => x.PartialAuthToken).NotEmpty();
    }
}
