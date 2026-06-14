using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CashOut.Data.Migrations
{
    /// <inheritdoc />
    public partial class TransactionMerchantIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE transactions
                ADD COLUMN IF NOT EXISTS ""RawName"" text NOT NULL DEFAULT ''");

            migrationBuilder.Sql(@"
                ALTER TABLE transactions
                ADD COLUMN IF NOT EXISTS ""NormalizedName"" text NOT NULL DEFAULT ''");

            migrationBuilder.Sql(@"
                UPDATE transactions
                SET ""RawName"" = ""Name""
                WHERE ""RawName"" = ''");

            migrationBuilder.Sql(@"
                UPDATE transactions
                SET ""NormalizedName"" =
                    TRIM(
                        regexp_replace(
                            regexp_replace(
                                regexp_replace(
                                    UPPER(
                                        regexp_replace(
                                            regexp_replace(TRIM(""RawName""), '\s+', ' ', 'g'),
                                            '\s*\([^)]*\)\s*$', ' ', 'g'
                                        )
                                    ),
                                    '[-*./:,#]', ' ', 'g'
                                ),
                                '\s+', ' ', 'g'
                            ),
                            '\m\d{7,}\M', '', 'g'
                        )
                    )
                WHERE ""NormalizedName"" = ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE transactions
                DROP COLUMN IF EXISTS ""NormalizedName""");

            migrationBuilder.Sql(@"
                ALTER TABLE transactions
                DROP COLUMN IF EXISTS ""RawName""");
        }
    }
}
