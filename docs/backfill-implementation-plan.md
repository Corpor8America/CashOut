# CashOut — Backfill Feature Implementation Plan

**Target feature:** Allow users to import historical CSV transactions into a Plaid-linked account,  
with cross-source deduplication to prevent overlaps between Plaid data and CSV imports.

**Intended reader:** A code-capable agent with access to the CashOut repository. Follow each task  
in sequence. Do not skip steps. Each task lists every file to touch, the exact change to make,  
and how to verify it is correct before moving on.

---

## Overview of What Gets Built

1. A "Backfill (CSV)" button on the Linked Accounts page that routes to the existing CSV import flow with the linked account's ID.
2. A fuzzy fingerprint deduplication check inside `CsvImportService.Import` that compares incoming CSV rows against existing Plaid (and previous CSV) transactions for the same account.
3. Updated import result statistics that distinguish between same-file (internal) duplicates and cross-source duplicates (CSV vs. existing DB rows).
4. No new database migrations are required — all changes are in application code.

---

## Prerequisite Reading

Before writing any code, read these files in the repository:

- `CashOut/Services/CsvImportService.cs` — the import pipeline you will modify
- `CashOut/Pages/Accounts.razor` — the page you will add the button to
- `CashOut/Pages/CsvImport.razor` — the import UI you will update for new stats
- `CashOut/Controllers/CsvImportController.cs` — the API surface (no changes needed here)
- `CashOut/Models/Transaction.cs` — the `Transaction` model, particularly `NormalizeSingleAmount` and `NormalizeSplitColumns`
- `docs/backfill-plan.md` — the original design document

---

## Task 1 — Add the "Backfill (CSV)" button to the Linked Accounts page

**File:** `CashOut/Pages/Accounts.razor`

**Goal:** Each linked account row in the table should have a "Backfill (CSV)" button that navigates to `/csv-import/{AccountId}`, where `AccountId` is the Plaid `account_id` string (the `AccountId` property on `LinkedAccount`, not the row `Id` GUID).

**Exact change:**

Locate the `<RowTemplate>` block inside the `<MudTable>`. It currently has a single column for the Remove button:

```razor
<MudTd>
    @if (_confirmRemoveId == context.Id)
    { ... }
    else
    {
        <MudButton ... OnClick="() => RequestRemove(context.Id)">Remove</MudButton>
    }
</MudTd>
```

Replace that single `<MudTd>` with two `<MudTd>` elements — one for the new Backfill button and one for the existing Remove button:

```razor
<MudTd>
    <MudButton Variant="Variant.Outlined" Color="Color.Primary" Size="Size.Small"
               Href="@($"/csv-import/{context.AccountId}")"
               StartIcon="@Icons.Material.Filled.Upload">
        Backfill (CSV)
    </MudButton>
</MudTd>
<MudTd>
    @if (_confirmRemoveId == context.Id)
    {
        <MudStack Row="true" AlignItems="AlignItems.Center" Spacing="1">
            <MudText Typo="Typo.body2">Sure?</MudText>
            <MudButton Variant="Variant.Filled" Color="Color.Error" Size="Size.Small"
                       OnClick="() => ConfirmRemove(context.Id)">Yes, remove</MudButton>
            <MudButton Variant="Variant.Outlined" Size="Size.Small" OnClick="CancelRemove">Cancel</MudButton>
        </MudStack>
    }
    else
    {
        <MudButton Variant="Variant.Outlined" Color="Color.Error" Size="Size.Small"
                   OnClick="() => RequestRemove(context.Id)">Remove</MudButton>
    }
</MudTd>
```

Also update the `<HeaderContent>` to add a matching empty header column before the current final column:

```razor
<MudTh></MudTh>
<MudTh></MudTh>
```

**Verification:** Run the app (`dotnet run`), navigate to `/accounts`, and confirm each linked account row shows a "Backfill (CSV)" button. Clicking it should navigate to `/csv-import/{account_id}`. The Remove button should still work normally.

---

## Task 2 — Add a cross-source duplicate count to the import result model

**File:** `CashOut/Services/CsvImportService.cs`

**Goal:** The `ImportResult` record currently tracks `Imported`, `SkippedDuplicates`, and `SkippedRows`. Add a new field `CrossSourceDuplicates` to count rows that were skipped because they matched an existing transaction in the database (as opposed to an internal duplicate within the same CSV upload).

Locate the `ImportResult` record at the bottom of `CsvImportService.cs`:

