using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Spening.Data.Migrations
{
    /// <inheritdoc />
    public partial class Fixings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_app_settings",
                table: "app_settings");

            migrationBuilder.DeleteData(
                table: "app_settings",
                keyColumn: "Key",
                keyColumnType: "text",
                keyValue: "output_year");

            migrationBuilder.DeleteData(
                table: "app_settings",
                keyColumn: "Key",
                keyColumnType: "text",
                keyValue: "plaid_environment");

            migrationBuilder.DropColumn(
                name: "Key",
                table: "app_settings");

            migrationBuilder.RenameColumn(
                name: "Value",
                table: "app_settings",
                newName: "PlaidEnvironment");

            migrationBuilder.AddColumn<string>(
                name: "ItemId",
                table: "linked_accounts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Id",
                table: "app_settings",
                type: "integer",
                nullable: false,
                defaultValue: 0)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddPrimaryKey(
                name: "PK_app_settings",
                table: "app_settings",
                column: "Id");

            migrationBuilder.InsertData(
                table: "app_settings",
                columns: new[] { "Id", "PlaidEnvironment" },
                values: new object[] { 1, "sandbox" });

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

            migrationBuilder.DropPrimaryKey(
                name: "PK_app_settings",
                table: "app_settings");

            migrationBuilder.DeleteData(
                table: "app_settings",
                keyColumn: "Id",
                keyColumnType: "integer",
                keyValue: 1);

            migrationBuilder.DropColumn(
                name: "ItemId",
                table: "linked_accounts");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "app_settings");

            migrationBuilder.RenameColumn(
                name: "PlaidEnvironment",
                table: "app_settings",
                newName: "Value");

            migrationBuilder.AddColumn<string>(
                name: "Key",
                table: "app_settings",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddPrimaryKey(
                name: "PK_app_settings",
                table: "app_settings",
                column: "Key");

            migrationBuilder.InsertData(
                table: "app_settings",
                columns: new[] { "Key", "Value" },
                values: new object[,]
                {
                    { "output_year", "2026" },
                    { "plaid_environment", "sandbox" }
                });
        }
    }
}
