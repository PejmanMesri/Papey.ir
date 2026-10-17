using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Papey.Migrations
{
    /// <inheritdoc />
    public partial class AdminOrderSmsRecipients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrderAdminSmsJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<int>(type: "integer", nullable: false),
                    Phone = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false),
                    IsSandbox = table.Column<bool>(type: "boolean", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AvailableAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MessageId = table.Column<long>(type: "bigint", nullable: true),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderAdminSmsJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderAdminSmsJobs_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderAdminSmsJobs_OrderId_Phone",
                table: "OrderAdminSmsJobs",
                columns: new[] { "OrderId", "Phone" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderAdminSmsJobs_State_AvailableAt",
                table: "OrderAdminSmsJobs",
                columns: new[] { "State", "AvailableAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderAdminSmsJobs");
        }
    }
}