```csharp
public record ImportResult(int Imported, int SkippedDuplicates, List<SkippedRow> SkippedRows)
{
    public int TotalSkipped => SkippedDuplicates + SkippedRows.Count;
}
```

Replace it with:

```csharp
public record ImportResult(int Imported, int SkippedDuplicates, int CrossSourceDuplicates, List<SkippedRow> SkippedRows)
{
    public int TotalSkipped => SkippedDuplicates + CrossSourceDuplicates + SkippedRows.Count;
}
```

**Note:** `SkippedDuplicates` will now count only intra-file duplicates (a row whose dedup key appears more than once within the same uploaded CSV). `CrossSourceDuplicates` counts rows that matched an existing DB transaction via the fuzzy fingerprint (see Task 3).

**Verification:** The file must compile. Run `dotnet build` and fix any errors before proceeding. There will be compile errors in `CsvImport.razor` and anywhere `ImportResult` is constructed — you will fix those in later tasks.

---

## Task 3 — Implement fuzzy fingerprint deduplication in `CsvImportService.Import`

**File:** `CashOut/Services/CsvImportService.cs`

This is the core of the feature. The `Import` method must:

1. Load all existing transactions for the target account that fall within the date range covered by the CSV.
2. For each incoming CSV row, after parsing its date and amount, check whether a "matching" transaction already exists in the database using the fuzzy fingerprint rules.
3. If a match is found, increment `crossSourceDuplicates` and skip the row (do not add it to `SkippedRows` — it is not an error, just a duplicate).

### 3a — Add a private helper method for the fuzzy match

Add this private static method anywhere inside the `CsvImportService` class, below the existing `TryParseAmount` helper:

```csharp
/// <summary>
/// Returns true if a CSV row is a fuzzy duplicate of an existing transaction.
/// Rules (ALL must match):
///   1. Exact date match.
///   2. Exact amount match (same absolute value; sign conventions are normalized before comparison).
///   3. Name similarity: one normalized name contains the other.
/// </summary>
private static bool IsCrossSourceDuplicate(
    DateOnly csvDate,
    decimal csvAmount,
    string csvNormalizedName,
    IEnumerable<Transaction> existingTransactions)
{
    foreach (var existing in existingTransactions)
    {
        if (existing.Date != csvDate)
            continue;

        if (existing.Amount != csvAmount)
            continue;

        var existingNorm = string.IsNullOrWhiteSpace(existing.NormalizedName)
            ? NormalizeForDedup(existing.Name)
            : existing.NormalizedName;

        if (existingNorm.Contains(csvNormalizedName, StringComparison.OrdinalIgnoreCase) ||
            csvNormalizedName.Contains(existingNorm, StringComparison.OrdinalIgnoreCase))
            return true;
    }

    return false;
}

/// <summary>
/// Minimal normalization for cross-source name comparison.
/// Uppercase, strip punctuation and long numeric sequences, collapse whitespace.
/// Mirrors the logic in MerchantNormalizationService.Normalize but is local
/// so CsvImportService has no dependency on that service for this check.
/// </summary>
private static string NormalizeForDedup(string raw)
{
    if (string.IsNullOrWhiteSpace(raw)) return "";
    var s = raw.Trim().ToUpperInvariant();
    s = System.Text.RegularExpressions.Regex.Replace(s, @"[-*./:,#]", " ");
    s = System.Text.RegularExpressions.Regex.Replace(s, @"\b\d{7,}\b", "");
    s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();
    return s;
}
```

### 3b — Load existing transactions at the start of `Import`

Inside the `Import` method, after the line that loads `existingDedupKeys`:

```csharp
var existingDedupKeys = await _db.Transactions
    .Where(t => t.AccountId == accountId && t.DedupKey != null)
    .Select(t => t.DedupKey!)
    .ToHashSetAsync();
```

Add the following block to determine the date range of the CSV and load existing transactions in that range:

```csharp
// Load existing transactions for cross-source deduplication.
// We scope to the date range present in the CSV to avoid loading everything.
// Parse dates from data rows first, then load matching DB rows.
var csvDates = new List<DateOnly>();
foreach (var row in dataRows)
{
    var rawDate = GetField(row, dateIdx);
    if (DateOnly.TryParse(rawDate, out var d))
        csvDates.Add(d);
}

List<Transaction> existingForCrossCheck = new();
if (csvDates.Count > 0)
{
    var minDate = csvDates.Min();
    var maxDate = csvDates.Max();
    existingForCrossCheck = await _db.Transactions
        .Where(t => t.AccountId == accountId && t.Date >= minDate && t.Date <= maxDate)
        .ToListAsync();
}
```

