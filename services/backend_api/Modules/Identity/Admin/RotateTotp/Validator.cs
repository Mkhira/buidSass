using FluentValidation;

namespace BackendApi.Modules.Identity.Admin.RotateTotp;

public sealed class RotateTotpRequestValidator : AbstractValidator<RotateTotpRequest>
{
    public RotateTotpRequestValidator()
    {
        RuleFor(x => x.FactorId).NotEmpty();
    }
}
