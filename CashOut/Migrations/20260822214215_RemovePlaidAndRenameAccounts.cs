using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CashOut.Migrations
{
    /// <inheritdoc />
    public partial class RemovePlaidAndRenameAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Remove Plaid-sourced transactions and any transactions/profiles
            // that belong to linked accounts before the table is dropped.
            migrationBuilder.Sql(@"
DELETE FROM transactions
WHERE ""Source"" = 'Plaid'
   OR ""AccountId"" IN (SELECT ""AccountId"" FROM linked_accounts)
   OR ""AccountId"" IN (SELECT ""Id""::text FROM linked_accounts);

DELETE FROM csv_mapping_profiles
WHERE ""AccountId"" IN (SELECT ""Id""::text FROM linked_accounts);
");

            migrationBuilder.DropTable(
                name: "linked_accounts");

            // Preserve existing manual accounts by renaming the table.
            migrationBuilder.RenameTable(
                name: "manual_accounts",
                newName: "accounts");

            migrationBuilder.Sql(
                @"ALTER TABLE accounts RENAME CONSTRAINT ""PK_manual_accounts"" TO ""PK_accounts"";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"ALTER TABLE accounts RENAME CONSTRAINT ""PK_accounts"" TO ""PK_manual_accounts"";");

            migrationBuilder.RenameTable(
                name: "accounts",
                newName: "manual_accounts");

            migrationBuilder.CreateTable(
                name: "linked_accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccessToken = table.Column<string>(type: "text", nullable: false),
                    AccountId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    Institution = table.Column<string>(type: "text", nullable: false),
                    ItemId = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    Mask = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Subtype = table.Column<string>(type: "text", nullable: false),
                    SyncCursor = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_linked_accounts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_linked_accounts_AccountId",
                table: "linked_accounts",
                column: "AccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_linked_accounts_ItemId",
                table: "linked_accounts",
                column: "ItemId");
        }
    }
}
