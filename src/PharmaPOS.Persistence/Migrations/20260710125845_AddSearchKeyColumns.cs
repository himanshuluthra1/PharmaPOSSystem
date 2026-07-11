using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaPOS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchKeyColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NameSearchKey",
                table: "Suppliers",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                computedColumnSql: "REPLACE([Name], N' ', N'')",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "PhoneSearchKey",
                table: "Suppliers",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                computedColumnSql: "CAST(REPLACE(ISNULL([Phone], N''), N' ', N'') AS nvarchar(64))",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "BarcodeSearchKey",
                table: "Medicines",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                computedColumnSql: "REPLACE(ISNULL([Barcode], N''), N' ', N'')",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "GenericNameSearchKey",
                table: "Medicines",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                computedColumnSql: "REPLACE(ISNULL([GenericName], N''), N' ', N'')",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "NameSearchKey",
                table: "Medicines",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                computedColumnSql: "REPLACE([Name], N' ', N'')",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_NameSearchKey",
                table: "Suppliers",
                column: "NameSearchKey");

            migrationBuilder.CreateIndex(
                name: "IX_Medicines_BarcodeSearchKey",
                table: "Medicines",
                column: "BarcodeSearchKey");

            migrationBuilder.CreateIndex(
                name: "IX_Medicines_GenericNameSearchKey",
                table: "Medicines",
                column: "GenericNameSearchKey");

            migrationBuilder.CreateIndex(
                name: "IX_Medicines_NameSearchKey",
                table: "Medicines",
                column: "NameSearchKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Suppliers_NameSearchKey",
                table: "Suppliers");

            migrationBuilder.DropIndex(
                name: "IX_Medicines_BarcodeSearchKey",
                table: "Medicines");

            migrationBuilder.DropIndex(
                name: "IX_Medicines_GenericNameSearchKey",
                table: "Medicines");

            migrationBuilder.DropIndex(
                name: "IX_Medicines_NameSearchKey",
                table: "Medicines");

            migrationBuilder.DropColumn(
                name: "NameSearchKey",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "PhoneSearchKey",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "BarcodeSearchKey",
                table: "Medicines");

            migrationBuilder.DropColumn(
                name: "GenericNameSearchKey",
                table: "Medicines");

            migrationBuilder.DropColumn(
                name: "NameSearchKey",
                table: "Medicines");
        }
    }
}
