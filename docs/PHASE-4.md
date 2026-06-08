# Phase 4 — Sync, Fetch, Transactions API & CSV Export

## Progress Tracker

- [ ] 4.1 Add `SyncTransactions` to `PlaidService`
- [ ] 4.2 Add `FetchTransactions` to `PlaidService`
- [ ] 4.3 Create `TransactionService` (upsert/merge logic)
- [ ] 4.4 Create `TransactionsController`
- [ ] 4.5 Register `TransactionService` in `Program.cs`
- [ ] 4.6 Verify sync and fetch with curl

---

## Context

Plaid provides two ways to get transactions:

**Sync** (`/transactions/sync`) — cursor-based incremental updates. Returns `added`, `modified`,
and `removed` arrays since the last cursor. Loop while `has_more` is true, updating the cursor
each iteration. Persist the final `next_cursor` on the `LinkedAccount` row after all pages are
consumed. This is the preferred method for keeping data up-to-date.

**Fetch** (`/transactions/get`) — date-range pull. Returns all transactions for a given date range
(full calendar year). Used to backfill or re-sync a specific year. Does not update the sync cursor.

Both paths feed into the same `TransactionService.Merge()` method which upserts records and
handles removals.

### Amount Convention
Plaid returns positive amounts for expenses (money leaving the account) and negative for income or
refunds. Store amounts as-is. The reports layer filters to `amount > 0` for expense reports.

---

## Task 4.1 — Add `SyncTransactions` to PlaidService

Add to `Services/PlaidService.cs`:

```csharp
/// <summary>
/// Runs a full cursor-based sync for one account's access token.
/// Loops until has_more is false. Returns all added/modified transactions,
/// all removed transaction IDs, and the new cursor to persist.
/// </summary>
public async Task<(List<Transaction> added, List<string> removedIds, string nextCursor)>
    SyncTransactions(string encryptedAccessToken, string? currentCursor)
{
    var plainToken = _encryption.Decrypt(encryptedAccessToken);
    var added = new List<Transaction>();
    var removedIds = new List<string>();
    var cursor = currentCursor ?? "";
    bool hasMore = true;

    while (hasMore)
    {
        var json = await Post("/transactions/sync", new
        {
            client_id = ClientId,
            secret = await Secret(),
            access_token = plainToken,
            cursor = cursor,
            options = new { include_personal_finance_category = true }
        });

        foreach (var t in json.GetProperty("added").EnumerateArray())
            added.Add(MapTransaction(t));

        foreach (var t in json.GetProperty("modified").EnumerateArray())
            added.Add(MapTransaction(t));  // modified are treated as upserts

        foreach (var t in json.GetProperty("removed").EnumerateArray())
            removedIds.Add(t.GetProperty("transaction_id").GetString()!);

        hasMore = json.GetProperty("has_more").GetBoolean();
        cursor = json.GetProperty("next_cursor").GetString()!;
    }

    return (added, removedIds, cursor);
}
```

---

## Task 4.2 — Add `FetchTransactions` to PlaidService

Add to `Services/PlaidService.cs`:

```csharp
/// <summary>
/// Fetches all transactions for a calendar year using /transactions/get.
/// Does not update sync cursor.
/// </summary>
public async Task<List<Transaction>> FetchTransactions(
    string encryptedAccessToken, int year)
{
    var plainToken = _encryption.Decrypt(encryptedAccessToken);

    var json = await Post("/transactions/get", new
    {
        client_id = ClientId,
        secret = await Secret(),
        access_token = plainToken,
        start_date = $"{year}-01-01",
        end_date = $"{year}-12-31",
        options = new { include_personal_finance_category = true }
    });

    return json.GetProperty("transactions")
        .EnumerateArray()
        .Select(MapTransaction)
        .ToList();
}
```

Also add the `MapTransaction` private helper to `PlaidService.cs` (used by both sync and fetch):

