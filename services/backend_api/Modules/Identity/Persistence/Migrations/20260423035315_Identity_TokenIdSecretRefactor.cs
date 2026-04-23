using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Modules.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Identity_TokenIdSecretRefactor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TokenSalt",
                schema: "identity",
                table: "refresh_tokens");

            migrationBuilder.AlterColumn<byte[]>(
                name: "TokenHash",
                schema: "identity",
                table: "refresh_tokens",
                type: "bytea",
                nullable: true,
                oldClrType: typeof(byte[]),
                oldType: "bytea");

            migrationBuilder.AddColumn<Guid>(
                name: "TokenId",
                schema: "identity",
                table: "refresh_tokens",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "TokenSecretHash",
                schema: "identity",
                table: "refresh_tokens",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TokenId",
                schema: "identity",
                table: "password_reset_tokens",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "TokenSecretHash",
                schema: "identity",
                table: "password_reset_tokens",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TokenId",
                schema: "identity",
                table: "email_verification_challenges",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "TokenSecretHash",
                schema: "identity",
                table: "email_verification_challenges",
                type: "bytea",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_TokenId",
                schema: "identity",
                table: "refresh_tokens",
                column: "TokenId",
                unique: true,
                filter: "\"TokenId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_password_reset_tokens_TokenId",
                schema: "identity",
                table: "password_reset_tokens",
                column: "TokenId",
                unique: true,
                filter: "\"TokenId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_email_verification_challenges_TokenId",
                schema: "identity",
                table: "email_verification_challenges",
                column: "TokenId",
                unique: true,
                filter: "\"TokenId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_refresh_tokens_TokenId",
                schema: "identity",
                table: "refresh_tokens");

            migrationBuilder.DropIndex(
                name: "IX_password_reset_tokens_TokenId",
                schema: "identity",
                table: "password_reset_tokens");

            migrationBuilder.DropIndex(
                name: "IX_email_verification_challenges_TokenId",
                schema: "identity",
                table: "email_verification_challenges");

            migrationBuilder.DropColumn(
                name: "TokenId",
                schema: "identity",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "TokenSecretHash",
                schema: "identity",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "TokenId",
                schema: "identity",
                table: "password_reset_tokens");

            migrationBuilder.DropColumn(
                name: "TokenSecretHash",
                schema: "identity",
                table: "password_reset_tokens");

            migrationBuilder.DropColumn(
                name: "TokenId",
                schema: "identity",
                table: "email_verification_challenges");

            migrationBuilder.DropColumn(
                name: "TokenSecretHash",
                schema: "identity",
                table: "email_verification_challenges");

            migrationBuilder.AlterColumn<byte[]>(
                name: "TokenHash",
                schema: "identity",
                table: "refresh_tokens",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0],
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldNullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "TokenSalt",
                schema: "identity",
                table: "refresh_tokens",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0]);
        }
    }
}
