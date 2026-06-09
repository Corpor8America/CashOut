using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Spening.Data.Migrations
{
    /// <inheritdoc />
    public partial class UpdateSettingsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop the old key-value app_settings table
            migrationBuilder.DropTable(name: "app_settings");

            // Create the new typed app_settings table with a single row
            migrationBuilder.CreateTable(
                name: "app_settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.None),
                    PlaidEnvironment = table.Column<string>(
                        type: "text", nullable: false, defaultValue: "sandbox")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_settings", x => x.Id);
                });

            // Seed the single settings row
            migrationBuilder.InsertData(
                table: "app_settings",
                columns: new[] { "Id", "PlaidEnvironment" },
                values: new object[] { 1, "sandbox" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "app_settings");

            // Restore the old key-value table
            migrationBuilder.CreateTable(
                name: "app_settings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_settings", x => x.Key);
                });

            migrationBuilder.InsertData(
                table: "app_settings",
                columns: new[] { "Key", "Value" },
                values: new object[,]
                {
                    { "plaid_environment", "sandbox" },
                    { "output_year", "2026" }
                });
        }
    }
}