### 3c — Add the cross-source duplicate counter and check inside the row loop

At the top of the row loop (just after `int imported = 0;` and `int skippedDup = 0;`), add:

```csharp
int crossSourceDuplicates = 0;
```

Inside the row loop, after the existing dedup-key check:

```csharp
if (existingDedupKeys.Contains(dedupKey))
{
    skippedDup++;
    continue;
}
```

Add the cross-source check immediately after it (before the date parse block):

```csharp
// Parse date early so we can run the cross-source check
var rawDateEarly = GetField(row, dateIdx);
if (!DateOnly.TryParse(rawDateEarly, out var dateEarly))
{
    // Will be caught and reported properly below; skip cross-source check
    goto parseDateFull;
}

// Determine the net amount for this row (for cross-source match)
// We do a quick parse here; the full validation happens below
decimal crossCheckAmount = 0;
bool hasCrossCheckAmount = false;

if (amountIdx >= 0)
{
    var rawAmtCs = GetField(row, amountIdx);
    if (TryParseAmount(rawAmtCs, out var parsedCs) && parsedCs != 0)
    {
        var (_, _, amtCs) = Transaction.NormalizeSingleAmount(parsedCs);
        crossCheckAmount = amtCs;
        hasCrossCheckAmount = true;
    }
}
else
{
    var rawCredit = GetField(row, creditIdx);
    var rawDebit = GetField(row, debitIdx);
    bool hasC = !string.IsNullOrWhiteSpace(rawCredit);
    bool hasD = !string.IsNullOrWhiteSpace(rawDebit);
    if (hasC && TryParseAmount(rawCredit, out var c))
    {
        var (_, _, amtC) = Transaction.NormalizeSplitColumns(c, null);
        crossCheckAmount = amtC;
        hasCrossCheckAmount = true;
    }
    else if (hasD && TryParseAmount(rawDebit, out var d))
    {
        var (_, _, amtD) = Transaction.NormalizeSplitColumns(null, d);
        crossCheckAmount = amtD;
        hasCrossCheckAmount = true;
    }
}

if (hasCrossCheckAmount)
{
    var csvNorm = NormalizeForDedup(GetField(row, descIdx));
    if (IsCrossSourceDuplicate(dateEarly, crossCheckAmount, csvNorm, existingForCrossCheck))
    {
        crossSourceDuplicates++;
        existingDedupKeys.Add(dedupKey); // prevent re-import on future uploads
        continue;
    }
}

parseDateFull:;
```

**Important:** The `goto parseDateFull` label must be placed immediately before the existing `var rawDate = GetField(row, dateIdx);` line. In C#, you cannot declare variables after a label without a block, so restructure as follows — replace the existing date parse block:

```csharp
// Parse date (full validation, with error reporting)
parseDateFull:
var rawDate = GetField(row, dateIdx);
if (!DateOnly.TryParse(rawDate, out var date))
{
    skippedRows.Add(new SkippedRow(rawRowNum, TruncateRow(row), "Date could not be parsed"));
    continue;
}
```

### 3d — Update the `return` statement at the end of `Import`

Change:

```csharp
return new ImportResult(imported, skippedDup, skippedRows);
```

To:

```csharp
return new ImportResult(imported, skippedDup, crossSourceDuplicates, skippedRows);
```

**Verification:** Run `dotnet build`. There should be compile errors only in `CsvImport.razor` (from Task 4). Resolve those next.

---

## Task 4 — Update `CsvImport.razor` to display the new duplicate statistics

**File:** `CashOut/Pages/CsvImport.razor`

### 4a — Update the local `ImportResult` record

At the bottom of the `@code` block, find:

```csharp
private record ImportResult(int Imported, int SkippedDuplicates, List<SkippedRow> SkippedRows);
```

Replace with:

```csharp
private record ImportResult(int Imported, int SkippedDuplicates, int CrossSourceDuplicates, List<SkippedRow> SkippedRows);
```

### 4b — Update the result summary table

In the `@if (_step == Step.Result && _result != null)` section, find the `<MudSimpleTable>` that shows import statistics:

