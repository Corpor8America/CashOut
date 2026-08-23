using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CashOut.Migrations
{
    /// <inheritdoc />
    public partial class AddNegativeIsCredit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "NegativeIsCredit",
                table: "csv_mapping_profiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NegativeIsCredit",
                table: "csv_mapping_profiles");
        }
    }
}
