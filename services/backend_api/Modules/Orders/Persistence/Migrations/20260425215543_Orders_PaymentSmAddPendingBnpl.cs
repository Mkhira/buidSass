using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Modules.Orders.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Orders_PaymentSmAddPendingBnpl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_orders_orders_payment_state_enum",
                schema: "orders",
                table: "orders");

            migrationBuilder.AddCheckConstraint(
                name: "CK_orders_orders_payment_state_enum",
                schema: "orders",
                table: "orders",
                sql: "\"PaymentState\" IN ('authorized','captured','pending_cod','pending_bank_transfer','pending_bnpl','failed','voided','refunded','partially_refunded')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_orders_orders_payment_state_enum",
                schema: "orders",
                table: "orders");

            migrationBuilder.AddCheckConstraint(
                name: "CK_orders_orders_payment_state_enum",
                schema: "orders",
                table: "orders",
                sql: "\"PaymentState\" IN ('authorized','captured','pending_cod','pending_bank_transfer','failed','voided','refunded','partially_refunded')");
        }
    }
}
