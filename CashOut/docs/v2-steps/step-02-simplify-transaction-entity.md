# Step 2: Simplify Transaction Entity

## Goal

Remove normalization-related properties from the `Transaction` entity, remove the four normalization entity classes entirely, remove the `AppSetting` entity and its `DbSet`, and simplify `AppDbContext`.

## Files to Modify

### 1. `CashOut/Models/Transaction.cs` — Remove Properties + Nav Props

**Remove these properties and navigation properties:**

```csharp
// REMOVE these properties:
public string NormalizedName { get; set; } = "";
public int? AliasId { get; set; }
public BusinessAlias? Alias { get; set; }
public int? RawBusinessId { get; set; }
public RawBusiness? RawBusiness { get; set; }
```

**Keep everything else:**
- `TransactionId`, `AccountId`, `Source`, `Date`, `Name`, `RawName`
- `Credit`, `Debit`, `Amount`
- `Category`
- `CreatedAt`, `UpdatedAt`
- `NormalizeSingleAmount()` and `NormalizeSplitColumns()` static methods

**After edit, Transaction.cs should contain:**

```csharp
public enum TransactionSource { Plaid, CSV }

public class Transaction
{
    public string TransactionId { get; set; } = "";
    public string AccountId { get; set; } = "";
    public TransactionSource Source { get; set; } = TransactionSource.Plaid;
    public DateOnly Date { get; set; }
    public string Name { get; set; } = "";
    public string RawName { get; set; } = "";
    public decimal? Credit { get; set; }
    public decimal? Debit { get; set; }
    public decimal Amount { get; set; }
    public string Category { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Keep both static methods: NormalizeSingleAmount and NormalizeSplitColumns
    // (exact code unchanged from current file)
}
```

### 2. Delete These Entity Files Entirely

- `CashOut/Models/BusinessAlias.cs`
- `CashOut/Models/AliasPattern.cs`
- `CashOut/Models/RawBusiness.cs`
- `CashOut/Models/RawBusinessAliasMap.cs`
- `CashOut/Models/AppSetting.cs`

### 3. `CashOut/Data/AppDbContext.cs` — Remove DbSets + OnModelCreating Config

**Remove these DbSet properties (lines 14-17 in current file):**

```csharp
// REMOVE:
public DbSet<AppSetting> AppSettings => Set<AppSetting>();
public DbSet<RawBusiness> RawBusinesses => Set<RawBusiness>();
public DbSet<BusinessAlias> BusinessAliases => Set<BusinessAlias>();
public DbSet<AliasPattern> AliasPatterns => Set<AliasPattern>();
```

**Keep these DbSets:**
```csharp
public DbSet<LinkedAccount> LinkedAccounts => Set<LinkedAccount>();
public DbSet<Transaction> Transactions => Set<Transaction>();
public DbSet<ManualAccount> ManualAccounts => Set<ManualAccount>();
public DbSet<CsvMappingProfile> CsvMappingProfiles => Set<CsvMappingProfile>();
```

**Remove these entire `modelBuilder.Entity<>` blocks from `OnModelCreating`:**

- The `AppSetting` block (currently lines ~91-96):
  ```csharp
  modelBuilder.Entity<AppSetting>(e => { ... });
  ```

- The `RawBusiness` block (currently lines ~98-110):
  ```csharp
  modelBuilder.Entity<RawBusiness>(e => { ... });
  ```

- The `BusinessAlias` block (currently lines ~112-124):
  ```csharp
  modelBuilder.Entity<BusinessAlias>(e => { ... });
  ```

- The `AliasPattern` block (currently lines ~126-138):
  ```csharp
  modelBuilder.Entity<AliasPattern>(e => { ... });
  ```

- The `RawBusinessAliasMap` block (currently lines ~140-146):
  ```csharp
  modelBuilder.Entity<RawBusinessAliasMap>(e => { ... });
  ```

**Simplify the `Transaction` entity block** — remove these lines:

```csharp
// REMOVE from the Transaction config block:
e.Property(x => x.NormalizedName).IsRequired().HasDefaultValue("");
e.HasOne(x => x.Alias).WithMany().HasForeignKey(x => x.AliasId);
e.HasOne(x => x.RawBusiness).WithMany().HasForeignKey(x => x.RawBusinessId);
```

**After edit, the Transaction config should be:**

```csharp
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
```

## Verification

```bash
dotnet build CashOut/CashOut.csproj
```

The build will not compile with zero errors at this point because other files still reference the deleted types. The following will fail until subsequent steps fix them:

- `MerchantNormalizationService.cs` — references `BusinessAlias`, `AliasPattern`, `RawBusiness`, `RawBusinessAliasMap` (deleted in Step 3)
- `TransactionService.cs` — references `MerchantNormalizationService` and normalization properties (fixed in Step 3+4)
- `CsvImportService.cs` — references `MerchantNormalizationService` (fixed in Step 3+4)
- `SettingsService.cs` — references `AppSetting` and `ExcludedCategories` (fixed in Step 10)

**If doing Steps 1+2 combined**, generate the migration now before proceeding to Step 3.
