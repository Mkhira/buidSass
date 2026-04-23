using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Modules.Pricing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Pricing_Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "pricing");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.CreateTable(
                name: "account_b2b_tiers",
                schema: "pricing",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    TierId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AssignedByAccountId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_b2b_tiers", x => x.AccountId);
                });

            migrationBuilder.CreateTable(
                name: "b2b_tiers",
                schema: "pricing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "citext", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    DefaultDiscountBps = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_b2b_tiers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "bundle_memberships",
                schema: "pricing",
                columns: table => new
                {
                    BundleProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComponentProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Qty = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bundle_memberships", x => new { x.BundleProductId, x.ComponentProductId });
                });

            migrationBuilder.CreateTable(
                name: "coupon_redemptions",
                schema: "pricing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CouponId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    RedeemedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coupon_redemptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "coupons",
                schema: "pricing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "citext", nullable: false),
                    Kind = table.Column<string>(type: "citext", nullable: false),
                    Value = table.Column<int>(type: "integer", nullable: false),
                    CapMinor = table.Column<long>(type: "bigint", nullable: true),
                    PerCustomerLimit = table.Column<int>(type: "integer", nullable: true),
                    OverallLimit = table.Column<int>(type: "integer", nullable: true),
                    UsedCount = table.Column<int>(type: "integer", nullable: false),
                    ExcludesRestricted = table.Column<bool>(type: "boolean", nullable: false),
                    MarketCodes = table.Column<string[]>(type: "citext[]", nullable: false),
                    ValidFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ValidTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OwnerId = table.Column<string>(type: "citext", nullable: true),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coupons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "price_explanations",
                schema: "pricing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerKind = table.Column<string>(type: "citext", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    MarketCode = table.Column<string>(type: "citext", nullable: false),
                    ExplanationJson = table.Column<string>(type: "jsonb", nullable: false),
                    ExplanationHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    GrandTotalMinor = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_price_explanations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "product_tier_prices",
                schema: "pricing",
                columns: table => new
                {
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    TierId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketCode = table.Column<string>(type: "citext", nullable: false),
                    NetMinor = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_tier_prices", x => new { x.ProductId, x.TierId, x.MarketCode });
                });

            migrationBuilder.CreateTable(
                name: "promotions",
                schema: "pricing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "citext", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ConfigJson = table.Column<string>(type: "jsonb", nullable: false),
                    AppliesToProductIds = table.Column<Guid[]>(type: "uuid[]", nullable: true),
                    AppliesToCategoryIds = table.Column<Guid[]>(type: "uuid[]", nullable: true),
                    MarketCodes = table.Column<string[]>(type: "citext[]", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OwnerId = table.Column<string>(type: "citext", nullable: true),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tax_rates",
                schema: "pricing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketCode = table.Column<string>(type: "citext", nullable: false),
                    Kind = table.Column<string>(type: "citext", nullable: false),
                    RateBps = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EffectiveTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_rates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_b2b_tiers_Slug",
                schema: "pricing",
                table: "b2b_tiers",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_coupon_redemptions_CouponId_AccountId",
                schema: "pricing",
                table: "coupon_redemptions",
                columns: new[] { "CouponId", "AccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_coupon_redemptions_CouponId_AccountId_OrderId",
                schema: "pricing",
                table: "coupon_redemptions",
                columns: new[] { "CouponId", "AccountId", "OrderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_coupons_Code",
                schema: "pricing",
                table: "coupons",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_price_explanations_OwnerKind_OwnerId",
                schema: "pricing",
                table: "price_explanations",
                columns: new[] { "OwnerKind", "OwnerId" },
                unique: true,
                filter: "\"OwnerKind\" IN ('quote','order')");

            migrationBuilder.CreateIndex(
                name: "IX_promotions_IsActive_DeletedAt",
                schema: "pricing",
                table: "promotions",
                columns: new[] { "IsActive", "DeletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_promotions_Priority",
                schema: "pricing",
                table: "promotions",
                column: "Priority");

            migrationBuilder.CreateIndex(
                name: "IX_tax_rates_MarketCode_Kind_EffectiveFrom",
                schema: "pricing",
                table: "tax_rates",
                columns: new[] { "MarketCode", "Kind", "EffectiveFrom" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_b2b_tiers",
                schema: "pricing");

            migrationBuilder.DropTable(
                name: "b2b_tiers",
                schema: "pricing");

            migrationBuilder.DropTable(
                name: "bundle_memberships",
                schema: "pricing");

            migrationBuilder.DropTable(
                name: "coupon_redemptions",
                schema: "pricing");

            migrationBuilder.DropTable(
                name: "coupons",
                schema: "pricing");

            migrationBuilder.DropTable(
                name: "price_explanations",
                schema: "pricing");

            migrationBuilder.DropTable(
                name: "product_tier_prices",
                schema: "pricing");

            migrationBuilder.DropTable(
                name: "promotions",
                schema: "pricing");

            migrationBuilder.DropTable(
                name: "tax_rates",
                schema: "pricing");
        }
    }
}
