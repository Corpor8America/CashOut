using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CashOut.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDedupKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DedupKey",
                table: "transactions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DedupKey",
                table: "transactions",
                type: "text",
                nullable: true);
        }
    }
}
