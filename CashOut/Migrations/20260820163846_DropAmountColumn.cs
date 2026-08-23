using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CashOut.Migrations
{
    /// <inheritdoc />
    public partial class DropAmountColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Amount",
                table: "transactions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Amount",
                table: "transactions",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);
        }
    }
}
