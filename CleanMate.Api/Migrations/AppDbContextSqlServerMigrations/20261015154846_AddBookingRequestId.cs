using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanMate.Api.Migrations.AppDbContextSqlServerMigrations
{
    /// <inheritdoc />
    public partial class AddBookingRequestId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "Bookings",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestId",
                table: "Bookings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_UserId_RequestId",
                table: "Bookings",
                columns: new[] { "UserId", "RequestId" },
                unique: true,
                filter: "[RequestId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Bookings_UserId_RequestId",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "RequestId",
                table: "Bookings");

        }
    }
}
