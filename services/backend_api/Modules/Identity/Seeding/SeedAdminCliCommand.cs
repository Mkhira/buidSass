using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Admin.Common;
using BackendApi.Modules.Identity.Entities;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BackendApi.Modules.Identity.Seeding;

public static class SeedAdminCliCommand
{
    private static readonly Guid SystemActorId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public const string Verb = "seed-admin";

    public static async Task<int> RunAsync(WebApplication app, string[] args, CancellationToken ct)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("seed-admin-cli");
        var email = TryReadArg(args, "--email");
        if (app.Environment.IsDevelopment())
        {
            logger.LogError("The seed-admin command is disabled in Development. Use the seeding framework instead.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            logger.LogError("Missing required argument --email.");
            return 1;
        }

        var initialPassword = await ResolveInitialPasswordAsync(args, logger, ct);
        if (string.IsNullOrWhiteSpace(initialPassword))
        {
            return 1;
        }

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<Argon2idHasher>();
        var breachListChecker = scope.ServiceProvider.GetRequiredService<BreachListChecker>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditEventPublisher>();

        if (!ValidateInitialPassword(initialPassword, breachListChecker, out var passwordValidationError))
        {
            logger.LogError("{ValidationError}", passwordValidationError);
            return 1;
        }

        var existingSuperAdmin = await (
                from existingAccount in db.Accounts
                join accountRole in db.AccountRoles on existingAccount.Id equals accountRole.AccountId
                join role in db.Roles on accountRole.RoleId equals role.Id
                where existingAccount.Surface == "admin"
                      && role.Code == "platform.super_admin"
                orderby existingAccount.CreatedAt
                select existingAccount)
            .FirstOrDefaultAsync(ct);

        if (existingSuperAdmin is not null)
        {
            logger.LogError("A platform super-admin already exists. Refusing to reprovision.");
            return 1;
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Surface = "admin",
            MarketCode = "platform",
            CreatedAt = now,
        };

        account.EmailNormalized = normalizedEmail;
        account.EmailDisplay = email;
        account.PasswordHash = hasher.HashPassword(initialPassword, SurfaceKind.Admin);
        account.PasswordHashVersion = 1;
        account.Status = "pending_password_rotation";
        account.EmailVerifiedAt = now;
        account.Locale = "en";
        account.UpdatedAt = now;

        db.Accounts.Add(account);

        var provisioningToken = AdminIdentityResponseFactory.CreateOpaqueToken();
        db.AdminMfaFactors.Add(new AdminMfaFactor
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Kind = "totp",
            SecretEncrypted = [],
            ProvisioningTokenHash = AdminIdentityResponseFactory.HashString(provisioningToken),
            ProvisioningTokenExpiresAt = now.AddHours(24),
            ConfirmedAt = null,
            CreatedAt = now,
            RecoveryCodesHash = "[]",
        });

        var roleId = await db.Roles
            .Where(x => x.Code == "platform.super_admin")
            .Select(x => x.Id)
            .SingleAsync(ct);

        var mappingExists = await db.AccountRoles.AnyAsync(
            x => x.AccountId == account.Id && x.RoleId == roleId && x.MarketCode == "platform",
            ct);

        if (!mappingExists)
        {
            db.AccountRoles.Add(new AccountRole
            {
                AccountId = account.Id,
                RoleId = roleId,
                MarketCode = "platform",
                GrantedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);

        await audit.PublishAsync(new AuditEvent(
            ActorId: SystemActorId,
            ActorRole: "system.cli",
            Action: "identity.admin.bootstrap",
            EntityType: nameof(Account),
            EntityId: account.Id,
            BeforeState: null,
            AfterState: new { account.EmailNormalized, account.Surface, account.MarketCode },
            Reason: "create"), ct);

        logger.LogInformation("Super-admin bootstrap complete for {Email}.", account.EmailDisplay);
        return 0;
    }

    private static string? TryReadArg(string[] args, string key)
    {
        var prefix = $"{key}=";
        foreach (var arg in args)
        {
            if (arg.StartsWith(prefix, StringComparison.Ordinal))
            {
                return arg[prefix.Length..];
            }
        }

        return null;
    }

    private static async Task<string?> ResolveInitialPasswordAsync(string[] args, ILogger logger, CancellationToken cancellationToken)
    {
        var inlinePassword = TryReadArg(args, "--initial-password");
        var passwordFile = TryReadArg(args, "--initial-password-file");

        if (!string.IsNullOrWhiteSpace(inlinePassword) && !string.IsNullOrWhiteSpace(passwordFile))
        {
            logger.LogError("Provide either --initial-password or --initial-password-file, not both.");
            return null;
        }

        if (!string.IsNullOrWhiteSpace(inlinePassword))
        {
            return inlinePassword;
        }

        if (!string.IsNullOrWhiteSpace(passwordFile))
        {
            if (!File.Exists(passwordFile))
            {
                logger.LogError("Password file does not exist: {PasswordFile}", passwordFile);
                return null;
            }

            var content = await File.ReadAllTextAsync(passwordFile, cancellationToken);
            return content.Trim();
        }

        logger.LogError("Missing required argument: --initial-password or --initial-password-file.");
        return null;
    }

    private static bool ValidateInitialPassword(
        string password,
        BreachListChecker breachListChecker,
        out string validationError)
    {
        if (password.Length < 12)
        {
            validationError = "The initial password must be at least 12 characters long.";
            return false;
        }

        var classCount = 0;
        if (password.Any(char.IsLower)) classCount++;
        if (password.Any(char.IsUpper)) classCount++;
        if (password.Any(char.IsDigit)) classCount++;
        if (password.Any(ch => !char.IsLetterOrDigit(ch))) classCount++;

        if (classCount < 3)
        {
            validationError = "The initial password must include at least three character classes.";
            return false;
        }

        if (breachListChecker.IsCompromised(password))
        {
            validationError = "The initial password appears in a known breached-password list.";
            return false;
        }

        validationError = string.Empty;
        return true;
    }
}
