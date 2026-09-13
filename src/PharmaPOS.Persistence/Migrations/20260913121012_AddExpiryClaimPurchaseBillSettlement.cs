using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaPOS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExpiryClaimPurchaseBillSettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CreditSettlementKind",
                table: "ExpirySupplierClaims",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SettledAgainstPurchaseId",
                table: "ExpirySupplierClaims",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpirySupplierClaims_SettledAgainstPurchaseId",
                table: "ExpirySupplierClaims",
                column: "SettledAgainstPurchaseId");

            migrationBuilder.AddForeignKey(
                name: "FK_ExpirySupplierClaims_Purchases_SettledAgainstPurchaseId",
                table: "ExpirySupplierClaims",
                column: "SettledAgainstPurchaseId",
                principalTable: "Purchases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExpirySupplierClaims_Purchases_SettledAgainstPurchaseId",
                table: "ExpirySupplierClaims");

            migrationBuilder.DropIndex(
                name: "IX_ExpirySupplierClaims_SettledAgainstPurchaseId",
                table: "ExpirySupplierClaims");

            migrationBuilder.DropColumn(
                name: "CreditSettlementKind",
                table: "ExpirySupplierClaims");

            migrationBuilder.DropColumn(
                name: "SettledAgainstPurchaseId",
                table: "ExpirySupplierClaims");
        }
    }
}
