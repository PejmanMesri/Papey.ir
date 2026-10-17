using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Papey.Migrations
{
    /// <inheritdoc />
    public partial class HomeHeroSlotsAndRemoveDemoProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Identify the original sample catalog by both slug and bundled image.
            // Keep anything referenced by an order, but hide it from the storefront.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE papey_demo_cleanup ON COMMIT DROP AS
                SELECT "Id" FROM "Products" WHERE ("Slug", "ImageUrl") IN (
                    ('tshirt-anime-shadow','/img/products/damavand.svg'),
                    ('tshirt-history-iran','/img/products/persepolis.svg'),
                    ('tshirt-gaming-neon','/img/products/persian-legacy.svg'),
                    ('tshirt-modern-art','/img/products/versk.svg'),
                    ('tshirt-street-music','/img/products/alborz.svg'),
                    ('tshirt-classic-car','/img/products/car-classic.svg'),
                    ('tshirt-minimal','/img/products/pinterest-minimal.svg'),
                    ('hoodie-anime-hero','/img/products/pinterest-vintage.svg'),
                    ('hoodie-history-story','/img/products/caspians.svg'),
                    ('hoodie-gaming-neon','/img/products/sport-football.svg'),
                    ('hoodie-art-brush','/img/products/sport-gym.svg'),
                    ('hoodie-music-rhythm','/img/products/set-couple.svg'),
                    ('hoodie-sport-car','/img/products/car-sport.svg'),
                    ('hoodie-papey-basic','/img/products/set-family.svg')
                );
                UPDATE "Products" SET "IsActive" = FALSE
                WHERE "Id" IN (SELECT "Id" FROM papey_demo_cleanup);
                DELETE FROM "Products" p USING papey_demo_cleanup d
                WHERE p."Id" = d."Id" AND NOT EXISTS (
                    SELECT 1 FROM "OrderItems" i WHERE i."ProductId" = p."Id"
                    OR i."ProductVariantId" IN (SELECT "Id" FROM "ProductVariants" WHERE "ProductId" = p."Id")
                );
                """);
            migrationBuilder.AddColumn<int>(
                name: "HomeHeroSlot",
                table: "Products",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_HomeHeroSlot",
                table: "Products",
                column: "HomeHeroSlot",
                unique: true,
                filter: "\"HomeHeroSlot\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Products_HomeHeroSlot",
                table: "Products",
                sql: "\"HomeHeroSlot\" IS NULL OR \"HomeHeroSlot\" BETWEEN 1 AND 2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_HomeHeroSlot",
                table: "Products");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_HomeHeroSlot",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "HomeHeroSlot",
                table: "Products");
        }
    }
}
