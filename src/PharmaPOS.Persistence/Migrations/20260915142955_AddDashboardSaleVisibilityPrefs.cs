using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaPOS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDashboardSaleVisibilityPrefs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ShowDashboardMonthlySales",
                table: "CompanyProfiles",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShowDashboardTodaySales",
                table: "CompanyProfiles",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShowDashboardMonthlySales",
                table: "CompanyProfiles");

            migrationBuilder.DropColumn(
                name: "ShowDashboardTodaySales",
                table: "CompanyProfiles");
        }
    }
}
