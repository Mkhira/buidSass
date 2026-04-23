using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Modules.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Identity_AccountPermissionVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PermissionVersion",
                schema: "identity",
                table: "accounts",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PermissionVersion",
                schema: "identity",
                table: "accounts");
        }
    }
}
