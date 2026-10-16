using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanMate.Api.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddBookingRequestIdPostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "Bookings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestId",
                table: "Bookings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_UserId_RequestId",
                table: "Bookings",
                columns: new[] { "UserId", "RequestId" },
                unique: true);
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
