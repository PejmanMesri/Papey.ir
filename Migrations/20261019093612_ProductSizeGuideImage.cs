using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Papey.Migrations
{
    /// <inheritdoc />
    public partial class ProductSizeGuideImage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SizeGuideImageUrl",
                table: "Products",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SizeGuideImageUrl",
                table: "Products");
        }
    }
}
