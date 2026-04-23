using FluentValidation;

namespace BackendApi.Modules.Identity.Admin.StepUpOtp;

public sealed class StepUpOtpRequestValidator : AbstractValidator<StepUpOtpRequest>
{
    public StepUpOtpRequestValidator()
    {
        RuleFor(x => x.Purpose).NotEmpty().MaximumLength(120);
    }
}
