# Phase 2 — Cutover: Category From Source, Wipe, Reimport

## Goal

Stop using merchant-alias normalization to set `Transaction.Category`.
Instead, Category comes directly from the source: Plaid's
`personal_finance_category.primary` for synced transactions, or the mapped
CSV category column for imports. Then wipe all existing transaction data
(which was categorized under the old alias-driven scheme) and reimport
everything cleanly.

**Do not start this phase until Phase 1 is deployed and you're happy with
the new `/accounts/{id}` pages.** This phase is destructive to transaction
data (recoverable only by reimporting/re-syncing).

This phase does **not** delete `MerchantNormalizationService`, its
controller, its page, or its tables — that's Phase 3. It only stops
*calling* the normalization pipeline during import/sync, so those tables
become unreferenced-but-still-present until Phase 3 cleans them up.

## What changes and what doesn't

- **Changes:** `TransactionService.MergePlaid` (Plaid sync/fetch path),
  `CsvImportService.Import` (CSV path). Both stop calling
  `MerchantNormalizationService.ResolveBulk` and stop setting
  `AliasId`/`RawBusinessId`/alias-driven `Category`.
- **Doesn't change:** `PlaidService.MapTransaction` — it already sets
  `Category` from `personal_finance_category.primary` correctly; that value
  just needs to survive downstream without being overwritten.
- **Doesn't change:** `MerchantNormalizationService.Normalize()` (the
  static text-normalization method) is still used in *both*
  `TransactionService.MergePlaid` (for `NormalizedName`) and
  `CsvImportService` (for CSV dedup fingerprinting) — not for
  categorization or alias matching.

## Step-by-step

### Step 1 — Edit `CashOut/Services/TransactionService.cs`

Remove the normalization dependency from the constructor. Find:

```csharp
    private readonly AppDbContext _db;
    private readonly PlaidService _plaid;
    private readonly SettingsService _settings;
    private readonly MerchantNormalizationService _normalization;

    public TransactionService(
        AppDbContext db,
        PlaidService plaid,
        SettingsService settings,
        MerchantNormalizationService normalization)
    {
        _db = db;
        _plaid = plaid;
        _settings = settings;
        _normalization = normalization;
    }
```

Replace with:

```csharp
    private readonly AppDbContext _db;
    private readonly PlaidService _plaid;
    private readonly SettingsService _settings;

    public TransactionService(
        AppDbContext db,
        PlaidService plaid,
        SettingsService settings)
    {
        _db = db;
        _plaid = plaid;
        _settings = settings;
    }
```

Now find the body of `MergePlaid` (the whole `if (incoming.Count > 0)`
block). The exact current text is:

```csharp
        int added = 0;

        if (incoming.Count > 0)
        {
            // Batch-load existing Plaid transactions by TransactionId to detect upserts
            var incomingIds = incoming.Select(t => t.TransactionId).ToHashSet();
            var existingEntities = await _db.Transactions
                .Where(t => incomingIds.Contains(t.TransactionId))
                .ToDictionaryAsync(t => t.TransactionId);

            // Batch-load alias patterns and raw businesses for normalization
            var allPatterns = await _db.AliasPatterns
                .Include(p => p.Alias)
                .ToListAsync();

            var rawNames = incoming
                .Select(t => MerchantNormalizationService.Normalize(t.Name))
                .ToHashSet();

            var rawByNormalized = await _db.RawBusinesses
                .Where(b => rawNames.Contains(b.RawNameNormalized))
                .ToDictionaryAsync(b => b.RawNameNormalized);

            foreach (var txn in incoming)
            {
                var (alias, rawBusiness, normalizedName, effectiveCategory) = await _normalization.ResolveBulk(
                    txn.Name, txn.Category, allPatterns, rawByNormalized);

                // When an alias matched, display the canonical alias name.
                // RawName always preserves the original string from Plaid.
                var displayName = alias != null ? alias.AliasName : txn.Name;

                if (!existingEntities.TryGetValue(txn.TransactionId, out var existing))
                {
                    txn.AliasId = alias?.Id;
                    txn.Alias = alias;
                    txn.RawBusinessId = rawBusiness?.Id == 0 ? null : rawBusiness?.Id;
                    txn.RawBusiness = rawBusiness;
                    txn.RawName = txn.Name;
                    txn.NormalizedName = normalizedName;
                    txn.Name = displayName;
                    txn.Category = effectiveCategory;
                    txn.CreatedAt = DateTime.UtcNow;
                    txn.UpdatedAt = DateTime.UtcNow;
                    _db.Transactions.Add(txn);
                    added++;
                }
                else
                {
                    existing.RawName = txn.Name;
                    existing.NormalizedName = normalizedName;
                    existing.Name = displayName;
                    existing.Credit = txn.Credit;
                    existing.Debit = txn.Debit;
                    existing.Amount = txn.Amount;
                    existing.Date = txn.Date;
                    existing.UpdatedAt = DateTime.UtcNow;
                    existing.AliasId = alias?.Id;
                    existing.Alias = alias;
                    existing.RawBusinessId = rawBusiness?.Id == 0 ? null : rawBusiness?.Id;
                    existing.RawBusiness = rawBusiness;
                    // Only update category if alias is set or existing has no category
                    if (alias != null || string.IsNullOrEmpty(existing.Category))
                        existing.Category = effectiveCategory;
                    _db.Transactions.Update(existing);
                }
            }
        }
```

