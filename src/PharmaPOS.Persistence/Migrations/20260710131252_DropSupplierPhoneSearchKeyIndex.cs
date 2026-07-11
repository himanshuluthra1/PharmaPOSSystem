using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaPOS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropSupplierPhoneSearchKeyIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE name = N'IX_Suppliers_PhoneSearchKey'
                      AND object_id = OBJECT_ID(N'Suppliers'))
                DROP INDEX [IX_Suppliers_PhoneSearchKey] ON [Suppliers];
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE name = N'IX_Suppliers_PhoneSearchKey'
                      AND object_id = OBJECT_ID(N'Suppliers'))
                  AND EXISTS (
                    SELECT 1 FROM sys.columns
                    WHERE name = N'PhoneSearchKey'
                      AND object_id = OBJECT_ID(N'Suppliers'))
                CREATE INDEX [IX_Suppliers_PhoneSearchKey] ON [Suppliers] ([PhoneSearchKey]);
                """);
        }
    }
}
