using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace CleanMate.Api.Migrations.Postgres;

public partial class AlignBookingSchemaPostgres : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Historical snapshots contained SQL Server types. The real PostgreSQL tables already
        // use native types, so only add genuinely missing columns; do not rewrite existing data.
        migrationBuilder.AddColumn<string>(name: "Address", table: "Users", type: "text", nullable: true);
        migrationBuilder.AddColumn<string>(name: "PhoneNumber", table: "Users", type: "text", nullable: true);
        migrationBuilder.AddColumn<string>(name: "Status", table: "Bookings", type: "text", nullable: false, defaultValue: "Pending");
        migrationBuilder.Sql("UPDATE \"Bookings\" SET \"Status\" = 'Accepted' WHERE \"CleanerId\" IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Address", table: "Users");
        migrationBuilder.DropColumn(name: "PhoneNumber", table: "Users");
        migrationBuilder.DropColumn(name: "Status", table: "Bookings");
    }
}