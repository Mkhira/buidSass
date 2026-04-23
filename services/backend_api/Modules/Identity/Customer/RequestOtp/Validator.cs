using FluentValidation;

namespace BackendApi.Modules.Identity.Customer.RequestOtp;

public sealed class RequestOtpRequestValidator : AbstractValidator<RequestOtpRequest>
{
    private static readonly string[] AllowedPurposes =
    [
        "signin_customer",
        "registration_phone",
        "password_reset_phone",
        "password_reset_confirm",
        "step_up_customer",
    ];

    public RequestOtpRequestValidator()
    {
        RuleFor(x => x.Phone).NotEmpty();
        RuleFor(x => x.Purpose)
            .NotEmpty()
            .Must(purpose => AllowedPurposes.Contains(purpose, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Unsupported OTP purpose.");
    }
}
