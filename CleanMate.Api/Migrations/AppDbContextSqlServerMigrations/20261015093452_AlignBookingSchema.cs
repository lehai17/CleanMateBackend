using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace CleanMate.Api.Migrations.AppDbContextSqlServerMigrations;

public partial class AlignBookingSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<string>(
        name: "Address", table: "Users", type: "nvarchar(max)", nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(
        name: "Address", table: "Users");
}