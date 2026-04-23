using BackendApi.Modules.Identity.Admin.Common;
using BackendApi.Modules.Identity.Entities;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Identity.Admin.InviteAdmin;

public static class InviteAdminHandler
{
    public static async Task<InviteAdminHandlerResult> HandleAsync(
        InviteAdminRequest request,
        Guid invitedByAccountId,
        string correlationId,
        IdentityDbContext dbContext,
        IIdentityEmailDispatcher emailDispatcher,
        CancellationToken cancellationToken)
    {
        var role = await dbContext.Roles.SingleOrDefaultAsync(x => x.Code == request.RoleCode, cancellationToken);
        if (role is null)
        {
            return InviteAdminHandlerResult.Fail(
                StatusCodes.Status400BadRequest,
                "identity.invitation.invalid_request",
                "Invalid invitation request",
                "The requested role code does not exist.");
        }

        if (!role.System || !string.Equals(role.Scope, "platform", StringComparison.OrdinalIgnoreCase))
        {
            return InviteAdminHandlerResult.Fail(
                StatusCodes.Status400BadRequest,
                "identity.invitation.invalid_role_scope",
                "Invalid invitation role scope",
                "The requested role cannot be assigned to admin invitations.");
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;
        var hasPendingInvitation = await dbContext.AdminInvitations.AnyAsync(
            x => x.EmailNormalized == normalizedEmail
                 && x.Status == "pending"
                 && x.ExpiresAt > now,
            cancellationToken);
        if (hasPendingInvitation)
        {
            return InviteAdminHandlerResult.Fail(
                StatusCodes.Status409Conflict,
                "identity.invitation.pending_exists",
                "Pending invitation already exists",
                "A pending invitation already exists for this email address.");
        }

        var rawToken = AdminIdentityResponseFactory.CreateOpaqueToken();
        var invitation = new AdminInvitation
        {
            Id = Guid.NewGuid(),
            EmailNormalized = normalizedEmail,
            InvitedByAccountId = invitedByAccountId,
            InvitedRoleId = role.Id,
            TokenHash = AdminIdentityResponseFactory.HashString(rawToken),
            CreatedAt = now,
            ExpiresAt = now.AddDays(2),
            Status = "pending",
        };

        dbContext.AdminInvitations.Add(invitation);
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            await emailDispatcher.DispatchAsync(
                new IdentityEmailDispatchRequest(
                    MessageId: invitation.Id,
                    Surface: SurfaceKind.Admin,
                    Destination: request.Email.Trim(),
                    Purpose: "admin_invitation",
                    Token: rawToken,
                    CorrelationId: correlationId),
                cancellationToken);
        }
        catch (IdentityDeliveryNotConfiguredException)
        {
            return InviteAdminHandlerResult.Fail(
                StatusCodes.Status503ServiceUnavailable,
                "identity.email.dispatch_unavailable",
                "Email service unavailable",
                "Email delivery is temporarily unavailable.");
        }

        return InviteAdminHandlerResult.Success(invitation.Id);
    }
}

public sealed record InviteAdminHandlerResult(
    bool IsSuccess,
    Guid InvitationId,
    int StatusCode,
    string? ReasonCode,
    string? Title,
    string? Detail)
{
    public static InviteAdminHandlerResult Success(Guid invitationId) =>
        new(true, invitationId, StatusCodes.Status202Accepted, null, null, null);

    public static InviteAdminHandlerResult Fail(int statusCode, string reasonCode, string title, string detail) =>
        new(false, Guid.Empty, statusCode, reasonCode, title, detail);
}
