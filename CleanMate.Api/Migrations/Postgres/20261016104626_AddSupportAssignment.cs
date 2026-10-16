using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanMate.Api.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddSupportAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AssignedAt",
                table: "SupportTickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AssignedToId",
                table: "SupportTickets",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_AssignedToId_Status_Id",
                table: "SupportTickets",
                columns: new[] { "AssignedToId", "Status", "Id" });

            migrationBuilder.AddForeignKey(
                name: "FK_SupportTickets_Users_AssignedToId",
                table: "SupportTickets",
                column: "AssignedToId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SupportTickets_Users_AssignedToId",
                table: "SupportTickets");

            migrationBuilder.DropIndex(
                name: "IX_SupportTickets_AssignedToId_Status_Id",
                table: "SupportTickets");

            migrationBuilder.DropColumn(
                name: "AssignedAt",
                table: "SupportTickets");

            migrationBuilder.DropColumn(
                name: "AssignedToId",
                table: "SupportTickets");
        }
    }
}
