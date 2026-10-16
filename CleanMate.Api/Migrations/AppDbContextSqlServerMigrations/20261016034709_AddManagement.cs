using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CleanMate.Api.Migrations.AppDbContextSqlServerMigrations
{
    /// <inheritdoc />
    public partial class AddManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AlterColumn<int>(
                name: "BookingId",
                table: "Notifications",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<string>(
                name: "Target",
                table: "Notifications",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewReason",
                table: "CleanerProfiles",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewStatus",
                table: "CleanerProfiles",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Pending");

            // Existing operating cleaners retain access; only new applications wait for review.
            migrationBuilder.Sql("UPDATE [CleanerProfiles] SET [ReviewStatus] = 'Approved'");

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAt",
                table: "CleanerProfiles",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReviewedById",
                table: "CleanerProfiles",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAt",
                table: "CleanerProfiles",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AdminAudits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActorId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Target = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Details = table.Column<string>(type: "nvarchar(max)", maxLength: 12000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdminAudits_Users_ActorId",
                        column: x => x.ActorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ServiceOfferings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    HourlyRate = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MinHours = table.Column<int>(type: "int", nullable: false),
                    MaxHours = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceOfferings", x => x.Key);
                });

            migrationBuilder.InsertData(
                table: "ServiceOfferings",
                columns: new[] { "Key", "HourlyRate", "IsActive", "MaxHours", "MinHours", "Name", "UpdatedAt", "Version" },
                values: new object[,]
                {
                    { "childcare", 100000m, true, 8, 1, "Chăm Sóc Trẻ Em", null, 1 },
                    { "cleaning", 70000m, true, 8, 1, "Dọn Dẹp Tổng Quát", null, 1 },
                    { "cooking", 90000m, true, 8, 1, "Nấu Ăn & Rửa Bát", null, 1 },
                    { "laundry", 70000m, true, 8, 1, "Giặt Ủi Quần Áo", null, 1 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_CleanerProfiles_ReviewedById",
                table: "CleanerProfiles",
                column: "ReviewedById");

            migrationBuilder.CreateIndex(
                name: "IX_AdminAudits_ActorId",
                table: "AdminAudits",
                column: "ActorId");

            migrationBuilder.AddForeignKey(
                name: "FK_CleanerProfiles_Users_ReviewedById",
                table: "CleanerProfiles",
                column: "ReviewedById",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [Notifications] WHERE [BookingId] IS NULL) THROW 51000, 'Profile notifications exist; management migration cannot be rolled back without archiving them first.', 1");
            migrationBuilder.DropForeignKey(
                name: "FK_CleanerProfiles_Users_ReviewedById",
                table: "CleanerProfiles");

            migrationBuilder.DropTable(
                name: "AdminAudits");

            migrationBuilder.DropTable(
                name: "ServiceOfferings");

            migrationBuilder.DropIndex(
                name: "IX_CleanerProfiles_ReviewedById",
                table: "CleanerProfiles");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Target",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "ReviewReason",
                table: "CleanerProfiles");

            migrationBuilder.DropColumn(
                name: "ReviewStatus",
                table: "CleanerProfiles");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "CleanerProfiles");

            migrationBuilder.DropColumn(
                name: "ReviewedById",
                table: "CleanerProfiles");

            migrationBuilder.DropColumn(
                name: "SubmittedAt",
                table: "CleanerProfiles");

            migrationBuilder.AlterColumn<int>(
                name: "BookingId",
                table: "Notifications",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}
