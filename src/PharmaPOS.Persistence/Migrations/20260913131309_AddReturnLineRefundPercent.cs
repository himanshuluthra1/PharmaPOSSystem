using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaPOS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReturnLineRefundPercent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "RefundPercent",
                table: "PurchaseReturnItems",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "RefundPercent",
                table: "ExpirySupplierClaimItems",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RefundPercent",
                table: "PurchaseReturnItems");

            migrationBuilder.DropColumn(
                name: "RefundPercent",
                table: "ExpirySupplierClaimItems");
        }
    }
}
