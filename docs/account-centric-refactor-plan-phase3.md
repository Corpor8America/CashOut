# Phase 3 — Delete Merchant Normalization & Old Global Reports

## Goal

Remove all now-dead code: merchant normalization service/controller/page/
models/tables, the old global report pages/service/controller, and the
now-unused `Alias`/`RawBusiness` links on `Transaction`. End state: a
smaller, account-centric app with no normalization machinery.

**Only start this phase after Phase 2 has been running in production for a
while and you're confident nothing needs the old alias/raw-business data.**
This phase includes a migration that drops tables and columns — it is not
easily reversible once applied to a real database (the `Down()` migration
will restore schema shape but not the alias/pattern data, which is gone
after Phase 2's truncate made it irrelevant anyway).

## Order of operations matters

Do these steps **in order**. Steps 1–3 must happen before step 6, because
step 6 deletes `MerchantNormalizationService.cs` and the files that steps
1–3's replacement code depends on for find/replace matching.

---

### Step 1 — Extract the dedup normalizer into its own helper

`CsvImportService` still calls `MerchantNormalizationService.Normalize(...)`
(a static method) purely to compute `NormalizedName` for CSV dedup
fingerprinting. Before deleting `MerchantNormalizationService`, move that
one method out to a small standalone helper.

Create **`CashOut/Helpers/TextNormalizer.cs`** (new file):

```csharp
using System.Text.RegularExpressions;

/// <summary>
/// Normalizes merchant/description strings into a canonical fingerprint.
/// Used only for CSV import deduplication (date + amount + normalized name).
/// Extracted from the old MerchantNormalizationService when merchant-alias
/// matching was removed — this is a dedup helper, not a categorization step.
/// </summary>
public static class TextNormalizer
{
    public static string Normalize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        var s = raw.Trim();
        s = Regex.Replace(s, @"\s*\([^)]*\)\s*$", " ");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        s = s.ToUpperInvariant();
        s = Regex.Replace(s, @"[-*./:,#]", " ");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        s = Regex.Replace(s, @"\b\d{7,}\b", " ");
        s = Regex.Replace(s, @"\s+", " ").Trim();

        return s;
    }
}
```

This is byte-for-byte the same normalization logic as
`MerchantNormalizationService.Normalize`, so dedup behavior is unchanged.

### Step 2 — Update `CashOut/Services/CsvImportService.cs`

Remove the `MerchantNormalizationService` dependency from the constructor. Find:

```csharp
using System.Text;
using Microsoft.EntityFrameworkCore;

public class CsvImportService
{
    private readonly AppDbContext _db;
    private readonly MerchantNormalizationService _normalization;

    public CsvImportService(AppDbContext db, MerchantNormalizationService normalization)
    {
        _db = db;
        _normalization = normalization;
    }
```

Replace with:

```csharp
using System.Text;
using Microsoft.EntityFrameworkCore;

public class CsvImportService
{
    private readonly AppDbContext _db;

    public CsvImportService(AppDbContext db)
    {
        _db = db;
    }
```

Find the dedup line added in Phase 2:

```csharp
            var normalizedName = MerchantNormalizationService.Normalize(description);
```

Replace with:

```csharp
            var normalizedName = TextNormalizer.Normalize(description);
```

### Step 3 — Update `CashOut/Services/TransactionService.cs`

Phase 2 introduced `MerchantNormalizationService.Normalize(txn.Name)` into
`TransactionService.MergePlaid` to compute `NormalizedName`. Now that
`MerchantNormalizationService` is being deleted, switch to the new helper.

Find:

```csharp
                var normalizedName = MerchantNormalizationService.Normalize(txn.Name);
```

Replace with:

```csharp
                var normalizedName = TextNormalizer.Normalize(txn.Name);
```

> **Note:** `TransactionService`'s constructor no longer takes a
> `MerchantNormalizationService` parameter — that was already removed in
> Phase 2 Step 1. Only the static `Normalize()` call needs updating here.

### Step 4 — Update `CashOut.Tests/CsvImportServiceTests.cs`

Find:

```csharp
    private static CsvImportService BuildSvc(AppDbContext db) =>
        new(db, new MerchantNormalizationService(db));
```

Replace with:

```csharp
    private static CsvImportService BuildSvc(AppDbContext db) =>
        new(db);
```

### Step 5 — Update `CashOut/Program.cs`

Remove the normalization and old global report service registrations. Find:

```csharp
builder.Services.AddScoped<MerchantNormalizationService>();
builder.Services.AddScoped<CsvImportService>();
builder.Services.AddScoped<PdfImportService>();
builder.Services.AddScoped<TransactionService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<AccountReportService>();
```

Replace with:

```csharp
builder.Services.AddScoped<CsvImportService>();
builder.Services.AddScoped<PdfImportService>();
builder.Services.AddScoped<TransactionService>();
builder.Services.AddScoped<AccountReportService>();
```

(If your `Program.cs` has these lines in a different order than shown,
just delete the `MerchantNormalizationService` and `ReportService`
registration lines specifically — leave `CsvImportService`,
`PdfImportService`, `TransactionService`, and `AccountReportService` as
they are.)

### Step 6 — Delete files

Delete these files entirely (they have no remaining references after
Steps 1–5):

```
CashOut/Services/MerchantNormalizationService.cs
CashOut/Controllers/BusinessNormalizationController.cs
CashOut/Pages/Merchants.razor
CashOut/Models/BusinessAlias.cs
CashOut/Models/AliasPattern.cs
CashOut/Models/RawBusiness.cs
CashOut/Models/RawBusinessAliasMap.cs
CashOut.Tests/MerchantNormalizationServiceTests.cs
CashOut/Pages/Reports.razor
CashOut/Pages/ReportCategory.razor
CashOut/Pages/ReportMerchant.razor
CashOut/Pages/ReportIncome.razor
CashOut/Pages/ReportCashFlow.razor
CashOut/Shared/ReportShell.razor
CashOut/Controllers/ReportsController.cs
CashOut/Services/ReportService.cs
CashOut.Tests/ReportServiceTests.cs
```

```bash
git rm \
  CashOut/Services/MerchantNormalizationService.cs \
  CashOut/Controllers/BusinessNormalizationController.cs \
  CashOut/Pages/Merchants.razor \
  CashOut/Models/BusinessAlias.cs \
  CashOut/Models/AliasPattern.cs \
  CashOut/Models/RawBusiness.cs \
  CashOut/Models/RawBusinessAliasMap.cs \
  CashOut.Tests/MerchantNormalizationServiceTests.cs \
  CashOut/Pages/Reports.razor \
  CashOut/Pages/ReportCategory.razor \
  CashOut/Pages/ReportMerchant.razor \
  CashOut/Pages/ReportIncome.razor \
  CashOut/Pages/ReportCashFlow.razor \
  CashOut/Shared/ReportShell.razor \
  CashOut/Controllers/ReportsController.cs \
  CashOut/Services/ReportService.cs \
  CashOut.Tests/ReportServiceTests.cs
```

### Step 7 — Update `CashOut/Shared/MainLayout.razor`

Remove the Reports and Merchants sidebar sections. Find this exact block:

```razor
            @if (_drawerOpen)
            {
                <MudText Typo="Typo.subtitle2" Color="Color.Primary" Class="ml-4 mt-4 mb-1">Reports</MudText>
            }
            <MudNavLink Href="/reports" Match="NavLinkMatch.All"
                        Icon="@Icons.Material.Filled.Dashboard">
                Executive Summary
            </MudNavLink>
            <MudNavLink Href="/reports/category"
                        Icon="@Icons.Material.Filled.Category">
                By Category
            </MudNavLink>
            <MudNavLink Href="/reports/merchant"
                        Icon="@Icons.Material.Filled.Store">
                By Merchant
            </MudNavLink>
            <MudNavLink Href="/reports/income"
                        Icon="@Icons.Material.Filled.TrendingUp">
                Income
            </MudNavLink>
            <MudNavLink Href="/reports/cashflow"
                        Icon="@Icons.Material.Filled.SwapVert">
                Cash Flow
            </MudNavLink>

            @if (_drawerOpen)
            {
                <MudText Typo="Typo.subtitle2" Color="Color.Primary" Class="ml-4 mt-4 mb-1">Merchants</MudText>
            }
            <MudNavLink Href="/merchants" Icon="@Icons.Material.Filled.Store">Merchants &amp; Aliases</MudNavLink>

            @if (_drawerOpen)
```

Replace with just:

```razor
            @if (_drawerOpen)
```

(This deletes the whole Reports + Merchants nav block, leaving the `@if
(_drawerOpen)` check that precedes the next section — "System" / Settings —
intact and untouched.)

### Step 8 — Update `CashOut/Models/Transaction.cs`

Remove the alias/raw-business FK properties. Find:

```csharp
    // ── Business normalization links ──────────────────────────────────────
    /// <summary>FK to the matched BusinessAlias, if any pattern matched during import.</summary>
    public int? AliasId { get; set; }
    public BusinessAlias? Alias { get; set; }

    /// <summary>
    /// FK to RawBusiness. Populated for transactions that did not match any alias pattern.
    /// Null for Plaid transactions that matched an alias (no RawBusiness created).
    /// </summary>
    public int? RawBusinessId { get; set; }
    public RawBusiness? RawBusiness { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
```

Replace with:

```csharp
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
```

`RawName` and `NormalizedName` stay on `Transaction` — they're still used
for CSV dedup fingerprinting via `TextNormalizer`.

### Step 9 — Update `CashOut/Data/AppDbContext.cs`

Remove the four `DbSet`s. Find:

```csharp
    public DbSet<LinkedAccount> LinkedAccounts => Set<LinkedAccount>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<ManualAccount> ManualAccounts => Set<ManualAccount>();
    public DbSet<RawBusiness> RawBusinesses => Set<RawBusiness>();
    public DbSet<BusinessAlias> BusinessAliases => Set<BusinessAlias>();
    public DbSet<AliasPattern> AliasPatterns => Set<AliasPattern>();
    public DbSet<RawBusinessAliasMap> RawBusinessAliasMaps => Set<RawBusinessAliasMap>();
    public DbSet<CsvMappingProfile> CsvMappingProfiles => Set<CsvMappingProfile>();
```

Replace with:

```csharp
    public DbSet<LinkedAccount> LinkedAccounts => Set<LinkedAccount>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<ManualAccount> ManualAccounts => Set<ManualAccount>();
    public DbSet<CsvMappingProfile> CsvMappingProfiles => Set<CsvMappingProfile>();
```

Remove the FK config inside the `Transaction` entity block. Find:

```csharp
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now() at time zone 'utc'");
            e.HasOne(x => x.Alias).WithMany().HasForeignKey(x => x.AliasId);
            e.HasOne(x => x.RawBusiness).WithMany().HasForeignKey(x => x.RawBusinessId);
        });
```

Replace with:

```csharp
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now() at time zone 'utc'");
        });
```

Remove the `RawBusiness`, `BusinessAlias`, `AliasPattern`, and
`RawBusinessAliasMap` entity configuration blocks entirely. Find and delete
this whole section:

```csharp
        // ── RawBusiness ───────────────────────────────────────────────────
        modelBuilder.Entity<RawBusiness>(e =>
        {
            e.ToTable("raw_businesses");
            e.HasKey(x => x.Id);
            e.Property(x => x.RawName).IsRequired();
            e.Property(x => x.RawNameNormalized).IsRequired().HasDefaultValue("");
            e.HasIndex(x => x.RawNameNormalized).IsUnique();
            e.Property(x => x.CategoryRaw).IsRequired().HasDefaultValue("");
            e.Property(x => x.IsMapped).IsRequired().HasDefaultValue(false);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now() at time zone 'utc'");
        });

        // ── BusinessAlias ─────────────────────────────────────────────────
        modelBuilder.Entity<BusinessAlias>(e =>
        {
            e.ToTable("business_aliases");
            e.HasKey(x => x.Id);
            e.Property(x => x.AliasName).IsRequired();
            e.HasIndex(x => x.AliasName).IsUnique();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now() at time zone 'utc'");
            e.HasMany(x => x.Patterns)
             .WithOne(x => x.Alias)
             .HasForeignKey(x => x.AliasId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── AliasPattern ──────────────────────────────────────────────────
        modelBuilder.Entity<AliasPattern>(e =>
        {
            e.ToTable("alias_patterns");
            e.HasKey(x => x.Id);
            e.Property(x => x.Pattern).IsRequired();
            e.Property(x => x.MatchType).HasConversion<string>().IsRequired();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now() at time zone 'utc'");
            e.HasOne(x => x.Alias)
             .WithMany(x => x.Patterns)
             .HasForeignKey(x => x.AliasId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── RawBusinessAliasMap ───────────────────────────────────────────
        modelBuilder.Entity<RawBusinessAliasMap>(e =>
        {
            e.ToTable("raw_business_alias_map");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.RawBusinessId).IsUnique();
            e.HasOne(x => x.RawBusiness).WithMany().HasForeignKey(x => x.RawBusinessId);
            e.HasOne(x => x.Alias).WithMany().HasForeignKey(x => x.AliasId);
        });

        // ── CsvMappingProfile ─────────────────────────────────────────────
```

Replace with just:

```csharp
        // ── CsvMappingProfile ─────────────────────────────────────────────
```

### Step 10 — Build, then generate the migration

Build first to catch any remaining references before generating the
migration (a migration generated against a broken build will be wrong):

```bash
dotnet build
```

Fix any compile errors before continuing — at this point there should be
none if Steps 1–8 were applied exactly as written. If the build references
`BusinessAlias`, `AliasPattern`, `RawBusiness`, or `RawBusinessAliasMap`
anywhere else not covered above (e.g. a controller or page not listed in
this doc), locate and remove that reference too before proceeding.

Then generate the migration:

```bash
dotnet ef migrations add RemoveMerchantNormalization --project CashOut
```

Inspect the generated migration file. It should contain, at minimum:

- `migrationBuilder.DropTable(name: "alias_patterns")`
- `migrationBuilder.DropTable(name: "raw_business_alias_map")`
- `migrationBuilder.DropTable(name: "business_aliases")`
- `migrationBuilder.DropTable(name: "raw_businesses")`
- `migrationBuilder.DropForeignKey` for `FK_transactions_business_aliases_AliasId`
- `migrationBuilder.DropForeignKey` for `FK_transactions_raw_businesses_RawBusinessId`
- `migrationBuilder.DropIndex` for `IX_transactions_AliasId` and
  `IX_transactions_RawBusinessId`
- `migrationBuilder.DropColumn(name: "AliasId", table: "transactions")`
- `migrationBuilder.DropColumn(name: "RawBusinessId", table: "transactions")`

If EF Core generates table drops in an order that violates FK constraints
(e.g. tries to drop `business_aliases` before `alias_patterns`, which
references it), reorder the `DropTable` calls in the generated `Up()`
method so dependent tables are dropped before the tables they reference:
`alias_patterns` and `raw_business_alias_map` before `business_aliases` and
`raw_businesses`.

Similarly, `DropForeignKey` and `DropIndex` operations on `transactions`
(e.g. `FK_transactions_business_aliases_AliasId`) must happen **before**
the `DropTable` calls for the referenced tables (`business_aliases`,
`raw_businesses`). EF Core usually gets this right, but verify it in the
generated migration.

### Step 11 — Build and test

```bash
dotnet build
dotnet test --filter "TestCategory!=UI"
```

Both must succeed. Expected remaining test files:
`EncryptionServiceTests`, `SettingsServiceTests`, `CsvImportServiceTests`
(with the Step 4 update). `MerchantNormalizationServiceTests` and
`ReportServiceTests` are gone (deleted in Step 6).

### Step 12 — Apply the migration and smoke-test

The app auto-migrates on startup (see `Program.cs`'s retry loop calling
`db.Database.Migrate()`), so just restart it against a real database:

```bash
docker compose -f docker-compose.dev.yml up -d --build
```

Then verify:

1. App starts without migration errors.
2. `/accounts` and `/manual-accounts` load and row-click still navigates to
   `/accounts/{id}`.
3. `/accounts/{id}` still shows Transactions, Cash Flow, and By Category
   tabs correctly.
4. `/reports`, `/reports/category`, `/reports/merchant`, `/reports/income`,
   `/reports/cashflow`, and `/merchants` all now 404 / show "Page not
   found" (expected — they're deleted).
5. Sidebar no longer shows "Reports" or "Merchants" sections — only
   Accounts, Data (Transactions), and System (Settings).
6. CSV import still works end-to-end (`/csv-import/{accountId}`) and
   correctly dedupes on re-upload.
7. Plaid sync (`Sync Transactions` button) still works.

### Step 13 — Update `README.md`

Find the **Merchant Normalization** section (the whole block from `##
Merchant Normalization` through the paragraph ending in "CSV/Plaid
categories are stored for reference only and never influence
categorization.") and delete it entirely — normalization no longer exists,
categories now come directly from the source.

Also update the **Features** list. Find:

```markdown
## Features
- Link bank/credit card accounts via Plaid
- Incremental transaction sync (cursor-based)
- Full year re-fetch
- CSV import with configurable column mapping
- **Merchant normalization** — alias patterns that auto-categorize messy merchant strings
- Reports: monthly totals, by category, pivot, top merchants, largest transactions
- CSV export
```

Replace with:

```markdown
## Features
- Link bank/credit card accounts via Plaid
- Incremental transaction sync (cursor-based)
- Full year re-fetch
- CSV import with configurable column mapping
- Per-account view: transaction list, cash flow, and spending by category
- CSV export
```

## Final verification checklist

- [ ] `dotnet build` succeeds with no errors or warnings about missing types
- [ ] `dotnet test --filter "TestCategory!=UI"` passes fully
- [ ] Migration applies cleanly on a fresh database (drop and recreate a
      local dev DB, let auto-migrate run all migrations from scratch)
- [ ] Migration applies cleanly on a copy of a database that went through
      Phase 1 and Phase 2 (i.e. still has the old normalization tables with
      stale data in them)
- [ ] No remaining references to `BusinessAlias`, `AliasPattern`,
      `RawBusiness`, `RawBusinessAliasMap`, or `MerchantNormalizationService`
      anywhere in the codebase (`grep -rn "MerchantNormalizationService\|BusinessAlias\|AliasPattern\|RawBusinessAliasMap\|RawBusiness" CashOut CashOut.Tests` should return nothing, aside from incidental matches you've reviewed)
- [ ] Full manual click-through of `/accounts` → account detail → all three
      tabs, for both a linked and a manual account
- [ ] README reflects the new feature set