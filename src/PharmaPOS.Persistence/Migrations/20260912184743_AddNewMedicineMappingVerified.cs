using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaPOS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNewMedicineMappingVerified : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsNewMappingVerified",
                table: "Medicines",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MappedCatalogueMedicineId",
                table: "Medicines",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Medicines_IsNewMappingVerified",
                table: "Medicines",
                column: "IsNewMappingVerified");

            migrationBuilder.CreateIndex(
                name: "IX_Medicines_MappedCatalogueMedicineId",
                table: "Medicines",
                column: "MappedCatalogueMedicineId");

            migrationBuilder.AddForeignKey(
                name: "FK_Medicines_Medicines_MappedCatalogueMedicineId",
                table: "Medicines",
                column: "MappedCatalogueMedicineId",
                principalTable: "Medicines",
                principalColumn: "Id",
                onDelete: ReferentialAction.NoAction);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Medicines_Medicines_MappedCatalogueMedicineId",
                table: "Medicines");

            migrationBuilder.DropIndex(
                name: "IX_Medicines_IsNewMappingVerified",
                table: "Medicines");

            migrationBuilder.DropIndex(
                name: "IX_Medicines_MappedCatalogueMedicineId",
                table: "Medicines");

            migrationBuilder.DropColumn(
                name: "IsNewMappingVerified",
                table: "Medicines");

            migrationBuilder.DropColumn(
                name: "MappedCatalogueMedicineId",
                table: "Medicines");
        }
    }
}
