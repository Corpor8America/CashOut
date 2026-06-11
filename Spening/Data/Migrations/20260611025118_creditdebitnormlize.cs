using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Spening.Data.Migrations
{
    /// <inheritdoc />
    public partial class creditdebitnormlize : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Credit",
                table: "transactions",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Debit",
                table: "transactions",
                type: "numeric",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Credit",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "Debit",
                table: "transactions");
        }
    }
}
