using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanMate.Api.Migrations.AppDbContextSqlServerMigrations
{
    /// <inheritdoc />
    public partial class AddMomoPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "PaidAt",
                table: "Payments",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AlterColumn<int>(
                name: "CollectedByCleanerId",
                table: "Payments",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<string>(
                name: "PayUrl",
                table: "Payments",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderEnvironment",
                table: "Payments",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderPartnerCode",
                table: "Payments",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ProviderTransactionId",
                table: "Payments",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ProviderEnvironment_ProviderTransactionId",
                table: "Payments",
                columns: new[] { "ProviderEnvironment", "ProviderTransactionId" },
                unique: true,
                filter: "[ProviderEnvironment] IS NOT NULL AND [ProviderTransactionId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Payments_ProviderEnvironment_ProviderTransactionId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "PayUrl",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ProviderEnvironment",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ProviderPartnerCode",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ProviderTransactionId",
                table: "Payments");

            migrationBuilder.AlterColumn<DateTime>(
                name: "PaidAt",
                table: "Payments",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "CollectedByCleanerId",
                table: "Payments",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}
