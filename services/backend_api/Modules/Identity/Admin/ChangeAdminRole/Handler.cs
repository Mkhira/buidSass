using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Entities;
using BackendApi.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Identity.Admin.ChangeAdminRole;

public static class ChangeAdminRoleHandler
{
    public static async Task<ChangeAdminRoleHandlerResult> HandleAsync(
        Guid targetAccountId,
        ChangeAdminRoleRequest request,
        Guid actorAccountId,
        IdentityDbContext dbContext,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var role = await dbContext.Roles.SingleOrDefaultAsync(x => x.Code == request.RoleCode, cancellationToken);
        if (role is null)
        {
            return ChangeAdminRoleHandlerResult.Fail(
                StatusCodes.Status400BadRequest,
                "identity.invitation.invalid_request",
                "Invalid role change request",
                "The target role code does not exist.");
        }

        if (!role.System || !string.Equals(role.Scope, "platform", StringComparison.OrdinalIgnoreCase))
        {
            return ChangeAdminRoleHandlerResult.Fail(
                StatusCodes.Status400BadRequest,
                "identity.invitation.invalid_role_scope",
                "Invalid role scope",
                "The requested role cannot be assigned to admin accounts.");
        }

        var targetAccount = await dbContext.Accounts.SingleOrDefaultAsync(
            x => x.Id == targetAccountId && x.Surface == "admin",
            cancellationToken);

        if (targetAccount is null)
        {
            return ChangeAdminRoleHandlerResult.Fail(
                StatusCodes.Status404NotFound,
                "identity.account.not_found",
                "Account not found",
                "The target admin account does not exist.");
        }

        var beforeRoles = await (
                from accountRole in dbContext.AccountRoles
                join r in dbContext.Roles on accountRole.RoleId equals r.Id
                where accountRole.AccountId == targetAccountId
                select r.Code)
            .ToListAsync(cancellationToken);

        var beforePermissions = await (
                from accountRole in dbContext.AccountRoles
                join rolePermission in dbContext.RolePermissions on accountRole.RoleId equals rolePermission.RoleId
                join permission in dbContext.Permissions on rolePermission.PermissionId equals permission.Id
                where accountRole.AccountId == targetAccountId
                select permission.Code)
            .Distinct()
            .ToListAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new AuditEvent(
                ActorId: actorAccountId,
                ActorRole: "admin",
                Action: "identity.admin.role.change.before",
                EntityType: nameof(AccountRole),
                EntityId: targetAccountId,
                BeforeState: new { Roles = beforeRoles },
                AfterState: null,
                Reason: "admin.role.change"),
            cancellationToken);

        var existingRows = await dbContext.AccountRoles
            .Where(x => x.AccountId == targetAccountId)
            .ToListAsync(cancellationToken);

        dbContext.AccountRoles.RemoveRange(existingRows);
        dbContext.AccountRoles.Add(new AccountRole
        {
            AccountId = targetAccountId,
            RoleId = role.Id,
            MarketCode = request.MarketCode.Trim().ToLowerInvariant(),
            GrantedByAccountId = actorAccountId,
            GrantedAt = DateTimeOffset.UtcNow,
        });
        targetAccount.PermissionVersion += 1;

        await dbContext.SaveChangesAsync(cancellationToken);

        var afterPermissions = await (
                from rolePermission in dbContext.RolePermissions
                join permission in dbContext.Permissions on rolePermission.PermissionId equals permission.Id
                where rolePermission.RoleId == role.Id
                select permission.Code)
            .Distinct()
            .ToListAsync(cancellationToken);

        var beforeSet = beforePermissions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var afterSet = afterPermissions.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var granted in afterSet.Except(beforeSet, StringComparer.OrdinalIgnoreCase))
        {
            await auditEventPublisher.PublishAsync(
                new AuditEvent(
                    ActorId: actorAccountId,
                    ActorRole: "admin",
                    Action: "identity.permission_granted",
                    EntityType: nameof(AccountRole),
                    EntityId: targetAccountId,
                    BeforeState: null,
                    AfterState: new { Permission = granted, request.MarketCode },
                    Reason: "admin.role.change"),
                cancellationToken);
        }

        foreach (var revoked in beforeSet.Except(afterSet, StringComparer.OrdinalIgnoreCase))
        {
            await auditEventPublisher.PublishAsync(
                new AuditEvent(
                    ActorId: actorAccountId,
                    ActorRole: "admin",
                    Action: "identity.permission_revoked",
                    EntityType: nameof(AccountRole),
                    EntityId: targetAccountId,
                    BeforeState: new { Permission = revoked },
                    AfterState: null,
                    Reason: "admin.role.change"),
                cancellationToken);
        }

        await auditEventPublisher.PublishAsync(
            new AuditEvent(
                ActorId: actorAccountId,
                ActorRole: "admin",
                Action: "identity.admin.role.change.after",
                EntityType: nameof(AccountRole),
                EntityId: targetAccountId,
                BeforeState: null,
                AfterState: new { Role = role.Code, request.MarketCode },
                Reason: "admin.role.change"),
            cancellationToken);

        return ChangeAdminRoleHandlerResult.Success();
    }
}

public sealed record ChangeAdminRoleHandlerResult(
    bool IsSuccess,
    int StatusCode,
    string? ReasonCode,
    string? Title,
    string? Detail)
{
    public static ChangeAdminRoleHandlerResult Success() =>
        new(true, StatusCodes.Status204NoContent, null, null, null);

    public static ChangeAdminRoleHandlerResult Fail(int statusCode, string reasonCode, string title, string detail) =>
        new(false, statusCode, reasonCode, title, detail);
}
