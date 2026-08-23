using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CashOut.Migrations
{
    /// <inheritdoc />
    public partial class AddPdfPagesAndRowFilter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PdfPages",
                table: "csv_mapping_profiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PdfRowFilter",
                table: "csv_mapping_profiles",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PdfPages",
                table: "csv_mapping_profiles");

            migrationBuilder.DropColumn(
                name: "PdfRowFilter",
                table: "csv_mapping_profiles");
        }
    }
}
