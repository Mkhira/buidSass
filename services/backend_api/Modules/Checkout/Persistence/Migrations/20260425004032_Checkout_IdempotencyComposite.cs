using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Modules.Checkout.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Checkout_IdempotencyComposite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_checkout_payment_attempts_state_enum",
                schema: "checkout",
                table: "payment_attempts");

            migrationBuilder.DropPrimaryKey(
                name: "PK_idempotency_results",
                schema: "checkout",
                table: "idempotency_results");

            migrationBuilder.AlterColumn<Guid>(
                name: "AccountId",
                schema: "checkout",
                table: "idempotency_results",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_idempotency_results",
                schema: "checkout",
                table: "idempotency_results",
                columns: new[] { "AccountId", "IdempotencyKey" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_checkout_payment_attempts_state_enum",
                schema: "checkout",
                table: "payment_attempts",
                sql: "\"State\" IN ('initiated','authorized','captured','declined','voided','failed','refunded','pending_webhook')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_checkout_payment_attempts_state_enum",
                schema: "checkout",
                table: "payment_attempts");

            migrationBuilder.DropPrimaryKey(
                name: "PK_idempotency_results",
                schema: "checkout",
                table: "idempotency_results");

            migrationBuilder.AlterColumn<Guid>(
                name: "AccountId",
                schema: "checkout",
                table: "idempotency_results",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddPrimaryKey(
                name: "PK_idempotency_results",
                schema: "checkout",
                table: "idempotency_results",
                column: "IdempotencyKey");

            migrationBuilder.AddCheckConstraint(
                name: "CK_checkout_payment_attempts_state_enum",
                schema: "checkout",
                table: "payment_attempts",
                sql: "\"State\" IN ('initiated','authorized','captured','declined','voided','failed','pending_webhook')");
        }
    }
}
