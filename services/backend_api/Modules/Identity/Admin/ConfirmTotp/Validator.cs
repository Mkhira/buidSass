using FluentValidation;

namespace BackendApi.Modules.Identity.Admin.ConfirmTotp;

public sealed class ConfirmTotpRequestValidator : AbstractValidator<ConfirmTotpRequest>
{
    public ConfirmTotpRequestValidator()
    {
        RuleFor(x => x.PartialAuthToken).NotEmpty();
        RuleFor(x => x.FactorId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty();
    }
}
