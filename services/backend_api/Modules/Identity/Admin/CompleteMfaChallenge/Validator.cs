using FluentValidation;

namespace BackendApi.Modules.Identity.Admin.CompleteMfaChallenge;

public sealed class CompleteMfaChallengeRequestValidator : AbstractValidator<CompleteMfaChallengeRequest>
{
    public CompleteMfaChallengeRequestValidator()
    {
        RuleFor(x => x.ChallengeId).NotEmpty();
        RuleFor(x => x.Kind)
            .NotEmpty()
            .Must(kind =>
                string.Equals(kind, "totp", StringComparison.OrdinalIgnoreCase)
                || string.Equals(kind, "recovery_code", StringComparison.OrdinalIgnoreCase));
        RuleFor(x => x.Code).NotEmpty();
    }
}
