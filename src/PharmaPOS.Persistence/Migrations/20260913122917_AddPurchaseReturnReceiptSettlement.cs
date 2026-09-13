using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaPOS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseReturnReceiptSettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReceiptSettlementKind",
                table: "PurchaseReturns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SettledAgainstPurchaseId",
                table: "PurchaseReturns",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturns_SettledAgainstPurchaseId",
                table: "PurchaseReturns",
                column: "SettledAgainstPurchaseId");

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseReturns_Purchases_SettledAgainstPurchaseId",
                table: "PurchaseReturns",
                column: "SettledAgainstPurchaseId",
                principalTable: "Purchases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseReturns_Purchases_SettledAgainstPurchaseId",
                table: "PurchaseReturns");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseReturns_SettledAgainstPurchaseId",
                table: "PurchaseReturns");

            migrationBuilder.DropColumn(
                name: "ReceiptSettlementKind",
                table: "PurchaseReturns");

            migrationBuilder.DropColumn(
                name: "SettledAgainstPurchaseId",
                table: "PurchaseReturns");
        }
    }
}
