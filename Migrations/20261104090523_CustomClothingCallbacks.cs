using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Papey.Migrations
{
    /// <inheritdoc />
    public partial class CustomClothingCallbacks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "RequestId",
                table: "CustomDesignSmsJobs",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "CallbackRequestId",
                table: "CustomDesignSmsJobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CustomCallbackRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Phone = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AdminNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomCallbackRequests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomDesignSmsJobs_CallbackRequestId_Phone",
                table: "CustomDesignSmsJobs",
                columns: new[] { "CallbackRequestId", "Phone" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_CustomDesignSmsJobs_Request",
                table: "CustomDesignSmsJobs",
                sql: "(\"RequestId\" IS NOT NULL) <> (\"CallbackRequestId\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_CustomCallbackRequests_Phone_CreatedAt",
                table: "CustomCallbackRequests",
                columns: new[] { "Phone", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomCallbackRequests_Status_CreatedAt",
                table: "CustomCallbackRequests",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_CustomDesignSmsJobs_CustomCallbackRequests_CallbackRequestId",
                table: "CustomDesignSmsJobs",
                column: "CallbackRequestId",
                principalTable: "CustomCallbackRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CustomDesignSmsJobs_CustomCallbackRequests_CallbackRequestId",
                table: "CustomDesignSmsJobs");

            migrationBuilder.DropTable(
                name: "CustomCallbackRequests");

            migrationBuilder.DropIndex(
                name: "IX_CustomDesignSmsJobs_CallbackRequestId_Phone",
                table: "CustomDesignSmsJobs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CustomDesignSmsJobs_Request",
                table: "CustomDesignSmsJobs");

            migrationBuilder.DropColumn(
                name: "CallbackRequestId",
                table: "CustomDesignSmsJobs");

            migrationBuilder.AlterColumn<Guid>(
                name: "RequestId",
                table: "CustomDesignSmsJobs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
