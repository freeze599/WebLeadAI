using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebLeadAI.Migrations
{
    /// <inheritdoc />
    public partial class AddLeadProposalDraft : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProposalDraft",
                table: "Leads",
                type: "TEXT",
                maxLength: 20000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProposalLanguage",
                table: "Leads",
                type: "TEXT",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProposalSavedAt",
                table: "Leads",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProposalDraft",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "ProposalLanguage",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "ProposalSavedAt",
                table: "Leads");
        }
    }
}
