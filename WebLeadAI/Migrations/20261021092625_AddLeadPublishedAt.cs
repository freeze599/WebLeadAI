using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebLeadAI.Migrations
{
    /// <inheritdoc />
    public partial class AddLeadPublishedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PublishedAt",
                table: "Leads",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PublishedAt",
                table: "Leads");
        }
    }
}
