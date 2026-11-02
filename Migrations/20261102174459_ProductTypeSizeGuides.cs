using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Papey.Migrations
{
    /// <inheritdoc />
    public partial class ProductTypeSizeGuides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SizeGuideImageUrl",
                table: "ProductTypes",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
            migrationBuilder.Sql("""
                UPDATE "ProductTypes" SET "SizeGuideImageUrl" = CASE
                    WHEN replace("Name", 'ي', 'ی') IN ('هودی', 'hoodie', 'Hoodie') THEN '/img/size-guide-hoodie-default.jpg'
                    WHEN replace("Name", 'ي', 'ی') IN ('لانگ اسلیو', 'لانگ‌اسلیو', 'long sleeve', 'long-sleeve') THEN '/img/size-guide-long-sleeve-default.png'
                    ELSE '/img/size-guide-default.jpg' END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SizeGuideImageUrl",
                table: "ProductTypes");
        }
    }
}
