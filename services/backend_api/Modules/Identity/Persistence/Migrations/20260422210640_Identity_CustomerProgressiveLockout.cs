using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Modules.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Identity_CustomerProgressiveLockout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CooldownIndex",
                schema: "identity",
                table: "lockout_state",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresAdminUnlock",
                schema: "identity",
                table: "lockout_state",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Tier",
                schema: "identity",
                table: "lockout_state",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CooldownIndex",
                schema: "identity",
                table: "lockout_state");

            migrationBuilder.DropColumn(
                name: "RequiresAdminUnlock",
                schema: "identity",
                table: "lockout_state");

            migrationBuilder.DropColumn(
                name: "Tier",
                schema: "identity",
                table: "lockout_state");
        }
    }
}
