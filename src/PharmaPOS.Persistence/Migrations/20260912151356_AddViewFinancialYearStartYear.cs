using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaPOS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddViewFinancialYearStartYear : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ViewFinancialYearStartYear",
                table: "CompanyProfiles",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ViewFinancialYearStartYear",
                table: "CompanyProfiles");
        }
    }
}