Replace the entire block with:

```csharp
        int added = 0;

        if (incoming.Count > 0)
        {
            // Batch-load existing Plaid transactions by TransactionId to detect upserts
            var incomingIds = incoming.Select(t => t.TransactionId).ToHashSet();
            var existingEntities = await _db.Transactions
                .Where(t => incomingIds.Contains(t.TransactionId))
                .ToDictionaryAsync(t => t.TransactionId);

            foreach (var txn in incoming)
            {
                // Category comes directly from Plaid's personal_finance_category,
                // already set in PlaidService.MapTransaction — no normalization
                // or alias override applied here.
                var normalizedName = MerchantNormalizationService.Normalize(txn.Name);

                if (!existingEntities.TryGetValue(txn.TransactionId, out var existing))
                {
                    txn.RawName = txn.Name;
                    txn.NormalizedName = normalizedName;
                    txn.CreatedAt = DateTime.UtcNow;
                    txn.UpdatedAt = DateTime.UtcNow;
                    _db.Transactions.Add(txn);
                    added++;
                }
                else
                {
                    existing.RawName = txn.Name;
                    existing.NormalizedName = normalizedName;
                    existing.Name = txn.Name;
                    existing.Credit = txn.Credit;
                    existing.Debit = txn.Debit;
                    existing.Amount = txn.Amount;
                    existing.Date = txn.Date;
                    existing.Category = txn.Category;
                    existing.UpdatedAt = DateTime.UtcNow;
                    _db.Transactions.Update(existing);
                }
            }
        }
```

### Step 2 — Edit `CashOut/Services/CsvImportService.cs`

Find this block (just before the row-processing loop):

```csharp
        // Pre-load normalization data for batch processing
        var allPatterns = await _db.AliasPatterns
            .Include(p => p.Alias)
            .ToListAsync();

        var rawByNormalized = await _db.RawBusinesses
            .ToDictionaryAsync(b => b.RawNameNormalized);

        // Collect all distinct dates from parsed rows so we can batch-load
```

Replace with:

```csharp
        // Collect all distinct dates from parsed rows so we can batch-load
```

(This removes the two normalization pre-load statements and the blank line
after them, while keeping the "Collect all distinct dates..." comment and
everything after it exactly as-is.)

Next, find this block inside the row loop:

```csharp
            var categoryRaw = GetField(row, categoryIdx);

            var (alias, rawBusiness, normalizedName, effectiveCategory) = await _normalization.ResolveBulk(
                description, categoryRaw, allPatterns, rawByNormalized);

            // ── Additive-only dedup: skip if (date, signed amount, normalizedName) already in DB ──
            if (existingTuples.Contains((date, amount, normalizedName)))
            {
                skippedAlreadyPresent++;
                continue;
            }

            // When an alias matched, use the canonical alias name as the display name.
            // description (raw) is preserved in RawName.
            var displayName = alias != null ? alias.AliasName : description;

            var txn = new Transaction
            {
                TransactionId = $"csv-{Guid.NewGuid()}",
                AccountId = resolvedAccountId,
                Source = TransactionSource.CSV,
                Date = date,
                Name = displayName,
                RawName = description,
                NormalizedName = normalizedName,
                Credit = credit,
                Debit = debit,
                Amount = amount,
                Category = effectiveCategory,
                AliasId = alias?.Id,
                Alias = alias,
                RawBusinessId = rawBusiness?.Id == 0 ? null : rawBusiness?.Id,
                RawBusiness = rawBusiness,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _db.Transactions.Add(txn);
            // Do NOT add to existingTuples — two identical rows in the same CSV both get inserted.
            imported++;
```

Replace with:

```csharp
            var categoryRaw = GetField(row, categoryIdx);
            var normalizedName = MerchantNormalizationService.Normalize(description);

            // ── Additive-only dedup: skip if (date, signed amount, normalizedName) already in DB ──
            if (existingTuples.Contains((date, amount, normalizedName)))
            {
                skippedAlreadyPresent++;
                continue;
            }

            var txn = new Transaction
            {
                TransactionId = $"csv-{Guid.NewGuid()}",
                AccountId = resolvedAccountId,
                Source = TransactionSource.CSV,
                Date = date,
                Name = description,
                RawName = description,
                NormalizedName = normalizedName,
                Credit = credit,
                Debit = debit,
                Amount = amount,
                Category = categoryRaw,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _db.Transactions.Add(txn);
            // Do NOT add to existingTuples — two identical rows in the same CSV both get inserted.
            imported++;
```

