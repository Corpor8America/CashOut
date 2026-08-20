# Step 4: Simplify Import Pipelines

## Goal

Rewrite `TransactionService.MergePlaid()` and `CsvImportService.Import()` to store transactions raw — no alias resolution, no `RawBusiness` creation, no `NormalizedName` population. Categories come directly from the source (Plaid `personal_finance_category.primary` or CSV column) and are immutable after import.

## Key Design Decisions

- **Transaction.Name** = the raw merchant string from the source (Plaid `name` or CSV description column). No alias resolution.
- **Transaction.RawName** = same as `Name` (keep the field but store the same raw value for backward compat)
- **Transaction.Category** = from source: Plaid's `personal_finance_category.primary` (fallback to `category[0]`), CSV mapped category column, or `"Uncategorized"` if none
- **No `NormalizedName`**, **no `AliasId`**, **no `RawBusinessId`** — these fields no longer exist on the entity

## File: `CashOut/Services/TransactionService.cs`

### Rewrite `MergePlaid()` Method

Current method (lines 90-175) does: remove deleted, resolve bulk aliases, set `AliasId`/`RawBusinessId`/`NormalizedName`/`Category` from normalization, upsert.

**New implementation:**

```csharp
private async Task<(int added, int removed)> MergePlaid(
    List<Transaction> incoming, List<string> removedIds)
{
    if (removedIds.Count > 0)
    {
        var toDelete = await _db.Transactions
            .Where(t => removedIds.Contains(t.TransactionId)
                        && t.Source == TransactionSource.Plaid)
            .ToListAsync();
        _db.Transactions.RemoveRange(toDelete);
    }

    int added = 0;

    if (incoming.Count > 0)
    {
        var incomingIds = incoming.Select(t => t.TransactionId).ToHashSet();
        var existingEntities = await _db.Transactions
            .Where(t => incomingIds.Contains(t.TransactionId))
            .ToDictionaryAsync(t => t.TransactionId);

        foreach (var txn in incoming)
        {
            // Store raw Plaid name directly — no alias resolution
            txn.RawName = txn.Name;

            if (!existingEntities.TryGetValue(txn.TransactionId, out var existing))
            {
                txn.CreatedAt = DateTime.UtcNow;
                txn.UpdatedAt = DateTime.UtcNow;
                _db.Transactions.Add(txn);
                added++;
            }
            else
            {
                existing.RawName = txn.Name;
                existing.Name = txn.Name;
                existing.Credit = txn.Credit;
                existing.Debit = txn.Debit;
                existing.Amount = txn.Amount;
                existing.Date = txn.Date;
                existing.UpdatedAt = DateTime.UtcNow;
                // Preserve existing category on update — don't overwrite from Plaid
                _db.Transactions.Update(existing);
            }
        }
    }

    await _db.SaveChangesAsync();
    return (added, removedIds.Count);
}
```

**Key changes from current code:**
- Removed: `_normalization.ResolveBulk()` call, `allPatterns`/`rawByNormalized` pre-loading
- Removed: `AliasId`, `RawBusinessId`, `NormalizedName` assignments
- Removed: `displayName = alias != null ? alias.AliasName : txn.Name` logic
- Removed: `effectiveCategory` from normalization — category is whatever Plaid provided
- `Name` is set directly to `txn.Name` (raw Plaid name)

### Remove `Query()` Category Alias Resolution

The current `Query()` method (lines 179-203) is already clean — it does `Where` filtering by category name and returns raw entities. No changes needed to this method.

## File: `CashOut/Services/CsvImportService.cs`

### Rewrite `Import()` Method — Normalization Section

The current `Import()` method (lines 104-265) pre-loads alias patterns and resolves each row through `_normalization.ResolveBulk()`. The dedup check uses `normalizedName`.

**Changes needed in the `Import()` method:**

1. **Remove lines 148-154** — the pattern/raw-business pre-loading:
   ```csharp
   // REMOVE:
   var allPatterns = await _db.AliasPatterns
       .Include(p => p.Alias)
       .ToListAsync();

   var rawByNormalized = await _db.RawBusinesses
       .ToDictionaryAsync(b => b.RawNameNormalized);
   ```

2. **Change the dedup check** (lines 156-167) — currently uses `normalizedName`. Replace with raw description:
   ```csharp
   var existingTuples = new HashSet<(DateOnly date, decimal amount, string rawName)>();
   foreach (var d in distinctDates)
   {
       var txnsForDate = await _db.Transactions
           .Where(t => t.AccountId == resolvedAccountId && t.Date == d)
           .ToListAsync();
       foreach (var t in txnsForDate)
           existingTuples.Add((t.Date, t.Amount, t.RawName));
   }
   ```

3. **Replace the normalization resolution block** (lines 202-214) — remove the `_normalization.ResolveBulk()` call and replace with simple raw storage:
   ```csharp
   // REPLACE the block that does:
   //   var (alias, rawBusiness, normalizedName, effectiveCategory) = await _normalization.ResolveBulk(...)
   //   var displayName = alias != null ? alias.AliasName : description;
   //
   // WITH:
   var rawName = description;  // raw CSV description
   ```

4. **Update the dedup check** — currently uses `normalizedName`:
   ```csharp
   // CHANGE:
   //   if (existingTuples.Contains((date, amount, normalizedName)))
   // TO:
   if (existingTuples.Contains((date, amount, rawName)))
   ```

5. **Update the Transaction construction** (lines 222-239):
   ```csharp
   // REMOVE all normalization-related fields. New construction:
   var txn = new Transaction
   {
       TransactionId = $"csv-{Guid.NewGuid()}",
       AccountId = resolvedAccountId,
       Source = TransactionSource.CSV,
       Date = date,
       Name = rawName,           // raw CSV description
       RawName = rawName,        // same as Name
       Credit = credit,
       Debit = debit,
       Amount = amount,
       Category = string.IsNullOrWhiteSpace(categoryRaw) ? "Uncategorized" : categoryRaw,
       CreatedAt = DateTime.UtcNow,
       UpdatedAt = DateTime.UtcNow
   };
   // REMOVED: NormalizedName, AliasId, Alias, RawBusinessId, RawBusiness
   ```

## File: `CashOut/Services/TransactionService.cs` — Also Remove `NormalizedName` Reference

In the `ExportCsv()` method (lines 240-258), the CSV export references `t.RawName`. This is fine — keep it as-is.

## Verification

```bash
dotnet build CashOut/CashOut.csproj
```

After this step, the build should compile cleanly. The import pipelines now store raw data with no normalization.

## Test Impact

The following existing tests will need updating (do this as part of Step 11 or now):
- `CashOut.Tests/CsvImportServiceTests.cs` — tests may reference normalization behavior
- `CashOut.Tests/ReportServiceTests.cs` — tests create `Transaction` objects with normalization fields; update the `MakeTxn` helper to not set `NormalizedName`, `AliasId`, `RawBusinessId`

The `MakeTxn` helper in `ReportServiceTests.cs` (lines 32-45) currently works because it only sets fields that still exist. But if any test sets `NormalizedName`, `AliasId`, or `RawBusinessId`, those references must be removed.
