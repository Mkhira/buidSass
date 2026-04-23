using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Modules.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Identity_SessionClientFingerprint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "ClientFingerprintHash",
                schema: "identity",
                table: "sessions",
                type: "bytea",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClientFingerprintHash",
                schema: "identity",
                table: "sessions");
        }
    }
}
