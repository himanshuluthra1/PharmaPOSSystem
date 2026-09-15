using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaPOS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleItemLooseQuantity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent: column may already exist if it was applied manually / via a prior partial run.
            migrationBuilder.Sql("""
                IF COL_LENGTH(N'dbo.SaleItems', N'LooseQuantity') IS NULL
                BEGIN
                    ALTER TABLE [SaleItems]
                    ADD [LooseQuantity] decimal(18,2) NOT NULL
                        CONSTRAINT [DF_SaleItems_LooseQuantity] DEFAULT (0);
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH(N'dbo.SaleItems', N'LooseQuantity') IS NOT NULL
                BEGIN
                    DECLARE @df sysname =
                        (SELECT dc.name
                         FROM sys.default_constraints dc
                         INNER JOIN sys.columns c
                             ON c.default_object_id = dc.object_id
                            AND c.object_id = dc.parent_object_id
                         WHERE dc.parent_object_id = OBJECT_ID(N'dbo.SaleItems')
                           AND c.name = N'LooseQuantity');
                    IF @df IS NOT NULL
                        EXEC(N'ALTER TABLE [SaleItems] DROP CONSTRAINT [' + @df + N']');
                    ALTER TABLE [SaleItems] DROP COLUMN [LooseQuantity];
                END
                """);
        }
    }
}
