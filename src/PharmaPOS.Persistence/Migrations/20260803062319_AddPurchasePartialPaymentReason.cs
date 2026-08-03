using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaPOS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchasePartialPaymentReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LinkedPurchaseReturnId",
                table: "Purchases",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartialPaymentNotes",
                table: "Purchases",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PartialPaymentReason",
                table: "Purchases",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ReturnCreditApplied",
                table: "Purchases",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CreditAppliedAmount",
                table: "PurchaseReturns",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_LinkedPurchaseReturnId",
                table: "Purchases",
                column: "LinkedPurchaseReturnId",
                unique: true,
                filter: "[LinkedPurchaseReturnId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Purchases_PurchaseReturns_LinkedPurchaseReturnId",
                table: "Purchases",
                column: "LinkedPurchaseReturnId",
                principalTable: "PurchaseReturns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Purchases_PurchaseReturns_LinkedPurchaseReturnId",
                table: "Purchases");

            migrationBuilder.DropIndex(
                name: "IX_Purchases_LinkedPurchaseReturnId",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "LinkedPurchaseReturnId",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "PartialPaymentNotes",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "PartialPaymentReason",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "ReturnCreditApplied",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "CreditAppliedAmount",
                table: "PurchaseReturns");
        }
    }
}
