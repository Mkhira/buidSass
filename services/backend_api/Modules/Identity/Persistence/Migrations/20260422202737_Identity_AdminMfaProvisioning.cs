using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Modules.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Identity_AdminMfaProvisioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProvisioningTokenExpiresAt",
                schema: "identity",
                table: "admin_mfa_factors",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "ProvisioningTokenHash",
                schema: "identity",
                table: "admin_mfa_factors",
                type: "bytea",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProvisioningTokenExpiresAt",
                schema: "identity",
                table: "admin_mfa_factors");

            migrationBuilder.DropColumn(
                name: "ProvisioningTokenHash",
                schema: "identity",
                table: "admin_mfa_factors");
        }
    }
}
