using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Papey.Migrations
{
    /// <inheritdoc />
    public partial class ProductReviewsAndDesignGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DesignCode",
                table: "Products",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProductReviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    OrderId = table.Column<int>(type: "integer", nullable: false),
                    PrintQuality = table.Column<int>(type: "integer", nullable: false),
                    FabricQuality = table.Column<int>(type: "integer", nullable: false),
                    Fit = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsHidden = table.Column<bool>(type: "boolean", nullable: false),
                    AdminReply = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    AdminRepliedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductReviews", x => x.Id);
                    table.CheckConstraint("CK_ProductReviews_Scores", "\"PrintQuality\" BETWEEN 1 AND 5 AND \"FabricQuality\" BETWEEN 1 AND 5 AND \"Fit\" BETWEEN 1 AND 3");
                    table.ForeignKey(
                        name: "FK_ProductReviews_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductReviews_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductReviews_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Products_DesignCode",
                table: "Products",
                column: "DesignCode");

            migrationBuilder.CreateIndex(
                name: "IX_ProductReviews_OrderId",
                table: "ProductReviews",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductReviews_ProductId_IsHidden_CreatedAt",
                table: "ProductReviews",
                columns: new[] { "ProductId", "IsHidden", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductReviews_ProductId_UserId",
                table: "ProductReviews",
                columns: new[] { "ProductId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductReviews_UserId",
                table: "ProductReviews",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductReviews");

            migrationBuilder.DropIndex(
                name: "IX_Products_DesignCode",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "DesignCode",
                table: "Products");
        }
    }
}
