using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CashOut.Data.Migrations
{
    /// <inheritdoc />
    public partial class FeatureExpansion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlaidEnvironment",
                table: "app_settings");

            migrationBuilder.AddColumn<int>(
                name: "AliasId",
                table: "transactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DedupKey",
                table: "transactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RawBusinessId",
                table: "transactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "transactions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "business_aliases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AliasName = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_aliases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "csv_mapping_profiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AccountId = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    DateColumn = table.Column<string>(type: "text", nullable: false),
                    DescriptionColumn = table.Column<string>(type: "text", nullable: false),
                    CreditColumn = table.Column<string>(type: "text", nullable: true),
                    DebitColumn = table.Column<string>(type: "text", nullable: true),
                    AmountColumn = table.Column<string>(type: "text", nullable: true),
                    CategoryColumn = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_csv_mapping_profiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "manual_accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_manual_accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "raw_businesses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RawName = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_raw_businesses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "raw_business_alias_map",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RawBusinessId = table.Column<int>(type: "integer", nullable: false),
                    AliasId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_raw_business_alias_map", x => x.Id);
                    table.ForeignKey(
                        name: "FK_raw_business_alias_map_business_aliases_AliasId",
                        column: x => x.AliasId,
                        principalTable: "business_aliases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_raw_business_alias_map_raw_businesses_RawBusinessId",
                        column: x => x.RawBusinessId,
                        principalTable: "raw_businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_business_aliases_AliasName",
                table: "business_aliases",
                column: "AliasName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_raw_business_alias_map_AliasId",
                table: "raw_business_alias_map",
                column: "AliasId");

            migrationBuilder.CreateIndex(
                name: "IX_raw_business_alias_map_RawBusinessId",
                table: "raw_business_alias_map",
                column: "RawBusinessId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_raw_businesses_RawName",
                table: "raw_businesses",
                column: "RawName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "csv_mapping_profiles");

            migrationBuilder.DropTable(
                name: "manual_accounts");

            migrationBuilder.DropTable(
                name: "raw_business_alias_map");

            migrationBuilder.DropTable(
                name: "business_aliases");

            migrationBuilder.DropTable(
                name: "raw_businesses");

            migrationBuilder.DropColumn(
                name: "AliasId",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "DedupKey",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "RawBusinessId",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "transactions");

            migrationBuilder.AddColumn<string>(
                name: "PlaidEnvironment",
                table: "app_settings",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "app_settings",
                keyColumn: "Id",
                keyValue: 1,
                column: "PlaidEnvironment",
                value: "sandbox");
        }
    }
}
