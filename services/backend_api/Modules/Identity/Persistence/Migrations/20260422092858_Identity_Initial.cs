using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Modules.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Identity_Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.CreateTable(
                name: "account_roles",
                schema: "identity",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketCode = table.Column<string>(type: "citext", nullable: false),
                    GrantedByAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    GrantedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_roles", x => new { x.AccountId, x.RoleId, x.MarketCode });
                });

            migrationBuilder.CreateTable(
                name: "accounts",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Surface = table.Column<string>(type: "citext", nullable: false),
                    MarketCode = table.Column<string>(type: "citext", nullable: false),
                    EmailNormalized = table.Column<string>(type: "citext", nullable: false),
                    EmailDisplay = table.Column<string>(type: "text", nullable: false),
                    PhoneE164 = table.Column<string>(type: "text", nullable: true),
                    PhoneMarketCode = table.Column<string>(type: "citext", nullable: true),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    PasswordHashVersion = table.Column<short>(type: "smallint", nullable: false),
                    Status = table.Column<string>(type: "citext", nullable: false),
                    EmailVerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PhoneVerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Locale = table.Column<string>(type: "citext", nullable: false, defaultValue: "ar"),
                    DisplayName = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "admin_invitations",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmailNormalized = table.Column<string>(type: "citext", nullable: false),
                    InvitedByAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvitedRoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "citext", nullable: false),
                    AcceptedAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_invitations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "admin_mfa_factors",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "citext", nullable: false),
                    SecretEncrypted = table.Column<byte[]>(type: "bytea", nullable: false),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RecoveryCodesHash = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_mfa_factors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "admin_mfa_replay_guard",
                schema: "identity",
                columns: table => new
                {
                    FactorId = table.Column<Guid>(type: "uuid", nullable: false),
                    WindowCounter = table.Column<long>(type: "bigint", nullable: false),
                    ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_mfa_replay_guard", x => new { x.FactorId, x.WindowCounter });
                });

            migrationBuilder.CreateTable(
                name: "authorization_audit",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    Surface = table.Column<string>(type: "citext", nullable: false),
                    PermissionCode = table.Column<string>(type: "citext", nullable: false),
                    Decision = table.Column<string>(type: "citext", nullable: false),
                    ReasonCode = table.Column<string>(type: "citext", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_authorization_audit", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "email_verification_challenges",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "citext", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_verification_challenges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "lockout_state",
                schema: "identity",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "citext", nullable: false),
                    FailedCount = table.Column<int>(type: "integer", nullable: false),
                    FirstFailedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lockout_state", x => new { x.AccountId, x.Reason });
                });

            migrationBuilder.CreateTable(
                name: "otp_challenges",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    Purpose = table.Column<string>(type: "citext", nullable: false),
                    Surface = table.Column<string>(type: "citext", nullable: false),
                    Channel = table.Column<string>(type: "citext", nullable: false),
                    DestinationHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    CodeHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    CodeLength = table.Column<short>(type: "smallint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MaxAttempts = table.Column<short>(type: "smallint", nullable: false),
                    Attempts = table.Column<short>(type: "smallint", nullable: false),
                    Status = table.Column<string>(type: "citext", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_otp_challenges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "password_reset_tokens",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "citext", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_password_reset_tokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "permissions",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "citext", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_permissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "rate_limit_events",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyCode = table.Column<string>(type: "citext", nullable: false),
                    ScopeKeyHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    BlockedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Surface = table.Column<string>(type: "citext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rate_limit_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    TokenSalt = table.Column<byte[]>(type: "bytea", nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "citext", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SupersededBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_tokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "revoked_refresh_tokens",
                schema: "identity",
                columns: table => new
                {
                    TokenHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "citext", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_revoked_refresh_tokens", x => x.TokenHash);
                });

            migrationBuilder.CreateTable(
                name: "role_permissions",
                schema: "identity",
                columns: table => new
                {
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    PermissionId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_permissions", x => new { x.RoleId, x.PermissionId });
                });

            migrationBuilder.CreateTable(
                name: "roles",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "citext", nullable: false),
                    NameAr = table.Column<string>(type: "text", nullable: false),
                    NameEn = table.Column<string>(type: "text", nullable: false),
                    Scope = table.Column<string>(type: "citext", nullable: false),
                    System = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sessions",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Surface = table.Column<string>(type: "citext", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClientAgent = table.Column<string>(type: "text", nullable: true),
                    ClientIpHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    Status = table.Column<string>(type: "citext", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedReason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounts_MarketCode_CreatedAt",
                schema: "identity",
                table: "accounts",
                columns: new[] { "MarketCode", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_accounts_Surface_EmailNormalized",
                schema: "identity",
                table: "accounts",
                columns: new[] { "Surface", "EmailNormalized" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_accounts_Surface_PhoneE164",
                schema: "identity",
                table: "accounts",
                columns: new[] { "Surface", "PhoneE164" },
                unique: true,
                filter: "\"PhoneE164\" IS NOT NULL AND \"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_admin_mfa_factors_AccountId_Kind",
                schema: "identity",
                table: "admin_mfa_factors",
                columns: new[] { "AccountId", "Kind" },
                unique: true,
                filter: "\"RevokedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_authorization_audit_Surface_PermissionCode_Decision_Occurre~",
                schema: "identity",
                table: "authorization_audit",
                columns: new[] { "Surface", "PermissionCode", "Decision", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_otp_challenges_AccountId_Purpose_CreatedAt",
                schema: "identity",
                table: "otp_challenges",
                columns: new[] { "AccountId", "Purpose", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_permissions_Code",
                schema: "identity",
                table: "permissions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rate_limit_events_PolicyCode_BlockedAt",
                schema: "identity",
                table: "rate_limit_events",
                columns: new[] { "PolicyCode", "BlockedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_SessionId",
                schema: "identity",
                table: "refresh_tokens",
                column: "SessionId",
                unique: true,
                filter: "\"Status\" = 'active'");

            migrationBuilder.CreateIndex(
                name: "IX_roles_Code",
                schema: "identity",
                table: "roles",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sessions_AccountId_Status_LastSeenAt",
                schema: "identity",
                table: "sessions",
                columns: new[] { "AccountId", "Status", "LastSeenAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_roles",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "accounts",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "admin_invitations",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "admin_mfa_factors",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "admin_mfa_replay_guard",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "authorization_audit",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "email_verification_challenges",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "lockout_state",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "otp_challenges",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "password_reset_tokens",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "permissions",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "rate_limit_events",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "refresh_tokens",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "revoked_refresh_tokens",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "role_permissions",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "sessions",
                schema: "identity");
        }
    }
}
