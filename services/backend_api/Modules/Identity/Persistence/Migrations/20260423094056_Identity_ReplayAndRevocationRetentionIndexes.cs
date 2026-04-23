using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Modules.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Identity_ReplayAndRevocationRetentionIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_revoked_refresh_tokens_RevokedAt",
                schema: "identity",
                table: "revoked_refresh_tokens",
                column: "RevokedAt");

            migrationBuilder.CreateIndex(
                name: "IX_admin_mfa_replay_guard_ObservedAt",
                schema: "identity",
                table: "admin_mfa_replay_guard",
                column: "ObservedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_revoked_refresh_tokens_RevokedAt",
                schema: "identity",
                table: "revoked_refresh_tokens");

            migrationBuilder.DropIndex(
                name: "IX_admin_mfa_replay_guard_ObservedAt",
                schema: "identity",
                table: "admin_mfa_replay_guard");
        }
    }
}
