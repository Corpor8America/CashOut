using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CashOut.Migrations
{
    /// <inheritdoc />
    public partial class FlipAmountSignConvention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE transactions SET \"Amount\" = -\"Amount\" WHERE \"Amount\" != 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE transactions SET \"Amount\" = -\"Amount\" WHERE \"Amount\" != 0");
        }
    }
}
