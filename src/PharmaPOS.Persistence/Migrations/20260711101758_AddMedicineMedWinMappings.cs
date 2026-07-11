using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaPOS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMedicineMedWinMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MedicineMedWinMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OneMgMedicineId = table.Column<int>(type: "int", nullable: false),
                    MedWinMedicineId = table.Column<int>(type: "int", nullable: true),
                    MedWinId = table.Column<int>(type: "int", nullable: false),
                    MedWinMedicineName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OneMgMedicineName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OneMgCatalogId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
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
                    table.PrimaryKey("PK_MedicineMedWinMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MedicineMedWinMappings_Medicines_OneMgMedicineId",
                        column: x => x.OneMgMedicineId,
                        principalTable: "Medicines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MedicineMedWinMappings_MedWinId",
                table: "MedicineMedWinMappings",
                column: "MedWinId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MedicineMedWinMappings_MedWinMedicineName",
                table: "MedicineMedWinMappings",
                column: "MedWinMedicineName");

            migrationBuilder.CreateIndex(
                name: "IX_MedicineMedWinMappings_OneMgMedicineId",
                table: "MedicineMedWinMappings",
                column: "OneMgMedicineId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MedicineMedWinMappings");
        }
    }
}
