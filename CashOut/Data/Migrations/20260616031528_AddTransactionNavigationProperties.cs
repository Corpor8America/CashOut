using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CashOut.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionNavigationProperties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_transactions_AliasId",
                table: "transactions",
                column: "AliasId");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_RawBusinessId",
                table: "transactions",
                column: "RawBusinessId");

            migrationBuilder.AddForeignKey(
                name: "FK_transactions_business_aliases_AliasId",
                table: "transactions",
                column: "AliasId",
                principalTable: "business_aliases",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_transactions_raw_businesses_RawBusinessId",
                table: "transactions",
                column: "RawBusinessId",
                principalTable: "raw_businesses",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_transactions_business_aliases_AliasId",
                table: "transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_transactions_raw_businesses_RawBusinessId",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "IX_transactions_AliasId",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "IX_transactions_RawBusinessId",
                table: "transactions");
        }
    }
}
