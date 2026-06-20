using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CashOut.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExcludedCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExcludedCategories",
                table: "app_settings",
                type: "text",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "app_settings",
                keyColumn: "Id",
                keyValue: 1,
                column: "ExcludedCategories",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExcludedCategories",
                table: "app_settings");
        }
    }
}
