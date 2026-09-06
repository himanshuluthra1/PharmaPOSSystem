using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaPOS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExpirySupplierClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReturnKind",
                table: "PurchaseReturns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ExpirySupplierClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClaimNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ClaimDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SupplierId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PurchaseReturnId = table.Column<int>(type: "int", nullable: false),
                    ExpectedCreditAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreditNoteNumber = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    CreditNoteDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreditNoteAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BranchId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpirySupplierClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExpirySupplierClaims_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ExpirySupplierClaims_PurchaseReturns_PurchaseReturnId",
                        column: x => x.PurchaseReturnId,
                        principalTable: "PurchaseReturns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExpirySupplierClaims_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExpirySupplierClaimItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClaimId = table.Column<int>(type: "int", nullable: false),
                    MedicineId = table.Column<int>(type: "int", nullable: false),
                    MedicineBatchId = table.Column<int>(type: "int", nullable: false),
                    PurchaseId = table.Column<int>(type: "int", nullable: true),
                    BatchNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    ExpiryDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StockQuantity = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ClaimQuantity = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PurchasePrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GstPercent = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    LineTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpirySupplierClaimItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExpirySupplierClaimItems_ExpirySupplierClaims_ClaimId",
                        column: x => x.ClaimId,
                        principalTable: "ExpirySupplierClaims",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExpirySupplierClaimItems_MedicineBatches_MedicineBatchId",
                        column: x => x.MedicineBatchId,
                        principalTable: "MedicineBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExpirySupplierClaimItems_Medicines_MedicineId",
                        column: x => x.MedicineId,
                        principalTable: "Medicines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExpirySupplierClaimItems_Purchases_PurchaseId",
                        column: x => x.PurchaseId,
                        principalTable: "Purchases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturns_ReturnKind",
                table: "PurchaseReturns",
                column: "ReturnKind");

            migrationBuilder.CreateIndex(
                name: "IX_ExpirySupplierClaimItems_ClaimId",
                table: "ExpirySupplierClaimItems",
                column: "ClaimId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpirySupplierClaimItems_MedicineBatchId",
                table: "ExpirySupplierClaimItems",
                column: "MedicineBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpirySupplierClaimItems_MedicineId",
                table: "ExpirySupplierClaimItems",
                column: "MedicineId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpirySupplierClaimItems_PurchaseId",
                table: "ExpirySupplierClaimItems",
                column: "PurchaseId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpirySupplierClaims_BranchId",
                table: "ExpirySupplierClaims",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpirySupplierClaims_ClaimDate",
                table: "ExpirySupplierClaims",
                column: "ClaimDate");

            migrationBuilder.CreateIndex(
                name: "IX_ExpirySupplierClaims_ClaimNumber",
                table: "ExpirySupplierClaims",
                column: "ClaimNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpirySupplierClaims_PurchaseReturnId",
                table: "ExpirySupplierClaims",
                column: "PurchaseReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpirySupplierClaims_SupplierId_Status",
                table: "ExpirySupplierClaims",
                columns: new[] { "SupplierId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExpirySupplierClaimItems");

            migrationBuilder.DropTable(
                name: "ExpirySupplierClaims");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseReturns_ReturnKind",
                table: "PurchaseReturns");

            migrationBuilder.DropColumn(
                name: "ReturnKind",
                table: "PurchaseReturns");
        }
    }
}