```csharp
private static Transaction MapTransaction(JsonElement t) => new()
{
    TransactionId = t.GetProperty("transaction_id").GetString()!,
    AccountId = t.GetProperty("account_id").GetString()!,
    Date = DateOnly.Parse(t.GetProperty("date").GetString()!),
    Name = t.GetProperty("name").GetString()!,
    Amount = t.GetProperty("amount").GetDecimal(),
    Category = t.TryGetProperty("personal_finance_category", out var pfc)
               && pfc.ValueKind == JsonValueKind.Object
               ? pfc.GetProperty("primary").GetString() ?? ""
               : t.TryGetProperty("category", out var cat)
               && cat.ValueKind == JsonValueKind.Array
               ? string.Join(" > ", cat.EnumerateArray().Select(x => x.GetString()))
               : "",
    CreatedAt = DateTime.UtcNow,
    UpdatedAt = DateTime.UtcNow
};
```

---

## Task 4.3 — TransactionService

Handles DB upsert/merge logic, CSV generation, and querying. Keeps this logic out of controllers.

Create `Spening/Services/TransactionService.cs`:

```csharp
using System.Globalization;
using System.Text;

public class TransactionService
{
    private readonly AppDbContext _db;
    private readonly PlaidService _plaid;
    private readonly SettingsService _settings;

    public TransactionService(AppDbContext db, PlaidService plaid, SettingsService settings)
    {
        _db = db;
        _plaid = plaid;
        _settings = settings;
    }

    // ── Sync ──────────────────────────────────────────────────────────────

    /// <summary>Runs incremental sync across all linked accounts.</summary>
    public async Task<(int added, int removed)> SyncAll()
    {
        var accounts = await _db.LinkedAccounts.ToListAsync();
        int totalAdded = 0, totalRemoved = 0;

        foreach (var acct in accounts)
        {
            var (newTxns, removedIds, nextCursor) =
                await _plaid.SyncTransactions(acct.AccessToken, acct.SyncCursor);

            var (a, r) = await Merge(newTxns, removedIds);
            totalAdded += a;
            totalRemoved += r;

            acct.SyncCursor = nextCursor;
            _db.LinkedAccounts.Update(acct);
        }

        await _db.SaveChangesAsync();
        return (totalAdded, totalRemoved);
    }

    // ── Fetch ─────────────────────────────────────────────────────────────

    /// <summary>Full re-fetch of the configured year across all accounts.</summary>
    public async Task<int> FetchAll()
    {
        var year = await _settings.GetOutputYear();
        var accounts = await _db.LinkedAccounts.ToListAsync();
        var all = new List<Transaction>();

        foreach (var acct in accounts)
        {
            var txns = await _plaid.FetchTransactions(acct.AccessToken, year);
            all.AddRange(txns);
        }

        var (_, added, _) = await MergeWithTotal(all, new List<string>());
        return added;
    }

    // ── Merge ─────────────────────────────────────────────────────────────

    private async Task<(int added, int removed)> Merge(
        List<Transaction> incoming, List<string> removedIds)
    {
        var (_, added, removed) = await MergeWithTotal(incoming, removedIds);
        return (added, removed);
    }

    private async Task<(int total, int added, int removed)> MergeWithTotal(
        List<Transaction> incoming, List<string> removedIds)
    {
        // Remove deleted transactions
        if (removedIds.Count > 0)
        {
            var toDelete = await _db.Transactions
                .Where(t => removedIds.Contains(t.TransactionId))
                .ToListAsync();
            _db.Transactions.RemoveRange(toDelete);
        }

        int added = 0;
        foreach (var txn in incoming)
        {
            var existing = await _db.Transactions.FindAsync(txn.TransactionId);
            if (existing == null)
            {
                txn.CreatedAt = DateTime.UtcNow;
                txn.UpdatedAt = DateTime.UtcNow;
                _db.Transactions.Add(txn);
                added++;
            }
            else
            {
                existing.Name = txn.Name;
                existing.Amount = txn.Amount;
                existing.Category = txn.Category;
                existing.Date = txn.Date;
                existing.UpdatedAt = DateTime.UtcNow;
                _db.Transactions.Update(existing);
            }
        }

        await _db.SaveChangesAsync();

        var total = await _db.Transactions.CountAsync();
        return (total, added, removedIds.Count);
    }

    // ── Query ─────────────────────────────────────────────────────────────

    public async Task<List<Transaction>> Query(
        int? year = null, string? accountId = null, string? category = null)
    {
        var q = _db.Transactions.AsQueryable();

        if (year.HasValue)
            q = q.Where(t => t.Date.Year == year.Value);

        if (!string.IsNullOrEmpty(accountId))
            q = q.Where(t => t.AccountId == accountId);

        if (!string.IsNullOrEmpty(category))
            q = q.Where(t => t.Category == category);

        return await q.OrderByDescending(t => t.Date).ToListAsync();
    }

    // ── CSV Export ────────────────────────────────────────────────────────

    public async Task<byte[]> ExportCsv(int? year = null)
    {
        var transactions = await Query(year);
        var sb = new StringBuilder();
        sb.AppendLine("Date,Name,Amount,Category,TransactionId,AccountId");

        foreach (var t in transactions)
        {
            sb.AppendLine(
                $"{t.Date}," +
                $"{EscapeCsv(t.Name)}," +
                $"{t.Amount.ToString(CultureInfo.InvariantCulture)}," +
                $"{EscapeCsv(t.Category)}," +
                $"{t.TransactionId}," +
                $"{t.AccountId}");
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string EscapeCsv(string s) =>
        s.Contains(',') || s.Contains('"') || s.Contains('\n')
            ? $"\"{s.Replace("\"", "\"\"")}\""
            : s;
}
```

