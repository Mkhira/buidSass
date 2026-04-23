using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Modules.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Identity_AccountVerificationAndCompanyFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CompanyAccountId",
                schema: "identity",
                table: "accounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfessionalVerificationStatus",
                schema: "identity",
                table: "accounts",
                type: "citext",
                nullable: false,
                defaultValue: "unverified");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProfessionalVerificationStatusUpdatedAt",
                schema: "identity",
                table: "accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounts_CompanyAccountId",
                schema: "identity",
                table: "accounts",
                column: "CompanyAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_accounts_CompanyAccountId",
                schema: "identity",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "CompanyAccountId",
                schema: "identity",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "ProfessionalVerificationStatus",
                schema: "identity",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "ProfessionalVerificationStatusUpdatedAt",
                schema: "identity",
                table: "accounts");
        }
    }
}
