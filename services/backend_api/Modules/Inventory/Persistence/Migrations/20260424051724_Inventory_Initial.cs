using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BackendApi.Modules.Inventory.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Inventory_Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "inventory");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.CreateTable(
                name: "inventory_batches",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketCode = table.Column<string>(type: "citext", nullable: false),
                    LotNo = table.Column<string>(type: "text", nullable: false),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    QtyOnHand = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "citext", nullable: false, defaultValue: "active"),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReceivedByAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_batches", x => x.Id);
                    table.CheckConstraint("CK_inventory_batches_qty_on_hand_non_negative", "\"QtyOnHand\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "inventory_movements",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketCode = table.Column<string>(type: "citext", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "citext", nullable: false),
                    Delta = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    SourceKind = table.Column<string>(type: "citext", nullable: true),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_movements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "inventory_reservations",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketCode = table.Column<string>(type: "citext", nullable: false),
                    Qty = table.Column<int>(type: "integer", nullable: false),
                    CartId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    PickedBatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "citext", nullable: false, defaultValue: "active"),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConvertedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_reservations", x => x.Id);
                    table.CheckConstraint("CK_inventory_reservations_qty_positive", "\"Qty\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "reorder_alert_debounce",
                schema: "inventory",
                columns: table => new
                {
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    WindowStartHour = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reorder_alert_debounce", x => new { x.WarehouseId, x.ProductId, x.WindowStartHour });
                });

            migrationBuilder.CreateTable(
                name: "stock_levels",
                schema: "inventory",
                columns: table => new
                {
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    OnHand = table.Column<int>(type: "integer", nullable: false),
                    Reserved = table.Column<int>(type: "integer", nullable: false),
                    SafetyStock = table.Column<int>(type: "integer", nullable: false),
                    ReorderThreshold = table.Column<int>(type: "integer", nullable: false),
                    BucketCache = table.Column<string>(type: "citext", nullable: false, defaultValue: "out_of_stock"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_levels", x => new { x.ProductId, x.WarehouseId });
                    table.CheckConstraint("CK_stock_levels_on_hand_non_negative", "\"OnHand\" >= 0");
                    table.CheckConstraint("CK_stock_levels_reorder_threshold_non_negative", "\"ReorderThreshold\" >= 0");
                    table.CheckConstraint("CK_stock_levels_reserved_non_negative", "\"Reserved\" >= 0");
                    table.CheckConstraint("CK_stock_levels_safety_stock_non_negative", "\"SafetyStock\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "warehouses",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "citext", nullable: false),
                    MarketCode = table.Column<string>(type: "citext", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    OwnerId = table.Column<string>(type: "citext", nullable: false, defaultValue: "platform"),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_warehouses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_batches_MarketCode",
                schema: "inventory",
                table: "inventory_batches",
                column: "MarketCode");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_batches_ProductId_WarehouseId_ExpiryDate",
                schema: "inventory",
                table: "inventory_batches",
                columns: new[] { "ProductId", "WarehouseId", "ExpiryDate" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_batches_ProductId_WarehouseId_LotNo",
                schema: "inventory",
                table: "inventory_batches",
                columns: new[] { "ProductId", "WarehouseId", "LotNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_movements_MarketCode",
                schema: "inventory",
                table: "inventory_movements",
                column: "MarketCode");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_movements_ProductId_WarehouseId_OccurredAt",
                schema: "inventory",
                table: "inventory_movements",
                columns: new[] { "ProductId", "WarehouseId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_reservations_MarketCode",
                schema: "inventory",
                table: "inventory_reservations",
                column: "MarketCode");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_reservations_Status_ExpiresAt",
                schema: "inventory",
                table: "inventory_reservations",
                columns: new[] { "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_reorder_alert_debounce_EmittedAt",
                schema: "inventory",
                table: "reorder_alert_debounce",
                column: "EmittedAt");

            migrationBuilder.CreateIndex(
                name: "IX_stock_levels_BucketCache",
                schema: "inventory",
                table: "stock_levels",
                column: "BucketCache");

            migrationBuilder.CreateIndex(
                name: "IX_warehouses_Code",
                schema: "inventory",
                table: "warehouses",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_warehouses_MarketCode_IsActive",
                schema: "inventory",
                table: "warehouses",
                columns: new[] { "MarketCode", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inventory_batches",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "inventory_movements",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "inventory_reservations",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "reorder_alert_debounce",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_levels",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "warehouses",
                schema: "inventory");
        }
    }
}
