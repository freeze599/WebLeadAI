using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WebLeadAI.Data;

#nullable disable

namespace WebLeadAI.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261109150000_AddProcurementFields")]
    public partial class AddProcurementFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(name: "EstimatedBudget", table: "Leads", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "SubmissionDeadline", table: "Leads", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<string>(name: "BuyerName", table: "Leads", type: "TEXT", maxLength: 300, nullable: true);
        }
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "EstimatedBudget", table: "Leads");
            migrationBuilder.DropColumn(name: "SubmissionDeadline", table: "Leads");
            migrationBuilder.DropColumn(name: "BuyerName", table: "Leads");
        }
    }
}
