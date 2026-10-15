using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanMate.Api.Migrations.AppDbContextSqlServerMigrations
{
    /// <inheritdoc />
    public partial class AddCustomerCompletionFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CustomerConfirmedAt",
                table: "Bookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CustomerRating",
                table: "Bookings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerReview",
                table: "Bookings",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomerConfirmedAt",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CustomerRating",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CustomerReview",
                table: "Bookings");
        }
    }
}
