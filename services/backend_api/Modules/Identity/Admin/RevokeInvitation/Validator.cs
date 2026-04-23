using FluentValidation;

namespace BackendApi.Modules.Identity.Admin.RevokeInvitation;

public sealed class RevokeInvitationRequestValidator : AbstractValidator<RevokeInvitationRequest>
{
    public RevokeInvitationRequestValidator()
    {
        RuleFor(x => x.InvitationId).NotEmpty();
    }
}
