using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaPOS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceLocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsLocked",
                table: "Sales",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockedAtUtc",
                table: "Sales",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LockedBy",
                table: "Sales",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsLocked",
                table: "Purchases",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockedAtUtc",
                table: "Purchases",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LockedBy",
                table: "Purchases",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            // Existing finalized invoices start locked; unlock is required before edit.
            migrationBuilder.Sql("""
                UPDATE Sales
                SET IsLocked = 1,
                    LockedAtUtc = SYSUTCDATETIME(),
                    LockedBy = COALESCE(NULLIF(LTRIM(RTRIM(CreatedBy)), ''), 'migration')
                WHERE IsLocked = 0
                  AND Status IN (2, 3, 5);

                UPDATE Purchases
                SET IsLocked = 1,
                    LockedAtUtc = SYSUTCDATETIME(),
                    LockedBy = COALESCE(NULLIF(LTRIM(RTRIM(CreatedBy)), ''), 'migration')
                WHERE IsLocked = 0
                  AND Status = 3;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsLocked",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "LockedAtUtc",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "LockedBy",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "IsLocked",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "LockedAtUtc",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "LockedBy",
                table: "Purchases");
        }
    }
}
