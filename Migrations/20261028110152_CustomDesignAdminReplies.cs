using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Papey.Migrations
{
    /// <inheritdoc />
    public partial class CustomDesignAdminReplies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AdminRepliedAt",
                table: "CustomDesignRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdminReply",
                table: "CustomDesignRequests",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdminRepliedAt",
                table: "CustomDesignRequests");

            migrationBuilder.DropColumn(
                name: "AdminReply",
                table: "CustomDesignRequests");
        }
    }
}
