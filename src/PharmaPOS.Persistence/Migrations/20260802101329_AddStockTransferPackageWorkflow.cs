using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaPOS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStockTransferPackageWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockTransfers_TransferNumber",
                table: "StockTransfers");

            migrationBuilder.AddColumn<string>(
                name: "ExternalPackageKey",
                table: "StockTransfers",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FromBranchCode",
                table: "StockTransfers",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FromBranchName",
                table: "StockTransfers",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "StockTransfers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PackageKey",
                table: "StockTransfers",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ToBranchCode",
                table: "StockTransfers",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ToBranchName",
                table: "StockTransfers",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<int>(
                name: "SourceMedicineBatchId",
                table: "StockTransferItems",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<string>(
                name: "MedicineBarcode",
                table: "StockTransferItems",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MedicineName",
                table: "StockTransferItems",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_ExternalPackageKey",
                table: "StockTransfers",
                column: "ExternalPackageKey");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_PackageKey",
                table: "StockTransfers",
                column: "PackageKey");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_TransferNumber",
                table: "StockTransfers",
                column: "TransferNumber");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockTransfers_ExternalPackageKey",
                table: "StockTransfers");

            migrationBuilder.DropIndex(
                name: "IX_StockTransfers_PackageKey",
                table: "StockTransfers");

            migrationBuilder.DropIndex(
                name: "IX_StockTransfers_TransferNumber",
                table: "StockTransfers");

            migrationBuilder.DropColumn(
                name: "ExternalPackageKey",
                table: "StockTransfers");

            migrationBuilder.DropColumn(
                name: "FromBranchCode",
                table: "StockTransfers");

            migrationBuilder.DropColumn(
                name: "FromBranchName",
                table: "StockTransfers");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "StockTransfers");

            migrationBuilder.DropColumn(
                name: "PackageKey",
                table: "StockTransfers");

            migrationBuilder.DropColumn(
                name: "ToBranchCode",
                table: "StockTransfers");

            migrationBuilder.DropColumn(
                name: "ToBranchName",
                table: "StockTransfers");

            migrationBuilder.DropColumn(
                name: "MedicineBarcode",
                table: "StockTransferItems");

            migrationBuilder.DropColumn(
                name: "MedicineName",
                table: "StockTransferItems");

            migrationBuilder.AlterColumn<int>(
                name: "SourceMedicineBatchId",
                table: "StockTransferItems",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_TransferNumber",
                table: "StockTransfers",
                column: "TransferNumber",
                unique: true);
        }
    }
}
