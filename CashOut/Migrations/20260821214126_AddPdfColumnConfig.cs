using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CashOut.Migrations
{
    /// <inheritdoc />
    public partial class AddPdfColumnConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PdfAmountColumnStart",
                table: "csv_mapping_profiles",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PdfDateColumnEnd",
                table: "csv_mapping_profiles",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PdfJoinContinuationRows",
                table: "csv_mapping_profiles",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PdfAmountColumnStart",
                table: "csv_mapping_profiles");

            migrationBuilder.DropColumn(
                name: "PdfDateColumnEnd",
                table: "csv_mapping_profiles");

            migrationBuilder.DropColumn(
                name: "PdfJoinContinuationRows",
                table: "csv_mapping_profiles");
        }
    }
}