---

## Task 4.4 — TransactionsController

Create `Spening/Controllers/TransactionsController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/transactions")]
public class TransactionsController : ControllerBase
{
    private readonly TransactionService _txns;
    private readonly SettingsService _settings;

    public TransactionsController(TransactionService txns, SettingsService settings)
    {
        _txns = txns;
        _settings = settings;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int? year,
        [FromQuery] string? accountId,
        [FromQuery] string? category)
    {
        var results = await _txns.Query(year, accountId, category);
        return Ok(results);
    }

    [HttpPost("sync")]
    public async Task<IActionResult> Sync()
    {
        var (added, removed) = await _txns.SyncAll();
        return Ok(new { added, removed });
    }

    [HttpPost("fetch")]
    public async Task<IActionResult> Fetch()
    {
        var count = await _txns.FetchAll();
        return Ok(new { written = count });
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] int? year)
    {
        var resolvedYear = year ?? await _settings.GetOutputYear();
        var csv = await _txns.ExportCsv(resolvedYear);
        return File(csv, "text/csv", $"spening-{resolvedYear}.csv");
    }
}
```

---

## Task 4.5 — Register TransactionService in Program.cs

Add to the services section in `Program.cs`:

```csharp
builder.Services.AddScoped<TransactionService>();
```

---

## Task 4.6 — Verification

With at least one linked account from Phase 3:

```bash
# Incremental sync
curl -X POST http://localhost:8080/api/transactions/sync
# Expected: {"added": N, "removed": 0}

# Full fetch for current year
curl -X POST http://localhost:8080/api/transactions/fetch
# Expected: {"written": N}

# Query all transactions
curl http://localhost:8080/api/transactions
# Expected: JSON array of transaction objects

# Filter by year
curl "http://localhost:8080/api/transactions?year=2024"

# Download CSV
curl http://localhost:8080/api/transactions/export -o test.csv
# Expected: CSV file with header row + transaction rows
```

Also verify in the DB:
```sql
SELECT COUNT(*), MIN(date), MAX(date) FROM transactions;
SELECT sync_cursor IS NOT NULL FROM linked_accounts;
```

---

## Proceed to Phase 5

Continue with [PHASE-5.md](./PHASE-5.md).
