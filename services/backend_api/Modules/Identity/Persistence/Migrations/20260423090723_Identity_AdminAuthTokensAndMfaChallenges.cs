using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Modules.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Identity_AdminAuthTokensAndMfaChallenges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "admin_mfa_challenges",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    FactorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExhaustedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    MaxAttempts = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)3)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_mfa_challenges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "admin_partial_auth_tokens",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenSecretHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_partial_auth_tokens", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_admin_mfa_challenges_AccountId_ExpiresAt",
                schema: "identity",
                table: "admin_mfa_challenges",
                columns: new[] { "AccountId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_admin_mfa_challenges_ConsumedAt",
                schema: "identity",
                table: "admin_mfa_challenges",
                column: "ConsumedAt");

            migrationBuilder.CreateIndex(
                name: "IX_admin_mfa_challenges_ExhaustedAt",
                schema: "identity",
                table: "admin_mfa_challenges",
                column: "ExhaustedAt");

            migrationBuilder.CreateIndex(
                name: "IX_admin_mfa_challenges_FactorId",
                schema: "identity",
                table: "admin_mfa_challenges",
                column: "FactorId");

            migrationBuilder.CreateIndex(
                name: "IX_admin_partial_auth_tokens_AccountId_ExpiresAt",
                schema: "identity",
                table: "admin_partial_auth_tokens",
                columns: new[] { "AccountId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_admin_partial_auth_tokens_ConsumedAt",
                schema: "identity",
                table: "admin_partial_auth_tokens",
                column: "ConsumedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admin_mfa_challenges",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "admin_partial_auth_tokens",
                schema: "identity");
        }
    }
}
