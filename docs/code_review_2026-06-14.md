# CashOut — Code Review (June 15, 2026)

Severity legend:
- **🔴 Bug** — causes a runtime failure or data-loss scenario today
- **🟡 Risk** — architectural or performance debt that will hurt at scale or under concurrent load
- **🔵 Improvement** — correctness, consistency, or maintainability issues that don't break anything yet

Each finding includes a self-contained **Proposed Fix** that an agent can apply without needing broader context.

---

## 🔴 Bugs

### 1. `CsvImport.razor` — "Download Skipped Rows CSV" sends a GET to a POST endpoint (carry-over from previous review)

**File:** `CashOut/Pages/CsvImport.razor` (line near `Href="@($"api/csv-import/{AccountId}/skipped-export")"`)
**File:** `CashOut/Controllers/CsvImportController.cs` (`ExportSkipped` action)

The result-step button is an anchor tag (`Href=...`), which browsers always resolve with `GET`. The controller action is `[HttpPost]`, so every click returns `405 Method Not Allowed`.

**Proposed Fix — change the controller action to `[HttpGet]` and encode the skipped-row data as a query-string or session store, OR generate the CSV entirely client-side in Blazor:**

The simplest zero-regression fix is to make the endpoint a `GET` that reconstructs the CSV from a temporary in-memory store keyed by a short token. The cleanest fix that avoids server-side state is to generate the CSV bytes in Blazor and trigger a JS download:

```csharp
// CsvImportController.cs — replace [HttpPost] with [HttpGet] and accept rows via query string
// is impractical for large payloads, so the recommended fix is client-side generation.
```

In `CsvImport.razor`, replace the anchor tag with a Blazor button that calls a JS helper:

```razor
@* Replace the anchor <MudButton> with: *@
<MudButton Variant="Variant.Outlined" Size="Size.Small" Class="mt-2"
           StartIcon="@Icons.Material.Filled.Download"
           OnClick="DownloadSkipped">
    Download Skipped Rows CSV
</MudButton>

@code {
    private async Task DownloadSkipped()
    {
        if (_result == null) return;
        var sb = new System.Text.StringBuilder("Row,RawData,Reason\n");
        foreach (var row in _result.SkippedRows)
            sb.AppendLine($"{row.RowNumber},{EscCsv(row.RawData)},{EscCsv(row.Reason)}");
        var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(sb.ToString()));
        await JS.InvokeVoidAsync("cashoutDownload", "skipped-rows.csv", base64);
    }

    private static string EscCsv(string s) =>
        s.Contains(',') || s.Contains('"') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
}
```

Add to `wwwroot/app.js` (or `plaidLink.js`):

```js
window.cashoutDownload = (filename, base64) => {
    const a = document.createElement('a');
    a.href = 'data:text/csv;base64,' + base64;
    a.download = filename;
    a.click();
};
```

The now-unreachable `ExportSkipped` action on the controller can be deleted.

---

### 2. `CsvImportController` — `IFormFile` binding fails without `[FromForm]` (carry-over from previous review)

**File:** `CashOut/Controllers/CsvImportController.cs`

The `Preview` and `Import` actions accept `IFormFile file` as a parameter but lack `[FromForm]`. Under `[ApiController]` with multipart form data, this causes the parameter to be `null`, returning a `400 Bad Request` with the message "No file uploaded." even when a file is actually sent.

**Proposed Fix:**

```csharp
// In CsvImportController.cs, add [FromForm] to both actions:

[HttpPost("{accountId}/preview")]
public async Task<IActionResult> Preview(
    string accountId,
    [FromForm] IFormFile file,      // ← add [FromForm]
    [FromQuery] int skipTop = 0,
    [FromQuery] int skipBottom = 0)

[HttpPost("{accountId}/import")]
public async Task<IActionResult> Import(string accountId, [FromForm] IFormFile file)  // ← add [FromForm]
```

---

### 3. `BusinessNormalizationService` is registered but `MerchantNormalizationService` is what controllers use

**File:** `CashOut/Controllers/BusinessNormalizationController.cs`, `CashOut/Program.cs`

