using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WebLeadAI.Data;

#nullable disable

namespace WebLeadAI.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261126170000_AddLeadEmailOutbox")]
    public partial class AddLeadEmailOutbox : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LeadEmailOutbox",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Score = table.Column<int>(type: "INTEGER", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    NextAttemptUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    LastError = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_LeadEmailOutbox", x => x.Id));
            migrationBuilder.CreateIndex(
                name: "IX_LeadEmailOutbox_SourceUrl", table: "LeadEmailOutbox",
                column: "SourceUrl", unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_LeadEmailOutbox_SentAtUtc_NextAttemptUtc", table: "LeadEmailOutbox",
                columns: new[] { "SentAtUtc", "NextAttemptUtc" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "LeadEmailOutbox");
        }
    }
}