Note: `CsvImportService`'s constructor still takes a
`MerchantNormalizationService normalization` parameter and stores it as
`_normalization`, but that field is now unused (we only call the static
`MerchantNormalizationService.Normalize(...)` method, not the injected
instance). **Leave the constructor and field as-is in this phase** — don't
remove them yet. Removing them now would force an unrelated edit to
`CsvImportServiceTests.BuildSvc`, which we want to avoid touching until
Phase 3 (when the whole class gets extracted/deleted anyway). The unused
field will produce no build warning that breaks CI; it's a harmless
placeholder for one phase.

### Step 3 — Verify the build and existing tests

```bash
dotnet build
dotnet test --filter "TestCategory!=UI"
```

Expected: build succeeds. `CsvImportServiceTests` should all still pass
unchanged (none of them assert on `Category`, so switching from
alias-derived category to raw CSV category doesn't affect them).
`MerchantNormalizationServiceTests` should all still pass unchanged (that
file/service wasn't touched). If you have a `TransactionServiceTests.cs`
file not shown here, re-check any test that asserted `AliasId`,
`RawBusinessId`, or alias-derived `Category` on the result of `MergePlaid`
— those assertions will now fail and need updating to expect the raw Plaid
category and no alias/raw-business linkage. Update them to match the new
behavior described in Step 1, don't skip or delete them.

### Step 4 — Wipe transaction data

This is the deliberate destructive step. Do this only when you're ready to
immediately reimport.

1. **Stop the app** (or at least pause any in-flight syncs).
2. **Truncate the transactions table.** For the dev compose stack:

   ```bash
   docker compose -f docker-compose.dev.yml exec db \
     psql -U cashout -d cashout -c "TRUNCATE TABLE transactions;"
   ```

   For production (`docker-compose.yml`), same command against the `db`
   service in that stack. Adjust the service/container name if it differs
   from `db` in your deployment.

3. **Force a full resync for linked (Plaid) accounts.** The existing
   `SyncAll()` / `MergePlaid()` code path already handles a null
   `SyncCursor` as "do a full initial sync" (see the `INVALID_CURSOR`
   handling in `TransactionService.SyncAll`, and Plaid's own semantics for
   an empty cursor). Null out the cursor for every linked account so the
   next sync pulls full history:

   ```bash
   docker compose -f docker-compose.dev.yml exec db \
     psql -U cashout -d cashout -c 'UPDATE linked_accounts SET "SyncCursor" = NULL;'
   ```

4. **Restart the app.**
5. **Trigger sync for linked accounts** via the existing "Sync Transactions"
   button on `/accounts` (calls `POST api/transactions/sync` →
   `TransactionService.SyncAll()`). With `SyncCursor` null, this performs a
   full history sync per account.
6. **Reimport CSV data for manual accounts.** For each manual account with
   prior CSV-only history, go to `/csv-import/{accountId}` and re-upload the
   original file(s). The saved `CsvMappingProfile` for that account is
   untouched by the transaction wipe, so column mapping should already be
   correct — just confirm the preview looks right before importing.

### Step 5 — Verify the reimport

1. Query the row count and a sample to confirm data landed with real
   categories:

   ```bash
   docker compose -f docker-compose.dev.yml exec db \
     psql -U cashout -d cashout -c \
     "SELECT \"Category\", COUNT(*) FROM transactions GROUP BY \"Category\" ORDER BY 2 DESC LIMIT 20;"
   ```

   You should see real Plaid/CSV category values, not `Unassigned` /
   `(uncategorized)` dominating the list (some `(uncategorized)` from CSVs
   with no category column is expected and fine).

2. Open a few `/accounts/{id}` pages (from Phase 1) and confirm the
   Transactions, Cash Flow, and By Category tabs show sensible, real data
   for the reimported period.

3. Confirm `dotnet test --filter "TestCategory!=UI"` still passes after any
   `TransactionServiceTests` updates from Step 3.

## What's now inconsistent (expected, cleaned up in Phase 3)

- `business_aliases`, `alias_patterns`, `raw_businesses`,
  `raw_business_alias_map` tables still exist and still hold old data, but
  nothing writes to or reads from them via the import/sync path anymore.
  `/merchants` page and `BusinessNormalizationController` still work if
  visited directly, but are now disconnected from the live data going
  forward — new imports never create `RawBusiness` rows or match aliases.
- `Transaction.AliasId` / `Transaction.RawBusinessId` are never set on new
  rows (they'll be `null` for everything reimported in this phase).
- This is all intentional and gets removed in Phase 3.

Do not proceed to Phase 3 until you've run the app on real, reimported data
for a while and confirmed the account pages are correct and stable.