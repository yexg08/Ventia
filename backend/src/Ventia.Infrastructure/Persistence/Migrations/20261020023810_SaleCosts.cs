using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ventia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SaleCosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "UnitCost",
                table: "sale_lines",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UnitCost",
                table: "sale_lines");
        }
    }
}
