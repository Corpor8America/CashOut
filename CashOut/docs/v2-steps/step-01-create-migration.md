# Step 1: Create the Migration — Drop Normalization Tables + Simplify Transaction Entity

## Goal

Generate a single EF Core migration that transforms the database from the v1 schema (with normalization tables) to the v2 schema (raw storage, no normalization). This step also covers simplifying the entity models and DbContext to match.

---

## Part A: Entity Model Changes

### File: `CashOut/Models/Transaction.cs` — REPLACE entire file

```csharp
public enum TransactionSource { Plaid, CSV }

public class Transaction
{
    public string TransactionId { get; set; } = ""; // Plaid's stable ID or a generated key for CSV
    public string AccountId { get; set; } = "";     // Plaid account_id or ManualAccount.Id.ToString()

    /// <summary>Whether this came from Plaid sync or a CSV import.</summary>
    public TransactionSource Source { get; set; } = TransactionSource.Plaid;

    public DateOnly Date { get; set; }
    public string Name { get; set; } = "";
    public string RawName { get; set; } = "";

    /// <summary>
    /// Money entering the account (e.g. payroll, refund, credit card payment received).
    /// Exactly one of Credit or Debit is non-null per transaction.
    /// </summary>
    public decimal? Credit { get; set; }

    /// <summary>
    /// Money leaving the account (e.g. purchase, bill payment, withdrawal).
    /// Exactly one of Credit or Debit is non-null per transaction.
    /// </summary>
    public decimal? Debit { get; set; }

    /// <summary>
    /// Computed: Debit - Credit (stored for query/sort convenience).
    /// Positive = net outflow (expense/debit transaction).
    /// Negative = net inflow (income/refund/credit transaction).
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// Effective category for this transaction.
    /// Set from source during import:
    ///   - Plaid: personal_finance_category.primary, or category[0], or "Uncategorized"
    ///   - CSV: mapped category column value, or "Uncategorized"
    /// Immutable after import — no inline editing.
    /// </summary>
    public string Category { get; set; } = "";

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // ── Normalization helpers ─────────────────────────────────────────────

    /// <summary>
    /// Universal normalization rule for a single signed external amount.
    /// Applies to Plaid, CSV single-amount columns, and manual entries.
    ///
    /// Plaid sign convention: positive = outflow, negative = inflow.
    ///
    ///   externalAmount &lt; 0  → Credit = abs(amount), Debit = null,  Amount = -credit (negative)
    ///   externalAmount >= 0 → Debit  = amount,        Credit = null, Amount = debit  (positive)
    /// </summary>
    public static (decimal? credit, decimal? debit, decimal amount) NormalizeSingleAmount(
        decimal externalAmount)
    {
        if (externalAmount < 0)
        {
            var credit = Math.Abs(externalAmount);
            return (credit, null, -credit);
        }
        else
        {
            return (null, externalAmount, externalAmount);
        }
    }

    /// <summary>
    /// Normalization for CSV rows with separate Credit and Debit columns.
    /// Exactly one must be non-null; if both are set the row should be skipped upstream.
    ///
    ///   Credit row → Amount is negative (inflow)
    ///   Debit  row → Amount is positive (outflow)
    /// </summary>
    public static (decimal? credit, decimal? debit, decimal amount) NormalizeSplitColumns(
        decimal? rawCredit, decimal? rawDebit)
    {
        if (rawCredit.HasValue && !rawDebit.HasValue)
        {
            var c = Math.Abs(rawCredit.Value);
            return (c, null, -c);
        }
        if (rawDebit.HasValue && !rawCredit.HasValue)
        {
            var d = Math.Abs(rawDebit.Value);
            return (null, d, d);
        }
        // Both null or both set — caller should have caught this
        return (null, null, 0);
    }
}
```

**What was removed compared to v1:**
- `NormalizedName` property
- `AliasId` property + `Alias` navigation property
- `RawBusinessId` property + `RawBusiness` navigation property

**What was kept:**
- All other properties unchanged
- `NormalizeSingleAmount()` and `NormalizeSplitColumns()` static methods unchanged

### Files to DELETE entirely:

- `CashOut/Models/BusinessAlias.cs` — contained `BusinessAlias` entity (Id, AliasName, Category, CreatedAt, UpdatedAt, Patterns collection)
- `CashOut/Models/AliasPattern.cs` — contained `AliasPattern` entity (Id, AliasId, Pattern, MatchType, CreatedAt, UpdatedAt) and `AliasPatternMatchType` enum
- `CashOut/Models/RawBusiness.cs` — contained `RawBusiness` entity (Id, RawName, RawNameNormalized, CategoryRaw, IsMapped, CreatedAt, UpdatedAt)
- `CashOut/Models/RawBusinessAliasMap.cs` — contained `RawBusinessAliasMap` entity (Id, RawBusinessId, AliasId, navigation props)
- `CashOut/Models/AppSetting.cs` — contained `AppSetting` entity (Id=1, ExcludedCategories)

### Files UNCHANGED (keep as-is):

