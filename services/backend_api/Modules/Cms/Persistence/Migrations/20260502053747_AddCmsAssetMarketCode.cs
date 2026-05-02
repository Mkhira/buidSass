using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Modules.Cms.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCmsAssetMarketCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MarketCode",
                schema: "cms",
                table: "assets",
                type: "text",
                nullable: false,
                defaultValue: "*");

            migrationBuilder.AddCheckConstraint(
                name: "CK_cms_asset_market_code",
                schema: "cms",
                table: "assets",
                sql: "\"MarketCode\" IN ('EG','KSA','*')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_cms_asset_market_code",
                schema: "cms",
                table: "assets");

            migrationBuilder.DropColumn(
                name: "MarketCode",
                schema: "cms",
                table: "assets");
        }
    }
}