`BusinessNormalizationController` injects `MerchantNormalizationService` (correct), but the file is named `BusinessNormalizationService.cs` and exports a `BusinessNormalizationService` class that is *not* the same type. The older `BusinessNormalizationService` (with its own `GetOrCreateRawBusiness`, `Resolve`, etc. methods) still exists in `Services/BusinessNormalizationService.cs` and is not registered in DI, so it is dead code. More dangerously, if any future developer calls the wrong service they will get silent, divergent behavior — the two services have different normalization pipelines.

**Proposed Fix:** Delete `CashOut/Services/BusinessNormalizationService.cs` entirely. The functionality is fully superseded by `MerchantNormalizationService`. Run `dotnet build` to confirm nothing references it.

---

### 4. `TransactionService.SyncAll` silently swallows all per-account exceptions

**File:** `CashOut/Services/TransactionService.cs` — `SyncAll` method

```csharp
catch (Exception ex)
{
    Console.Error.WriteLine(
        $"[TransactionService] SyncAll: failed for account {acct.AccountId}: {ex.Message}");
}
```

If `MergePlaid` throws (e.g. a DB constraint violation from a concurrent import), the exception is caught, a line is written to stderr, and the loop continues. The caller (`TransactionsController.Sync`) returns `200 OK` with `{ added: 0, removed: 0 }` even when every account failed. The user sees no error.

**Proposed Fix:** Collect per-account errors and include them in the response:

```csharp
// In TransactionService.cs, change SyncAll return type and accumulate errors:
public async Task<(int added, int removed, List<string> errors)> SyncAll()
{
    var errors = new List<string>();
    // ... existing loop ...
    catch (Exception ex)
    {
        var msg = $"Account {acct.AccountId}: {ex.Message}";
        _logger.LogError(ex, "SyncAll failed for account {AccountId}", acct.AccountId);
        errors.Add(msg);
    }
    return (totalAdded, totalRemoved, errors);
}

// In TransactionsController.cs:
var (added, removed, errors) = await _txns.SyncAll();
if (errors.Count > 0)
    return Ok(new { added, removed, errors });  // still 200 so the UI shows partial results
return Ok(new { added, removed });
```

