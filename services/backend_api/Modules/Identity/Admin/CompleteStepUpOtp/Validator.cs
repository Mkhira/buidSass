using FluentValidation;

namespace BackendApi.Modules.Identity.Admin.CompleteStepUpOtp;

public sealed class CompleteStepUpOtpRequestValidator : AbstractValidator<CompleteStepUpOtpRequest>
{
    public CompleteStepUpOtpRequestValidator()
    {
        RuleFor(x => x.ChallengeId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty();
    }
}
