using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Modules.Pricing.Persistence.Migrations
{
    /// <summary>
    /// Spec 007-b US3 — reshapes <c>pricing.product_tier_prices</c> from the
    /// legacy composite-key shape <c>(product_id, tier_id, market_code)</c> to
    /// a surrogate <c>Id</c> primary key, allowing the table to host both
    /// tier-only rows and company-override rows (where <c>tier_id IS NULL</c>).
    ///
    /// The 007-a engine query is preserved by a unique partial index on
    /// <c>(TierId, MarketCode, ProductId)</c> filtered to ACTIVE tier-only rows.
    /// </summary>
    /// <inheritdoc />
    public partial class Pricing_007b_US3_BusinessPricingReshape : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Drop the composite primary key so we can add the surrogate one.
            migrationBuilder.DropPrimaryKey(
                name: "PK_product_tier_prices",
                schema: "pricing",
                table: "product_tier_prices");

            // 2. TierId becomes nullable so company-only override rows are legal.
            migrationBuilder.AlterColumn<Guid>(
                name: "TierId",
                schema: "pricing",
                table: "product_tier_prices",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            // 3. Add the surrogate Id column with a per-row default of
            //    gen_random_uuid() so existing rows get unique IDs (rather
            //    than colliding on the EF-generated all-zero default).
            //    AddColumn defaultValueSql ensures every existing row gets a
            //    server-generated UUID at backfill time; new inserts continue
            //    to receive an app-generated UUID via the entity initializer.
            migrationBuilder.AddColumn<Guid>(
                name: "Id",
                schema: "pricing",
                table: "product_tier_prices",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            // 4. Re-add the primary key on the surrogate column.
            migrationBuilder.AddPrimaryKey(
                name: "PK_product_tier_prices",
                schema: "pricing",
                table: "product_tier_prices",
                column: "Id");

            // 5. Unique partial indexes preserving:
            //    (a) the 007-a engine's (tier_id, market, product_id) lookup shape
            //    (b) the US3 company-override uniqueness constraint
            //    Both filtered to ACTIVE rows so deactivated rows don't block
            //    re-creation of a row for the same key tuple.
            migrationBuilder.CreateIndex(
                name: "UX_product_tier_prices_company_market_product",
                schema: "pricing",
                table: "product_tier_prices",
                columns: new[] { "CompanyId", "MarketCode", "ProductId" },
                unique: true,
                filter: "\"CompanyId\" IS NOT NULL AND \"State\" = 0");

            migrationBuilder.CreateIndex(
                name: "UX_product_tier_prices_tier_market_product",
                schema: "pricing",
                table: "product_tier_prices",
                columns: new[] { "TierId", "MarketCode", "ProductId" },
                unique: true,
                filter: "\"TierId\" IS NOT NULL AND \"CompanyId\" IS NULL AND \"State\" = 0");

            // 6. XOR check constraint — three valid row shapes (see entity docstring).
            migrationBuilder.AddCheckConstraint(
                name: "chk_tier_xor_company",
                schema: "pricing",
                table: "product_tier_prices",
                sql: "(\"TierId\" IS NOT NULL AND \"CompanyId\" IS NULL) " +
                     "OR (\"TierId\" IS NULL AND \"CompanyId\" IS NOT NULL) " +
                     "OR (\"TierId\" IS NOT NULL AND \"CompanyId\" IS NOT NULL AND \"CopiedFromTierId\" IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_tier_xor_company",
                schema: "pricing",
                table: "product_tier_prices");

            migrationBuilder.DropIndex(
                name: "UX_product_tier_prices_tier_market_product",
                schema: "pricing",
                table: "product_tier_prices");

            migrationBuilder.DropIndex(
                name: "UX_product_tier_prices_company_market_product",
                schema: "pricing",
                table: "product_tier_prices");

            migrationBuilder.DropPrimaryKey(
                name: "PK_product_tier_prices",
                schema: "pricing",
                table: "product_tier_prices");

            migrationBuilder.DropColumn(
                name: "Id",
                schema: "pricing",
                table: "product_tier_prices");

            // Roll back to the composite key shape. Any company-override rows
            // (TierId IS NULL) would block this and need to be removed first;
            // the down-path is intended for clean rollback only.
            migrationBuilder.AlterColumn<Guid>(
                name: "TierId",
                schema: "pricing",
                table: "product_tier_prices",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_product_tier_prices",
                schema: "pricing",
                table: "product_tier_prices",
                columns: new[] { "ProductId", "TierId", "MarketCode" });
        }
    }
}
