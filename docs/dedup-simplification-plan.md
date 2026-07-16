# Dedup Simplification Plan

## Goal

Replace the multi-layer dedup system with a simpler additive-only approach:
- **CSV manual accounts:** Additive-only. Never delete. Re-uploading the same CSV is a no-op.
- **Plaid linked accounts:** Upsert by Plaid `transaction_id` only.
- **CSV and Plaid are mutually exclusive** — a CSV account is never a Plaid-linked account and vice versa.

## Rules

- **CSV dedup match:** date + exact signed amount + normalizedName
- **Never delete** transactions during CSV import
- **Never deduplicate within the same CSV** — two identical rows in the same file are both inserted
- **RawName** is always the original text from the source, never modified after insert
- **NormalizedName** is the cleaned version used for matching

---

## Changes

### 1. Remove `DedupKey` from model layer

**`CashOut/Models/Transaction.cs`**
- Delete the `DedupKey` property (lines 57-62)

**`CashOut/Data/AppDbContext.cs`**
- Delete `e.Property(x => x.DedupKey).IsRequired(false);` (line 52)

### 2. Rewrite `CsvImportService`

**`CashOut/Services/CsvImportService.cs`**

Delete entirely:
- `BuildDedupKey()` method (lines 542-550)
- `ScanForDuplicates()` method (lines 315-498) — no longer needed
- `DuplicateScanResult`, `ScannedRow`, `ExistingTransactionInfo`, `DuplicateType` types (lines 602-613)

Remove from `Import()`:
- `existingDedupKeys` HashSet loading (lines 102-105)
- DedupKey check per row (lines 150-158)
- `DedupKey = dedupKey` assignment (line 298)
- `existingDedupKeys.Add(dedupKey)` tracking (line 305)
- Cross-source dedup block (lines 257-275)
- Date-range pre-load of `existingTxns` (lines 116-137)

**New `Import()` logic:**
1. Parse all CSV rows into (date, name, amount, ...) tuples
2. Collect all distinct dates from the parsed rows
3. For each date, load existing DB transactions for that date + account
4. Build a `HashSet<(DateOnly date, decimal amount, string normalizedName)>` from the loaded transactions
5. For each CSV row, check if (date, amount, normalizedName) exists in the set
6. If match found → skip (already present in DB)
7. If no match → insert
8. **Never check for duplicates between rows in the same CSV** — two identical rows both get inserted
9. **Never delete** any existing transactions

**Updated `ImportResult` type:**
```csharp
public record ImportResult(int Imported, int SkippedAlreadyPresent, List<SkippedRow> SkippedRows);
```

Remove:
- `SkippedDuplicates` and `SkippedCrossSourceDuplicates` fields (merged into `SkippedAlreadyPresent`)
- `TotalSkipped` computed property

### 3. Simplify `TransactionService.MergePlaid()`

**`CashOut/Services/TransactionService.cs`**

Remove from `MergePlaid()`:
- Cross-source dedup pre-load (lines 139-150): `existingTxnsForDedup`
- `addedTxns` list (lines 152-153)
- Cross-source match predicate and check (lines 166-184)
- `addedTxns.Add(txn)` tracking (line 197)

Keep:
- Removal of transactions by Plaid `removedIds` (lines 110-135)
- Upsert by `TransactionId` dictionary (lines 155-164)
- Update path for existing transactions (lines 169-177)
- Insert path for new transactions (lines 186-195)

### 4. Remove `DeduplicateCrossSource()`

**`CashOut/Services/MerchantNormalizationService.cs`**

- Delete `DeduplicateCrossSource()` method (lines 487-526)
- Remove call in `MapRawToAlias()` (line 364)
- Remove call in `RetroactivelyMap()` (line 395)
- Stop writing back to `RawName` in `ReprocessUnaliasedTransactions()` (line 413)

### 5. Remove `Scan` endpoint and `forceImportRows`

**`CashOut/Controllers/CsvImportController.cs`**

- Delete the `Scan` endpoint entirely (lines 49-74)
- Remove `forceImportRows` form parameter from import endpoint (line 81)
- Remove parsing logic (lines 93-99)
- Don't pass it to `svc.Import()`

### 6. Simplify CSV import UI

**`CashOut/Pages/CsvImport.razor`**

Remove:
- Step 3 ("Review Duplicates") entirely — no scan step
- `_scanResult` state variable and related state
- `_forceImportRows` set and `ToggleForceImport()` method
- Force-import checkbox column from the review table
- Client-side mirror types: `ScannedRow`, `DuplicateScanResult`, `ExistingTransactionInfo`, `DuplicateType`

Simplify to 3-step flow: `Upload` → `Map` → `Import Result`

The import result step shows:
- Summary: `Imported` count, `SkippedAlreadyPresent` count, `SkippedRows` count
- List of skipped rows with reason (if any)

### 7. Update tests

**`CashOut.Tests/CsvImportServiceTests.cs`**

Replace existing tests with:
1. `Import_NewRows_InsertsAll` — No existing data, CSV has rows. Assert all inserted.
2. `Import_ExistingRows_SkipsAlreadyPresent` — Pre-populate DB with matching transactions. Import same CSV. Assert nothing inserted, `SkippedAlreadyPresent` matches count.
3. `Import_PartialOverlap_InsertsOnlyNew` — Pre-populate DB with some transactions. Import CSV with additional rows. Assert only new rows inserted.
4. `Import_DuplicateRowsInSameCsv_BothInserted` — CSV has two identical rows. Assert both inserted (no intra-batch dedup).

### 8. Generate migration and verify

```bash
dotnet ef migrations add RemoveDedupKey --project CashOut
dotnet build
dotnet test --filter "TestCategory!=UI"
```

---

## Files touched

| File | Action |
|------|--------|
| `CashOut/Models/Transaction.cs` | Remove `DedupKey` property |
| `CashOut/Data/AppDbContext.cs` | Remove `DedupKey` config |
| `CashOut/Services/CsvImportService.cs` | Delete `ScanForDuplicates`, rewrite `Import()` with additive-only dedup |
| `CashOut/Services/TransactionService.cs` | Remove cross-source dedup from `MergePlaid()` |
| `CashOut/Services/MerchantNormalizationService.cs` | Delete `DeduplicateCrossSource()`, remove calls from `MapRawToAlias`/`RetroactivelyMap` |
| `CashOut/Controllers/CsvImportController.cs` | Delete `Scan` endpoint, remove `forceImportRows` |
| `CashOut/Pages/CsvImport.razor` | Remove review/duplicates step, simplify to 3-step flow |
| `CashOut/Services/ReportService.cs` | No change (informational alert stays) |
| `CashOut.Tests/CsvImportServiceTests.cs` | Rewrite tests for additive-only dedup |
| `AGENTS.md` | Already updated |
