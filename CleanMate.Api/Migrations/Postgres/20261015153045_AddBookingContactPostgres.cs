using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanMate.Api.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddBookingContactPostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContactName",
                table: "Bookings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactPhone",
                table: "Bookings",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContactName",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "ContactPhone",
                table: "Bookings");
        }
    }
}
