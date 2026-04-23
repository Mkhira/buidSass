using FluentValidation;

namespace BackendApi.Modules.Identity.Admin.AcceptInvitation;

public sealed class AcceptInvitationRequestValidator : AbstractValidator<AcceptInvitationRequest>
{
    public AcceptInvitationRequestValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .MinimumLength(12)
            .Must(HasAtLeastThreeCharacterClasses)
            .WithMessage("Password must include characters from at least three classes (lower, upper, digit, symbol).");
    }

    private static bool HasAtLeastThreeCharacterClasses(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return false;
        }

        var classes = 0;
        if (password.Any(char.IsLower)) classes++;
        if (password.Any(char.IsUpper)) classes++;
        if (password.Any(char.IsDigit)) classes++;
        if (password.Any(ch => !char.IsLetterOrDigit(ch))) classes++;
        return classes >= 3;
    }
}