- `CashOut/Models/LinkedAccount.cs`
- `CashOut/Models/ManualAccount.cs`
- `CashOut/Models/CsvMappingProfile.cs`

---

## Part B: DbContext Changes

### File: `CashOut/Data/AppDbContext.cs` — REPLACE entire file

```csharp
using Microsoft.EntityFrameworkCore;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<LinkedAccount> LinkedAccounts => Set<LinkedAccount>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<ManualAccount> ManualAccounts => Set<ManualAccount>();
    public DbSet<CsvMappingProfile> CsvMappingProfiles => Set<CsvMappingProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ── LinkedAccount ─────────────────────────────────────────────────
        modelBuilder.Entity<LinkedAccount>(e =>
        {
            e.ToTable("linked_accounts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.AccountId).IsRequired();
            e.HasIndex(x => x.AccountId).IsUnique();
            e.Property(x => x.ItemId).IsRequired().HasDefaultValue("");
            e.HasIndex(x => x.ItemId);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");
        });

        // ── ManualAccount ─────────────────────────────────────────────────
        modelBuilder.Entity<ManualAccount>(e =>
        {
            e.ToTable("manual_accounts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Name).IsRequired();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");
        });

        // ── Transaction ───────────────────────────────────────────────────
        modelBuilder.Entity<Transaction>(e =>
        {
            e.ToTable("transactions");
            e.HasKey(x => x.TransactionId);
            e.Property(x => x.TransactionId).ValueGeneratedNever();
            e.Property(x => x.Source).HasConversion<string>().IsRequired();
            e.Property(x => x.Credit).IsRequired(false);
            e.Property(x => x.Debit).IsRequired(false);
            e.Property(x => x.Amount).IsRequired();
            e.Property(x => x.RawName).IsRequired().HasDefaultValue("");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now() at time zone 'utc'");
        });

        // ── CsvMappingProfile ─────────────────────────────────────────────
        modelBuilder.Entity<CsvMappingProfile>(e =>
        {
            e.ToTable("csv_mapping_profiles");
            e.HasKey(x => x.Id);
            e.Property(x => x.AccountId).IsRequired();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now() at time zone 'utc'");
        });
    }
}
```

**What was removed:**
- `DbSet<AppSetting>`, `DbSet<RawBusiness>`, `DbSet<BusinessAlias>`, `DbSet<AliasPattern>`
- All `modelBuilder.Entity<>()` blocks for: AppSetting, RawBusiness, BusinessAlias, AliasPattern, RawBusinessAliasMap
- From Transaction config: `NormalizedName` property config, `Alias` FK, `RawBusiness` FK

---

## Part C: Design-Time Factory (UNCHANGED)

### File: `CashOut/Data/AppDbContextFactory.cs` — NO CHANGES

Keep this file exactly as-is. It reads `ConnectionStrings__Default` from `.env` for migration tooling.

---

## Part D: Generate the Migration

### Prerequisites

1. Dev database running:
   ```bash
   docker-compose -f docker-compose.dev.yml up db -d
   ```

2. `.env` file at project root with:
   ```
   ConnectionStrings__Default=Host=localhost;Database=cashout;Username=cashout;Password=<DB_PASSWORD>
   ```

### Commands

```bash
# From the repo root directory
dotnet ef migrations add V2MinimalRedesign --project CashOut

# Verify build compiles
dotnet build CashOut/CashOut.csproj
```

### If the auto-generated migration doesn't produce proper DROP statements

Open the generated migration file in `CashOut/Migrations/` and replace the `Up` method with:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    // Drop child tables first (FK dependencies)
    migrationBuilder.Sql("DROP TABLE IF EXISTS raw_business_alias_map CASCADE");
    migrationBuilder.Sql("DROP TABLE IF EXISTS alias_patterns CASCADE");
    migrationBuilder.Sql("DROP TABLE IF EXISTS raw_businesses CASCADE");
    migrationBuilder.Sql("DROP TABLE IF EXISTS business_aliases CASCADE");
    migrationBuilder.Sql("DROP TABLE IF EXISTS app_settings CASCADE");

    // Drop columns from transactions
    migrationBuilder.Sql("ALTER TABLE transactions DROP COLUMN IF EXISTS normalized_name");
    migrationBuilder.Sql("ALTER TABLE transactions DROP COLUMN IF EXISTS alias_id");
    migrationBuilder.Sql("ALTER TABLE transactions DROP COLUMN IF EXISTS raw_business_id");
}
```

### Naming Convention

Use PascalCase: `V2MinimalRedesign` or `RemoveNormalizationAndSimplifyTransaction`

---

## Verification

1. `dotnet build CashOut/CashOut.csproj` compiles
2. Migration file exists in `CashOut/Migrations/` with proper drop operations
3. The following files will NOT compile yet — that's expected, fixed in subsequent steps:
   - `MerchantNormalizationService.cs` (deleted in Step 3)
   - `TransactionService.cs` (fixed in Steps 3+4)
   - `CsvImportService.cs` (fixed in Steps 3+4)
   - `SettingsService.cs` (fixed in Step 10)
