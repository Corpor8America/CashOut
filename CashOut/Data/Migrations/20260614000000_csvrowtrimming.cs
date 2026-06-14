using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CashOut.Data.Migrations
{
    /// <inheritdoc />
    public partial class CsvRowTrimming : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE csv_mapping_profiles
                ADD COLUMN IF NOT EXISTS ""SkipRowsFromTop"" integer NOT NULL DEFAULT 0");

            migrationBuilder.Sql(@"
                ALTER TABLE csv_mapping_profiles
                ADD COLUMN IF NOT EXISTS ""SkipRowsFromBottom"" integer NOT NULL DEFAULT 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE csv_mapping_profiles
                DROP COLUMN IF EXISTS ""SkipRowsFromTop""");

            migrationBuilder.Sql(@"
                ALTER TABLE csv_mapping_profiles
                DROP COLUMN IF EXISTS ""SkipRowsFromBottom""");
        }
    }
}