Also inject `ILogger<TransactionService>` and replace `Console.Error.WriteLine` (see Risk #7 below).

---

### 5. `MerchantNormalizationService.DeleteAlias` — double `SaveChangesAsync` with orphaned FK reference window

**File:** `CashOut/Services/MerchantNormalizationService.cs` — `DeleteAlias` method

The method calls `_db.SaveChangesAsync()` after removing the alias, *then* loops over `affected` transactions setting `txn.AliasId = null`. Between those two saves, the `BusinessAlias` row is gone but the transaction `AliasId` FK still points to it. In PostgreSQL with FK constraints, if another thread reads those transactions between the two saves, EF may attempt to re-load the alias and receive a null navigation, or a concurrent write could hit a FK violation.

**Proposed Fix:** Consolidate to a single save at the end:

```csharp
public async Task<int> DeleteAlias(int aliasId)
{
    var alias = await _db.BusinessAliases
        .Include(a => a.Patterns)
        .FirstOrDefaultAsync(a => a.Id == aliasId)
        ?? throw new KeyNotFoundException($"Alias {aliasId} not found.");

    // 1. Unmap raw businesses
    var rawBusinessMapsToUnmap = await _db.RawBusinessAliasMaps
        .Where(m => m.AliasId == aliasId)
        .Include(m => m.RawBusiness)
        .ToListAsync();

    foreach (var map in rawBusinessMapsToUnmap)
    {
        if (map.RawBusiness != null)
        {
            map.RawBusiness.IsMapped = false;
            map.RawBusiness.UpdatedAt = DateTime.UtcNow;
        }
    }
    _db.RawBusinessAliasMaps.RemoveRange(rawBusinessMapsToUnmap);

    // 2. Null-out transaction FK references BEFORE deleting the alias
    var affected = await _db.Transactions
        .Where(t => t.AliasId == aliasId)
        .ToListAsync();

    foreach (var txn in affected)
    {
        txn.AliasId = null;
        txn.Alias = null;
        txn.Name = txn.RawName;
        txn.UpdatedAt = DateTime.UtcNow;
    }

    // 3. Now remove the alias (FK references are already cleared)
    _db.BusinessAliases.Remove(alias);

    // 4. Single save
    await _db.SaveChangesAsync();

    // 5. Reprocess and cleanup (these call SaveChangesAsync internally)
    await ReprocessUnaliasedTransactions(affected);
    await _db.SaveChangesAsync();
    await CleanupRawBusinesses();
    await _db.SaveChangesAsync();
    return affected.Count;
}
```

---

## 🟡 Risks

### 6. `ReportService` loads all expense transactions into memory (carry-over from previous review)

**File:** `CashOut/Services/ReportService.cs` — `GetExpenses`

```csharp
private async Task<List<Transaction>> GetExpenses(int year)
{
    return await _db.Transactions
        .Where(t => t.Date.Year == year && t.Amount > 0)
        .ToListAsync();
}
```

Every report endpoint (monthly, category, pivot, merchants, largest) calls this and then groups/sums in LINQ-to-Objects. At a few hundred transactions this is fine; at tens of thousands it causes unnecessary memory pressure and latency.

**Proposed Fix — push aggregations to the database for the two highest-traffic endpoints:**

```csharp
// Replace GetMonthly to aggregate in SQL:
public async Task<List<MonthlyRow>> GetMonthly(int? year = null)
{
    var y = year ?? await _settings.GetOutputYear();
    return await _db.Transactions
        .Where(t => t.Date.Year == y && t.Amount > 0)
        .GroupBy(t => new { t.Date.Year, t.Date.Month })
        .Select(g => new MonthlyRow(
            Month: g.Key.Year + "-" + (g.Key.Month < 10 ? "0" : "") + g.Key.Month,
            Label: "",   // computed client-side or via a second pass
            Total: g.Sum(t => t.Amount),
            Count: g.Count()))
        .OrderBy(r => r.Month)
        .ToListAsync();
}
```

Note: `DateOnly` grouping on `Year`/`Month` properties is supported by Npgsql's EF provider. The `Label` field can be derived from `Month` in a post-query projection to avoid the SQL date-formatting complexity.

---

### 7. `Console.Error.WriteLine` / `Console.WriteLine` bypass structured logging (carry-over from previous review)

**Files:** `CashOut/Services/TransactionService.cs`, `CashOut/Services/PlaidService.cs`

Both services write directly to stderr, bypassing `ILogger`. This makes it impossible to route logs to external sinks (Seq, Loki, CloudWatch) or filter by level.

**Proposed Fix:** Inject `ILogger<T>` into each service and replace all `Console.*` calls:

```csharp
// TransactionService.cs — add to constructor:
private readonly ILogger<TransactionService> _logger;

public TransactionService(
    AppDbContext db, PlaidService plaid, SettingsService settings,
    MerchantNormalizationService normalization,
    ILogger<TransactionService> logger)
{
    // ...
    _logger = logger;
}

// Replace Console.Error.WriteLine with:
_logger.LogError(ex, "SyncAll failed for account {AccountId}", acct.AccountId);
_logger.LogWarning("INVALID_CURSOR for account {AccountId} — resetting", acct.AccountId);

// PlaidService.cs — same pattern:
private readonly ILogger<PlaidService> _logger;
// Replace Console.Error.WriteLine with:
_logger.LogWarning(ex, "RemoveItem: Plaid revocation failed for item {ItemId}, deleting locally", itemId);
```

No DI registration change needed — `ILogger<T>` is available automatically via `builder.Services.AddLogging()` which is included in `WebApplication.CreateBuilder`.

---

### 8. Race condition in `MerchantNormalizationService.EnsureRawBusiness` (carry-over from previous review)

**File:** `CashOut/Services/MerchantNormalizationService.cs` — `EnsureRawBusiness`

```csharp
var existing = await _db.RawBusinesses
    .FirstOrDefaultAsync(b => b.RawNameNormalized == normalized);
if (existing != null) return existing;
// ... Add new RawBusiness ...
```

Two concurrent imports of the same merchant name both see `null` and both attempt to insert, hitting the unique index on `RawNameNormalized` and throwing a `DbUpdateException` with a Postgres unique-constraint violation.

**Proposed Fix:** Wrap the insert in a try-catch and re-fetch on conflict:

```csharp
private async Task<RawBusiness> EnsureRawBusiness(
    string rawName, string normalized, string categoryRaw)
{
    var existing = await _db.RawBusinesses
        .FirstOrDefaultAsync(b => b.RawNameNormalized == normalized);
    if (existing != null) return existing;

    var raw = new RawBusiness
    {
        RawName = rawName,
        RawNameNormalized = normalized,
        CategoryRaw = categoryRaw,
        IsMapped = false,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
    _db.RawBusinesses.Add(raw);

    try
    {
        await _db.SaveChangesAsync();
    }
    catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
        when (ex.InnerException?.Message.Contains("unique") == true ||
              ex.InnerException?.Message.Contains("IX_raw_businesses_RawNameNormalized") == true)
    {
        // Another thread won the race — detach and re-fetch
        _db.Entry(raw).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
        return await _db.RawBusinesses
            .FirstAsync(b => b.RawNameNormalized == normalized);
    }

    return raw;
}
```

---

### 9. `EncryptionService` is registered as `AddSingleton` but holds a mutable key byte array

**File:** `CashOut/Program.cs`

```csharp
builder.Services.AddSingleton<EncryptionService>();
```

`EncryptionService` stores `_key` as a `byte[]`. The key bytes themselves are not mutated after construction, so this is safe *today*, but `byte[]` is a reference type — if a future developer accidentally exposes or modifies `_key` (e.g. via `Array.Clear` in a key-rotation attempt), all in-flight requests sharing the singleton will break or silently use zeroed keys.

**Proposed Fix:** Change `_key` to `ReadOnlyMemory<byte>` to make the immutability contract explicit, or copy the key into the `AesGcm` constructor each time `Encrypt`/`Decrypt` is called (negligible cost for a singleton):

```csharp
// EncryptionService.cs
private readonly byte[] _key;  // already private — also mark the field with a comment

// Defensive: copy the key so the caller's array can be cleared independently
public EncryptionService(IConfiguration config)
{
    var raw = config["ENCRYPTION_KEY"]
        ?? Environment.GetEnvironmentVariable("ENCRYPTION_KEY")
        ?? throw new InvalidOperationException("ENCRYPTION_KEY environment variable is required.");

    var decoded = Convert.FromBase64String(raw);
    if (decoded.Length != 32)
        throw new InvalidOperationException("ENCRYPTION_KEY must be a base64-encoded 32-byte value.");

    _key = (byte[])decoded.Clone();  // own copy, isolated from caller
}
```

---

### 10. `TransactionService.FetchAll` ignores per-account errors and loses the year context

**File:** `CashOut/Services/TransactionService.cs` — `FetchAll`

```csharp
foreach (var acct in accounts)
{
    var txns = await _plaid.FetchTransactions(acct.AccessToken, year);
    all.AddRange(txns);
}
```

If `FetchTransactions` throws for one account (e.g. an expired access token), the exception propagates out of `FetchAll` and no transactions are merged — not even those from accounts that were fetched successfully before the failure. Unlike `SyncAll`, there is no per-account try-catch.

**Proposed Fix:** Add per-account error isolation, matching `SyncAll`'s pattern:

```csharp
public async Task<int> FetchAll()
{
    var year = await _settings.GetOutputYear();
    var accounts = await _db.LinkedAccounts.ToListAsync();
    var all = new List<Transaction>();

    foreach (var acct in accounts)
    {
        try
        {
            var txns = await _plaid.FetchTransactions(acct.AccessToken, year);
            all.AddRange(txns);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FetchAll failed for account {AccountId}", acct.AccountId);
        }
    }

    await MergePlaid(all, new List<string>());
    return all.Count;
}
```

---

### 11. `ReportService.GetCategorySummary` — `Where` clause on an EF-translated subquery with non-standard date comparison

**File:** `CashOut/Services/ReportService.cs` — `GetCategorySummary`

```csharp
MonthDebit = g.Where(t => t.Date.Year == year && t.Date.Month == month).Sum(t => t.Debit ?? 0),
MonthCredit = g.Where(t => t.Date.Year == year && t.Date.Month == month).Sum(t => t.Credit ?? 0)
```

These sub-filters inside a `GroupBy` projection are translated to SQL `FILTER (WHERE ...)` aggregate clauses by Npgsql. While this works in PostgreSQL, it is a non-standard EF pattern that fails silently on `InMemoryDatabase` (used by tests) — the filters are ignored in-memory, causing `GetCategorySummary` tests to return wrong results if ever added. It also makes the query plan significantly more complex than necessary.

**Proposed Fix:** Split into two queries — one for the 12-month aggregate and one for the single month:

```csharp
public async Task<List<CategorySummaryRow>> GetCategorySummary(int year, int month)
{
    var targetDate = new DateOnly(year, month, 1);
    var startDate = targetDate.AddMonths(-11);
    var endDate = targetDate.AddMonths(1).AddDays(-1);

    var twelveMonth = await _db.Transactions
        .Where(t => t.Date >= startDate && t.Date <= endDate)
        .GroupBy(t => string.IsNullOrEmpty(t.Category) ? "(uncategorized)" : t.Category)
        .Select(g => new { Category = g.Key,
            TwelveDebit = g.Sum(t => t.Debit ?? 0),
            TwelveCredit = g.Sum(t => t.Credit ?? 0) })
        .ToListAsync();

    var thisMonth = await _db.Transactions
        .Where(t => t.Date.Year == year && t.Date.Month == month)
        .GroupBy(t => string.IsNullOrEmpty(t.Category) ? "(uncategorized)" : t.Category)
        .Select(g => new { Category = g.Key,
            MonthDebit = g.Sum(t => t.Debit ?? 0),
            MonthCredit = g.Sum(t => t.Credit ?? 0) })
        .ToListAsync();

    var monthDict = thisMonth.ToDictionary(x => x.Category);

    return twelveMonth.Select(s =>
    {
        var m = monthDict.GetValueOrDefault(s.Category);
        return new CategorySummaryRow(
            s.Category,
            m?.MonthDebit ?? 0,
            m?.MonthCredit ?? 0,
            (m?.MonthCredit ?? 0) - (m?.MonthDebit ?? 0),
            (s.TwelveCredit - s.TwelveDebit) / 12m,
            s.TwelveDebit / 12m,
            s.TwelveCredit / 12m);
    })
    .OrderByDescending(r => Math.Abs(r.MonthNet))
    .ToList();
}
```

---

## 🔵 Improvements

### 12. Invalid HTML nesting in `ManualAccounts.razor` (carry-over from previous review)

**File:** `CashOut/Pages/ManualAccounts.razor`

The "Import CSV" button uses a `MudButton` with `Href` set, which MudBlazor renders as an `<a>` wrapping an inner element. The review from June 13 flagged this as a `<button>` inside an `<a>`. Looking at the current file, this is already using `MudButton` with `Href` — which MudBlazor renders correctly as an `<a>` tag with button styling. No action needed on this specific point; the concern from the prior review no longer applies.

---

### 13. `Transactions.razor` — `LoadAllCategories` fetches all transactions for the year to build the category list

**File:** `CashOut/Pages/Transactions.razor` — `LoadAllCategories`

```csharp
var all = await Http.GetFromJsonAsync<List<TransactionDto>>(
    $"api/transactions?year={_filterYear}") ?? new();
_categories = all.Select(t => t.Category)...
```

This deserializes every transaction for the year into the Blazor circuit just to extract distinct category strings. For a year with 5,000 transactions at ~200 bytes each, that's ~1 MB over the SignalR connection on every year change.

**Proposed Fix:** Add a dedicated `GET /api/transactions/categories?year={year}` endpoint:

```csharp
// In TransactionsController.cs:
[HttpGet("categories")]
public async Task<IActionResult> Categories([FromQuery] int? year)
{
    var q = _db.Transactions.AsQueryable();
    if (year.HasValue) q = q.Where(t => t.Date.Year == year.Value);
    var cats = await q
        .Where(t => t.Category != "")
        .Select(t => t.Category)
        .Distinct()
        .OrderBy(c => c)
        .ToListAsync();
    return Ok(cats);
}
```

In `Transactions.razor`, replace `LoadAllCategories` to call this endpoint instead.

---

### 14. `PlaidService.RemoveItem` uses encrypted token as a fallback group-delete key

**File:** `CashOut/Services/PlaidService.cs` — `RemoveItem`

```csharp
IQueryable<LinkedAccount> toRemove = string.IsNullOrEmpty(itemId)
    ? _db.LinkedAccounts.Where(a => a.AccessToken == encryptedAccessToken)
    : _db.LinkedAccounts.Where(a => a.ItemId == itemId);
```

The fallback path (when `itemId` is empty) matches accounts by their *encrypted* access token string. Because `EncryptionService.Encrypt` uses a random nonce, the same plaintext encrypts to a different ciphertext on every call. This means the fallback comparison will find zero matches — any account whose `AccessToken` column was re-encrypted (e.g. after a token refresh) will not be deleted.

This is partially mitigated by the fact that `ItemId` is populated for all accounts created since the `fixings` migration. But accounts that pre-date that migration may have an empty `ItemId`, making the fallback path silently fail.

**Proposed Fix:** The fallback should decrypt-and-compare or use a separate plaintext identifier. The simplest safe fix is to log a warning when `itemId` is empty rather than attempting the fragile encrypted-token match:

```csharp
IQueryable<LinkedAccount> toRemove;
if (string.IsNullOrEmpty(itemId))
{
    _logger.LogWarning(
        "RemoveItem called without ItemId — cannot group-delete safely. " +
        "Account may not be removed from DB.");
    // Last-resort: try to find by account ID decoded from the token response
    // For now, remove nothing rather than silently matching nothing:
    return;
}
else
{
    toRemove = _db.LinkedAccounts.Where(a => a.ItemId == itemId);
}
```

Or, if the legacy fallback must be retained, decrypt first:

```csharp
if (string.IsNullOrEmpty(itemId))
{
    // Decrypt to get the plaintext token, then re-encrypt each DB row to compare — too expensive.
    // Better: store plaintext item_id at link time (already done for new accounts).
    // For legacy rows, remove by the single account whose AccessToken decrypts to this token:
    var plain = _encryption.Decrypt(encryptedAccessToken);
    var all = await _db.LinkedAccounts.ToListAsync();
    toRemove = all.Where(a => {
        try { return _encryption.Decrypt(a.AccessToken) == plain; }
        catch { return false; }
    }).AsQueryable();
}
```

---

### 15. `AppDbContext` configures `OnDelete` cascade for `RawBusinessAliasMap` → `RawBusiness` but `RawBusiness` → `Transaction` has no cascade

**File:** `CashOut/Data/AppDbContext.cs`

`RawBusinessAliasMap` uses `OnDelete(DeleteBehavior.Cascade)` for its FK to `RawBusiness`. However, when a `RawBusiness` is deleted (via `CleanupRawBusinesses`), EF must also null-out `Transaction.RawBusinessId` — otherwise the delete fails with a FK violation. The current code calls `CleanupRawBusinesses` after confirming no transactions reference the raw business (via the `referencedRawIds` query), so this works today. But the FK on `Transaction.RawBusinessId` is configured with no explicit delete behavior:

```csharp
e.HasOne(x => x.RawBusiness).WithMany().HasForeignKey(x => x.RawBusinessId);
```

EF defaults this to `ClientSetNull`, meaning EF will set tracked entities to null — but only for entities currently in the change tracker. If a `RawBusiness` is deleted outside of EF (e.g. a direct SQL DELETE during a migration), orphaned `RawBusinessId` references in `transactions` will violate the FK in PostgreSQL.

**Proposed Fix:** Explicitly configure the behavior and add a DB-level `ON DELETE SET NULL`:

```csharp
// In AppDbContext.cs, Transaction configuration:
e.HasOne(x => x.RawBusiness)
 .WithMany()
 .HasForeignKey(x => x.RawBusinessId)
 .OnDelete(DeleteBehavior.SetNull);  // generates ON DELETE SET NULL in migration
```

Then create a new migration: `dotnet ef migrations add SetNullOnRawBusinessDelete`.

---

### 16. `Transactions.razor` `OnActiveMonthChanged` has an equality guard but the initial render can still double-load

**File:** `CashOut/Pages/Transactions.razor`

```csharp
private async Task OnActiveMonthChanged(int newIndex)
{
    var newMonth = newIndex + 1;
    if (_filterMonth == newMonth) return;
    _filterMonth = newMonth;
    await LoadTransactions();
    await LoadSummary();
}
```

`OnInitializedAsync` sets `_activeMonthTabIndex = _filterMonth - 1` and then calls `LoadTransactions()`. MudBlazor's `<MudTabs>` fires `ActivePanelIndexChanged` when `ActivePanelIndex` is first bound, which triggers `OnActiveMonthChanged` with the same index as the one set in `OnInitializedAsync`. The guard `if (_filterMonth == newMonth) return` catches this only if `_filterMonth` has already been set — which it has, since `OnInitializedAsync` sets it before the tabs render. So the guard does work, but it relies on initialization order that is implicit.

**Proposed Fix:** Make the initialization order explicit by initializing `_filterMonth` before `_activeMonthTabIndex`:

```csharp
protected override async Task OnInitializedAsync()
{
    _filterMonth = DateTime.Now.Month;       // set first
    _activeMonthTabIndex = _filterMonth - 1; // derived from it
    await LoadYears();
    await LoadTransactions();
    await LoadSummary();
}
```

This is already how the code is written, so the guard works — but add a comment explaining the dependency:

```csharp
// _filterMonth must be set before _activeMonthTabIndex so that the MudTabs
// ActivePanelIndexChanged callback (which fires on first bind) is a no-op.
```

---

### 17. `CsvImportService.Import` accumulates `RawBusiness` entities via `ResolveBulk` but does not save them until the end — zero-ID FK references

**File:** `CashOut/Services/CsvImportService.cs` — `Import`

```csharp
var (alias, rawBusiness, normalizedName, effectiveCategory) = await _normalization.ResolveBulk(
    description, categoryRaw, allPatterns, rawByNormalized);
// ...
txn.RawBusinessId = rawBusiness?.Id == 0 ? null : rawBusiness?.Id,
```

`ResolveBulk` adds new `RawBusiness` entities to the EF change tracker without saving. Their `Id` is `0` (the EF default before the database assigns the identity value). The code checks `rawBusiness?.Id == 0` and sets `RawBusinessId = null` to avoid storing `0`. This means the transaction is saved with `RawBusinessId = null`, but the `RawBusiness` *is* saved (as a side effect of `_db.SaveChangesAsync()` at the end of `Import`). So after save, the raw business has a real DB Id, but no transaction links to it — the FK link is silently lost.

This means the "Unmapped" tab will show merchants that never surface in transaction results, and `CleanupRawBusinesses` will delete them on the next retroactive-map run.

**Proposed Fix:** After `SaveChangesAsync`, update transaction `RawBusinessId` for those that were set to null because of the zero-Id:

The cleaner fix is to save in two passes — first save all new `RawBusiness` entities, then save transactions with correct IDs:

```csharp
// After the row loop and before _db.SaveChangesAsync():
// 1. Save just the new RawBusinesses so they get their DB-assigned IDs
await _db.SaveChangesAsync();  // only raw businesses and alias maps pending here

// 2. Now fix up any transaction that has a non-null RawBusiness reference but Id==0
foreach (var txn in _db.Transactions.Local.Where(
    t => t.RawBusiness != null && t.RawBusinessId == null))
{
    txn.RawBusinessId = txn.RawBusiness!.Id;
}

await _db.SaveChangesAsync();  // save transactions with correct RawBusinessId
```

Or alternatively, change the null-guard to assign after save:

```csharp
// Store the rawBusiness reference on the transaction object regardless:
txn.RawBusiness = rawBusiness;
// Set RawBusinessId only after SaveChangesAsync assigns real IDs:
```

Then after `await _db.SaveChangesAsync()` at the end of the loop, do a second pass to set `RawBusinessId`.

---

### 18. `DebugController` exposes credential metadata in production if middleware order is wrong

**File:** `CashOut/Controllers/DebugController.cs`, `CashOut/Program.cs`

The middleware gate in `Program.cs` only runs in non-Development environments:

```csharp
if (!app.Environment.IsDevelopment())
{
    app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/api/debug"))
        {
            context.Response.StatusCode = 404;
            return;
        }
        await next();
    });
}
```

This works for the standard `ASPNETCORE_ENVIRONMENT=Production` deployment. However, the Docker Compose dev file sets `ASPNETCORE_ENVIRONMENT=Development`, which means the debug endpoint is open *inside the dev container*. If the dev container is accidentally exposed (e.g. bound to `0.0.0.0:8080` on a VPS), the `/api/debug/env` endpoint returns masked-but-length-revealing Plaid credential metadata to anyone on the internet.

**Proposed Fix:** Restrict the debug controller to `localhost` origins unconditionally, rather than relying on environment name:

```csharp
// Replace the environment-based gate with a host-based gate that always applies:
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/debug") &&
        !context.Connection.LocalIpAddress?.IsLoopback() == true &&
        context.Request.Host.Host != "localhost")
    {
        context.Response.StatusCode = 404;
        return;
    }
    await next();
});
```

Or simpler: move the debug endpoint check to a policy attribute, or delete the controller entirely and rely on the `appsettings.json` `Logging` section for diagnostics.

---

## Summary Table

| # | Severity | File(s) | Issue | Effort |
|---|----------|---------|-------|--------|
| 1 | 🔴 Bug | `CsvImport.razor`, `CsvImportController.cs` | Skipped-rows export GET→POST 405 | Small |
| 2 | 🔴 Bug | `CsvImportController.cs` | Missing `[FromForm]` on `IFormFile` params | Trivial |
| 3 | 🔴 Bug | `BusinessNormalizationService.cs` | Dead service class, risk of wrong service use | Trivial (delete file) |
| 4 | 🔴 Bug | `TransactionService.cs` | Silent swallow of sync errors returns 200 OK | Small |
| 5 | 🔴 Bug | `MerchantNormalizationService.cs` | `DeleteAlias` double-save with FK inconsistency window | Small |
| 6 | 🟡 Risk | `ReportService.cs` | Full year load into memory for all report endpoints | Medium |
| 7 | 🟡 Risk | `TransactionService.cs`, `PlaidService.cs` | `Console.*` bypasses `ILogger` | Small |
| 8 | 🟡 Risk | `MerchantNormalizationService.cs` | `EnsureRawBusiness` race condition on concurrent import | Small |
| 9 | 🟡 Risk | `EncryptionService.cs`, `Program.cs` | Singleton key byte array not defensively copied | Trivial |
| 10 | 🟡 Risk | `TransactionService.cs` | `FetchAll` has no per-account error isolation | Small |
| 11 | 🟡 Risk | `ReportService.cs` | `GetCategorySummary` sub-filter in GroupBy breaks InMemoryDB tests | Small |
| 12 | 🔵 Impr. | `ManualAccounts.razor` | HTML nesting — already resolved in current code | None |
| 13 | 🔵 Impr. | `Transactions.razor` | Category list fetch loads all transactions | Small |
| 14 | 🔵 Impr. | `PlaidService.cs` | Encrypted-token fallback in `RemoveItem` always matches zero rows | Small |
| 15 | 🔵 Impr. | `AppDbContext.cs` | No `ON DELETE SET NULL` for `Transaction.RawBusinessId` FK | Trivial + migration |
| 16 | 🔵 Impr. | `Transactions.razor` | Double-load guard relies on implicit init order | Trivial (comment) |
| 17 | 🔵 Impr. | `CsvImportService.cs` | Zero-ID `RawBusinessId` FK references lost on import | Medium |
| 18 | 🔵 Impr. | `DebugController.cs`, `Program.cs` | Debug endpoint exposed inside dev Docker container | Small |

---

## Previously Resolved (confirmed closed)

| Issue | Status |
|-------|--------|
| N+1 Queries in MergePlaid | ✅ Fixed — batch loading implemented |
| Transaction Sign Inconsistency | ✅ Fixed — `NormalizeSingleAmount` / `NormalizeSplitColumns` |
| Merchants page deserialization | ✅ Fixed — DTOs updated |
| Alias category edits lost | ✅ Fixed — PATCH endpoints added |
| Category filter reset on year change | ✅ Fixed |
| Invalid HTML nesting in `ManualAccounts.razor` | ✅ Already using `MudButton Href=...` correctly |