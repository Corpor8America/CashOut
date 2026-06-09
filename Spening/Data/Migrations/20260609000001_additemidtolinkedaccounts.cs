using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Spening.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddItemIdToLinkedAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add item_id column — default empty string for existing rows
            migrationBuilder.AddColumn<string>(
                name: "ItemId",
                table: "linked_accounts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_linked_accounts_ItemId",
                table: "linked_accounts",
                column: "ItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_linked_accounts_ItemId",
                table: "linked_accounts");

            migrationBuilder.DropColumn(
                name: "ItemId",
                table: "linked_accounts");
        }
    }
}