```razor
<MudSimpleTable Class="mb-6" Style="max-width:400px">
    <tbody>
        <tr>
            <td>Rows imported</td>
            <td style="text-align:right"><strong>@_result.Imported</strong></td>
        </tr>
        <tr>
            <td>Duplicates skipped</td>
            <td style="text-align:right">@_result.SkippedDuplicates</td>
        </tr>
        <tr>
            <td>Other skipped</td>
            <td style="text-align:right">@_result.SkippedRows.Count</td>
        </tr>
    </tbody>
</MudSimpleTable>
```

Replace it with:

```razor
<MudSimpleTable Class="mb-6" Style="max-width:400px">
    <tbody>
        <tr>
            <td>Rows imported</td>
            <td style="text-align:right"><strong>@_result.Imported</strong></td>
        </tr>
        <tr>
            <td>Duplicate within file</td>
            <td style="text-align:right">@_result.SkippedDuplicates</td>
        </tr>
        <tr>
            <td>Already in database (cross-source)</td>
            <td style="text-align:right">@_result.CrossSourceDuplicates</td>
        </tr>
        <tr>
            <td>Other skipped</td>
            <td style="text-align:right">@_result.SkippedRows.Count</td>
        </tr>
    </tbody>
</MudSimpleTable>
```

**Verification:** Run `dotnet build`. There should be zero errors. If errors remain, they are in the `goto` restructure from Task 3 — revisit Task 3c and ensure the label and variable scoping are correct.

---

## Task 5 — End-to-end verification

Run the full application against a real or sandbox Plaid account (or use the existing seeded data).

### Scenario A — New backfill with no overlap

1. Navigate to `/accounts`.
2. Click "Backfill (CSV)" on any linked account.
3. Upload a CSV file containing transactions with dates that do not exist in the database for that account.
4. Map columns and import.
5. **Expected result:** All rows imported. `CrossSourceDuplicates` = 0. `SkippedDuplicates` = 0.

### Scenario B — Backfill with full overlap (re-import same CSV)

1. Import a CSV file successfully (Scenario A result).
2. Import the exact same CSV file again to the same account.
3. **Expected result:** `Imported` = 0. `SkippedDuplicates` = 0 (dedup keys are present for the file-level check). `CrossSourceDuplicates` = N (all rows matched via fuzzy fingerprint from the DB transactions created in the first import — or via dedup key if already added to `existingDedupKeys`).

> Note: The dedup key check runs before the cross-source check. On re-import of the same file, rows will be caught by the dedup key check first (`SkippedDuplicates`), not the cross-source check. The cross-source check applies primarily when the same transaction appears in both Plaid sync and a CSV (different dedup keys, same underlying transaction).

### Scenario C — Backfill with partial Plaid overlap

1. Sync transactions from Plaid for an account (use `/api/transactions/sync`).
2. Export a CSV from your bank that covers the same date range.
3. Import that CSV via the Backfill button.
4. **Expected result:** Transactions that are in both Plaid and the CSV are counted in `CrossSourceDuplicates`. Net-new transactions (outside the Plaid sync window) are imported.

### Run unit tests

```bash
dotnet test --configuration Release --verbosity normal
```

All existing tests must pass. No new tests are strictly required by this plan, but adding a test for `IsCrossSourceDuplicate` in `CashOut.Tests` is encouraged.

---

## Task 6 — Optional: Add a unit test for the cross-source deduplication logic

**File:** `CashOut.Tests/CsvImportServiceTests.cs` (create if it does not exist)

Add the following test class:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CashOut.Tests;

