using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CashOut.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "csv_mapping_profiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AccountId = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    SkipRowsFromTop = table.Column<int>(type: "integer", nullable: false),
                    SkipRowsFromBottom = table.Column<int>(type: "integer", nullable: false),
                    DateColumn = table.Column<string>(type: "text", nullable: false),
                    DescriptionColumn = table.Column<string>(type: "text", nullable: false),
                    CreditColumn = table.Column<string>(type: "text", nullable: true),
                    DebitColumn = table.Column<string>(type: "text", nullable: true),
                    AmountColumn = table.Column<string>(type: "text", nullable: true),
                    CategoryColumn = table.Column<string>(type: "text", nullable: true),
                    PdfPages = table.Column<string>(type: "text", nullable: true),
                    PdfRowRegex = table.Column<string>(type: "text", nullable: true),
                    PdfDateColumnEnd = table.Column<decimal>(type: "numeric", nullable: true),
                    PdfAmountColumnStart = table.Column<decimal>(type: "numeric", nullable: true),
                    PdfJoinContinuationRows = table.Column<bool>(type: "boolean", nullable: false),
                    NegativeIsCredit = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_csv_mapping_profiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "transactions",
                columns: table => new
                {
                    TransactionId = table.Column<string>(type: "text", nullable: false),
                    AccountId = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    RawName = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    Credit = table.Column<decimal>(type: "numeric", nullable: true),
                    Debit = table.Column<decimal>(type: "numeric", nullable: true),
                    Category = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transactions", x => x.TransactionId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accounts");

            migrationBuilder.DropTable(
                name: "csv_mapping_profiles");

            migrationBuilder.DropTable(
                name: "transactions");
        }
    }
}
