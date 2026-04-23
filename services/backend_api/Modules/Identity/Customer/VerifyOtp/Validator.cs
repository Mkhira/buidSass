using FluentValidation;

namespace BackendApi.Modules.Identity.Customer.VerifyOtp;

public sealed class VerifyOtpRequestValidator : AbstractValidator<VerifyOtpRequest>
{
    public VerifyOtpRequestValidator()
    {
        RuleFor(x => x.ChallengeId).NotEmpty();
        RuleFor(x => x.Identifier).NotEmpty().MaximumLength(320);
        RuleFor(x => x.Code).NotEmpty();
    }
}
