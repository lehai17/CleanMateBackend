using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanMate.Api.Migrations.AppDbContextSqlServerMigrations
{
    /// <inheritdoc />
    public partial class AddPayOsBankPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BankAccountName",
                table: "Payments",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankAccountNumber",
                table: "Payments",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankTransactionReference",
                table: "Payments",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayOsLinkId",
                table: "Payments",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PayOsOrderCode",
                table: "Payments",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_PayOsLinkId",
                table: "Payments",
                column: "PayOsLinkId",
                unique: true,
                filter: "[PayOsLinkId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_PayOsOrderCode",
                table: "Payments",
                column: "PayOsOrderCode",
                unique: true,
                filter: "[PayOsOrderCode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ProviderPartnerCode_BankTransactionReference",
                table: "Payments",
                columns: new[] { "ProviderPartnerCode", "BankTransactionReference" },
                unique: true,
                filter: "[ProviderPartnerCode] IS NOT NULL AND [BankTransactionReference] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Payments_PayOsLinkId",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_PayOsOrderCode",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_ProviderPartnerCode_BankTransactionReference",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "BankAccountName",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "BankAccountNumber",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "BankTransactionReference",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "PayOsLinkId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "PayOsOrderCode",
                table: "Payments");
        }
    }
}
