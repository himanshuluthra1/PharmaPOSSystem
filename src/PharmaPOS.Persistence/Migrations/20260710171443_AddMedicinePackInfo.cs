using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaPOS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMedicinePackInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PackInfo",
                table: "Medicines",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE Medicines
                SET PackInfo = LEFT(Notes, CHARINDEX(N' | ', Notes) - 1)
                WHERE Notes IS NOT NULL
                  AND Notes NOT LIKE N'MedWinId:%'
                  AND CHARINDEX(N' | ', Notes) > 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PackInfo",
                table: "Medicines");
        }
    }
}