[TestClass]
public class CsvImportServiceTests
{
    private static AppDbContext BuildDb(string dbName)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(opts);
    }

    private static MerchantNormalizationService BuildNorm(AppDbContext db) => new(db);

    private static CsvImportService BuildSvc(AppDbContext db) =>
        new(db, BuildNorm(db));

    // ── Cross-source deduplication ─────────────────────────────────────

    [TestMethod]
    public async Task Import_SkipsCrossSourceDuplicate_WhenPlaidTransactionMatchesCsvRow()
    {
        var db = BuildDb(nameof(Import_SkipsCrossSourceDuplicate_WhenPlaidTransactionMatchesCsvRow));

        // Seed an existing Plaid transaction
        db.Transactions.Add(new Transaction
        {
            TransactionId = "plaid-1",
            AccountId = "acct-1",
            Source = TransactionSource.Plaid,
            Date = new DateOnly(2025, 3, 15),
            Name = "AMAZON",
            RawName = "AMAZON",
            NormalizedName = "AMAZON",
            Amount = 49.99m,
            Debit = 49.99m,
            Category = "SHOPPING",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var svc = BuildSvc(db);

        // Save a profile for the account
        await svc.SaveProfile("acct-1", new CsvMappingProfile
        {
            AccountId = "acct-1",
            Version = 1,
            DateColumn = "Date",
            DescriptionColumn = "Description",
            AmountColumn = "Amount",
            CreatedAt = DateTime.UtcNow
        });

        // CSV contains the same transaction (same date, amount, similar name)
        const string csv = "Date,Description,Amount\n2025-03-15,Amazon.com,49.99\n";
        var profile = await svc.GetCurrentProfile("acct-1");

        var result = await svc.Import("acct-1", csv, profile!);

        Assert.AreEqual(0, result.Imported, "Should not import a duplicate");
        Assert.AreEqual(1, result.CrossSourceDuplicates, "Should count one cross-source duplicate");
        Assert.AreEqual(0, result.SkippedDuplicates, "Internal dup counter should be zero");
    }

    [TestMethod]
    public async Task Import_DoesNotSkip_WhenAmountDiffers()
    {
        var db = BuildDb(nameof(Import_DoesNotSkip_WhenAmountDiffers));

        db.Transactions.Add(new Transaction
        {
            TransactionId = "plaid-2",
            AccountId = "acct-1",
            Source = TransactionSource.Plaid,
            Date = new DateOnly(2025, 3, 15),
            Name = "AMAZON",
            RawName = "AMAZON",
            NormalizedName = "AMAZON",
            Amount = 99.00m,
            Debit = 99.00m,
            Category = "SHOPPING",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var svc = BuildSvc(db);
        await svc.SaveProfile("acct-1", new CsvMappingProfile
        {
            AccountId = "acct-1",
            Version = 1,
            DateColumn = "Date",
            DescriptionColumn = "Description",
            AmountColumn = "Amount",
            CreatedAt = DateTime.UtcNow
        });

        // Different amount — should NOT be a duplicate
        const string csv = "Date,Description,Amount\n2025-03-15,Amazon.com,49.99\n";
        var profile = await svc.GetCurrentProfile("acct-1");

        var result = await svc.Import("acct-1", csv, profile!);

        Assert.AreEqual(1, result.Imported, "Different amount should be imported");
        Assert.AreEqual(0, result.CrossSourceDuplicates);
    }
}
```

Run the tests again:

```bash
dotnet test --configuration Release --verbosity normal
```

---

## Summary of All File Changes

| File | Change |
|------|--------|
| `CashOut/Pages/Accounts.razor` | Add "Backfill (CSV)" button column to each linked account row |
| `CashOut/Services/CsvImportService.cs` | Add `CrossSourceDuplicates` to `ImportResult`; add `IsCrossSourceDuplicate` and `NormalizeForDedup` helpers; add date-range-scoped transaction load and fuzzy check inside `Import` |
| `CashOut/Pages/CsvImport.razor` | Update local `ImportResult` record; add "Already in database" row to result summary table |
| `CashOut.Tests/CsvImportServiceTests.cs` | New test file covering cross-source dedup logic (optional but recommended) |

No database migrations are required. No changes to controllers, routing, or DI registration are needed.

---

## Known Edge Cases and Accepted Behavior

**Two identical transactions on the same day (e.g., two $5.00 coffee purchases):** The fuzzy fingerprint will incorrectly treat the second as a duplicate if the merchant name is the same. This is an accepted trade-off documented in the original design — automated deduplication is better than manual cleanup of hundreds of overlapping rows during a bulk backfill.

**Plaid uses a very different merchant name than the bank's CSV:** If the Plaid name and CSV name do not share a common substring after normalization, the fuzzy check will not match, and the transaction will be imported as a new row. This is a false negative and is acceptable — the user can delete duplicates manually from the Transactions page.

**Dedup key vs. fuzzy check order:** The dedup key check (`existingDedupKeys`) runs first. If a row's SHA-256 fingerprint is already in the set, it is counted as `SkippedDuplicates` and never reaches the fuzzy check. The fuzzy check is only reached for rows with a new dedup key — meaning rows that have never been imported from any CSV before. This is intentional: the dedup key is an exact match (same CSV column values), while the fuzzy check covers the Plaid-vs-CSV overlap case.

**Performance:** The date-range-scoped load (`existingForCrossCheck`) pulls only transactions within the CSV's date span for the given account. For a 12-month backfill CSV on an account with 1,000 transactions per year, this loads ~1,000 rows into memory — acceptable. For very large datasets, consider adding a DB index on `(AccountId, Date)` in a future migration.