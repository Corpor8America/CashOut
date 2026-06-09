# Spening — Code Review

## Summary

Overall the codebase is clean, well-structured, and follows the plan faithfully. The issues below
range from bugs that will cause runtime failures to minor improvements. Severity levels:
**🔴 Bug** (will break), **🟡 Risk** (may break under certain conditions), **🔵 Improvement** (quality/maintainability).

---

## 🔴 Bugs

### 1. `PlaidService` Double-Registration in `Program.cs`

**File:** `Spening/Program.cs`

```csharp
builder.Services.AddHttpClient<PlaidService>();
builder.Services.AddScoped<PlaidService>(); // ← this overrides the typed client
```

`AddHttpClient<T>()` registers `PlaidService` as a typed `HttpClient` consumer. The subsequent
`AddScoped<PlaidService>()` replaces that registration with a plain scoped service, so the
`HttpClient` injected into `PlaidService` will be a raw transient instance without socket
pooling — not the managed one from `IHttpClientFactory`. The `walkthrough.md` notes this was
fixed, but the fix is not reflected in the current `Program.cs`.

**Fix:** Remove `builder.Services.AddScoped<PlaidService>();`.

---

### 2. `FetchTransactions` Does Not Paginate

**File:** `Spening/Services/PlaidService.cs`

```csharp
return json.GetProperty("transactions")
    .EnumerateArray()
    .Select(MapTransaction)
    .ToList();
```

Plaid's `/transactions/get` returns a maximum of 500 transactions per call. If an account has
more than 500 transactions in a year this silently truncates results. `walkthrough.md` notes
this was identified but the fix (offset pagination loop) is not in the current `PlaidService.cs`.

**Fix:** Loop using `count`/`offset` until `transactions.Count >= total_transactions`:

```csharp
var allTransactions = new List<Transaction>();
int offset = 0;
const int pageSize = 500;
int totalTransactions;

do
{
    var json = await Post("/transactions/get", new
    {
        client_id = ClientId,
        secret = await Secret(),
        access_token = plainToken,
        start_date = $"{year}-01-01",
        end_date = $"{year}-12-31",
        options = new
        {
            include_personal_finance_category = true,
            count = pageSize,
            offset = offset
        }
    });

    totalTransactions = json.GetProperty("total_transactions").GetInt32();
    var page = json.GetProperty("transactions").EnumerateArray()
        .Select(MapTransaction).ToList();
    allTransactions.AddRange(page);
    offset += page.Count;
} while (allTransactions.Count < totalTransactions);

return allTransactions;
```

---

### 3. `Accounts.razor` — `LinkTokenResponse` JSON Binding Fails

**File:** `Spening/Pages/Accounts.razor`

The page reads the link token as:

```csharp
var json = await resp.Content.ReadFromJsonAsync<Dictionary<string, string>>();
var linkToken = json?["link_token"] ?? throw new Exception("link_token missing from response");
```

This works, but `walkthrough.md` notes an earlier version used a typed record without
`[JsonPropertyName]`. If the code is ever refactored back to a typed record, it will silently
fail. The dictionary approach is fine as-is, but worth a note for future maintainers.

---

### 4. `TransactionService.FetchAll` Returns Wrong Count

**File:** `Spening/Services/TransactionService.cs`

```csharp
var (_, added, _) = await MergeWithTotal(all, new List<string>());
return added;
```

`MergeWithTotal` returns `(total, added, removed)`. The second element (`added`) is the count
of *newly inserted* rows only, not the total written. The endpoint returns `{ written: N }` which
implies total written, but on a re-fetch of an existing year `added` will be 0 even though
hundreds of rows were updated. This is misleading to the user.

**Fix:** Either rename the field to `inserted` in the API response, or return `all.Count` as
`written` to reflect total transactions processed.

---

### 5. Settings Schema Migration is Incomplete

**File:** `docs/implementation_plan.md` / `Spening/Models/AppSetting.cs`

`implementation_plan.md` describes restructuring `AppSetting` from key-value to a typed model,
but the current `AppSetting.cs` still uses the original key-value structure:

```csharp
public class AppSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
```

`SettingsService.cs` also still uses `FindAsync(key)` (EAV pattern). The migration and model
changes described in `implementation_plan.md` have not been implemented. This is an **incomplete
feature**, not a breakage, but the `featureChanges.md` doc implies this was intended to be done.

---

## 🟡 Risks

### 6. `HttpClient` Base Address Hardcodes `localhost`

**File:** `Spening/Program.cs`

```csharp
.Replace("http://+:", "http://localhost:")
```

This works when Blazor Server calls the API on the same host. However, in a Docker environment
where the container's hostname matters, or when behind a reverse proxy, `localhost` may not
resolve correctly. More critically, if `ASPNETCORE_URLS` is not set, the fallback is
`http://localhost:8080` — but Kestrel's default listen port in development is `5200` (per
`launchSettings.json`), causing Blazor pages to fail silently in dev.

**Fix:** In development, read the actual Kestrel port from `launchSettings.json` or explicitly
set `ASPNETCORE_URLS=http://localhost:5200` in dev. Or detect the environment:

```csharp
var port = builder.Environment.IsDevelopment() ? "5200" : "8080";
var baseAddress = $"http://localhost:{port}/";
```

---

### 7. Sync Cursor Saved Even on Partial Failure

**File:** `Spening/Services/TransactionService.cs`

```csharp
foreach (var acct in accounts)
{
    var (newTxns, removedIds, nextCursor) = await _plaid.SyncTransactions(...);
    var (a, r) = await Merge(newTxns, removedIds);
    acct.SyncCursor = nextCursor;
    _db.LinkedAccounts.Update(acct);
}
await _db.SaveChangesAsync(); // ← all cursors saved at the end
```

If `Merge()` throws after `SyncTransactions()` returns a new cursor, the cursor will still be
saved on the next successful iteration's `SaveChangesAsync`. This means transactions from the
failed account's sync window could be skipped permanently.

**Fix:** Save the cursor for each account immediately after its successful merge, or wrap each
account's sync in a try-catch that skips cursor update on failure.

---

### 8. `RemoveItem` Removes All Accounts With Same Encrypted Token (Fragile Comparison)

**File:** `Spening/Services/PlaidService.cs`

```csharp
var toRemove = _db.LinkedAccounts
    .Where(a => a.AccessToken == encryptedAccessToken);
```

This relies on encrypted tokens being byte-for-byte identical across DB rows for the same
Plaid Item. Since AES-GCM uses a random nonce per encryption call, the same plaintext produces
a different ciphertext each time. If `FetchAndPersistAccounts` is called more than once for the
same Item (e.g. re-linking), accounts may end up with different encrypted values for the same
underlying token, breaking the group-delete.

**Fix:** Group accounts by a stable identifier. Add a `ItemId` column to `LinkedAccount` (Plaid
returns `item_id` from `/accounts/get`) and delete by `ItemId` instead.

---

### 9. `DebugController` Left in Production Code

**File:** `Spening/Controllers/DebugController.cs`

This controller exposes (partially masked) Plaid credentials at `GET /api/debug/env`. Even with
masking, confirming credential length and prefix/suffix in a production endpoint is a security
risk — it gives an attacker confirmation that credentials exist and their approximate format.

**Fix:** Delete this file, or gate it behind `builder.Environment.IsDevelopment()` in
`Program.cs` using conditional controller registration or a middleware guard.

---

### 10. `AccountsController.Remove` Could 500 if Plaid Call Fails

**File:** `Spening/Controllers/AccountsController.cs`

```csharp
await _plaid.RemoveItem(account.AccessToken);
```

`RemoveItem` calls Plaid's `/item/remove` and throws `InvalidOperationException` on any non-2xx
response. If Plaid is unreachable or the token is already revoked, the account is never removed
from the local DB, leaving a dangling record the user cannot remove through the UI.

**Fix:** Catch Plaid errors in `RemoveItem` (or the controller) and proceed with local deletion
even if the remote revocation fails, logging the error. A "force remove" path is a common
pattern for linked account management.

---

### 11. `MergeWithTotal` Uses `FindAsync` in a Loop

**File:** `Spening/Services/TransactionService.cs`

```csharp
foreach (var txn in incoming)
{
    var existing = await _db.Transactions.FindAsync(txn.TransactionId);
    ...
}
```

For a full-year fetch this issues one `SELECT` per transaction. With 1,000+ transactions this
produces 1,000+ round-trips. This will be noticeably slow and hammers the DB.

**Fix:** Load existing IDs in bulk before the loop:

```csharp
var incomingIds = incoming.Select(t => t.TransactionId).ToHashSet();
var existingIds = await _db.Transactions
    .Where(t => incomingIds.Contains(t.TransactionId))
    .Select(t => t.TransactionId)
    .ToHashSetAsync();
```

Then branch on `existingIds.Contains(txn.TransactionId)` inside the loop.

---

## 🔵 Improvements

### 12. `output_year` Behavior Doesn't Match `featureChanges.md`

**File:** `docs/featureChanges.md`

The spec says the active year should default to the year of the most recent transaction, with a
7-year dropdown. The current implementation stores `output_year` as a static setting and the UI
uses a plain number input. `implementation_plan.md` documents the intended fix but it has not
been implemented yet.

---

### 13. Missing `@using Microsoft.AspNetCore.Components.Routing` in `App.razor`

**File:** `Spening/App.razor`

`App.razor` uses `<Router>` and `<RouteView>` but lacks the `@using` directives. This is covered
by `_Imports.razor`, but `App.razor` lives at the project root, not under `Pages/`, so
`_Imports.razor` may not apply to it depending on the build. Adding explicit usings to `App.razor`
or a root-level `_Imports.razor` makes this unambiguous.

---

### 14. `Category` Field Never Null But Treated as Nullable Throughout UI

**File:** Various

`Transaction.Category` is `string` (non-nullable) and defaults to `""`. Yet every Razor page
guards against null: `string.IsNullOrEmpty(t.Category) ? "—" : t.Category`. This is harmless
but inconsistent. Pick one: either make `Category` nullable (`string?`) in the model and use
null-coalescing, or keep it non-nullable and use `IsNullOrEmpty` everywhere.

---

### 15. `PlaidService` Uses `HttpClient` But Doesn't Set Default Headers

**File:** `Spening/Services/PlaidService.cs`

Every Plaid request is `POST` with `Content-Type: application/json`. `PostAsJsonAsync` sets the
content type per-request automatically, so this is fine. But if Plaid ever requires an
`Authorization` header or `Plaid-Version` header globally, there's no central place to add it.
Setting `DefaultRequestHeaders` in the typed client factory in `Program.cs` would be cleaner
for future-proofing.

---

### 16. `TransactionService.ExportCsv` Ignores `year` Parameter Partially

**File:** `Spening/Services/TransactionService.cs`

```csharp
public async Task<byte[]> ExportCsv(int? year = null)
{
    var transactions = await Query(year);
```

`Query()` with `year = null` returns all years. `TransactionsController.Export` resolves the year:

```csharp
var resolvedYear = year ?? await _settings.GetOutputYear();
var csv = await _txns.ExportCsv(resolvedYear);
```

So a resolved year is always passed. But `ExportCsv` itself accepts `null` and would export all
years if called directly with no argument. This is a minor inconsistency in the service API.

---

### 17. No Error Boundary in Blazor Layout

**File:** `Spening/Shared/MainLayout.razor`

Unhandled exceptions in Blazor Server components disconnect the circuit and show a generic error
UI. Adding an `<ErrorBoundary>` wrapper around `@Body` in `MainLayout.razor` allows graceful
in-page error display:

```razor
<main class="content">
    <ErrorBoundary>
        <ChildContent>@Body</ChildContent>
        <ErrorContent Context="ex">
            <div class="alert-error">Something went wrong: @ex.Message</div>
        </ErrorContent>
    </ErrorBoundary>
</main>
```

---

### 18. Docker Image Runs as `appuser` But Kestrel May Need Port 8080

**File:** `Spening/Dockerfile`

Port 8080 is unprivileged (>1024) so the non-root user can bind to it — this is fine on Linux.
Just worth noting: if the port is ever changed to something below 1024 (e.g. 80), the non-root
user will fail to bind and the container will crash silently.

---

### 19. `_Host.cshtml` Loads Plaid SDK on Every Page

**File:** `Spening/Pages/_Host.cshtml`

The Plaid Link SDK (`link-initialize.js`) is loaded globally in `_Host.cshtml` but is only used
on the Accounts page. This adds ~60KB of JS to every page load. The previous design (loading it
inline in `Accounts.razor` via a `<script>` tag) was more efficient, though it had ordering
issues. A better approach is a lazy-loaded script tag triggered only on the Accounts page using
`IJSRuntime.InvokeVoidAsync` to dynamically inject the script.

---

### 20. Migration Seeds `output_year` as `"2026"` (Hardcoded)

**File:** `Spening/Data/Migrations/20260608192250_InitialCreate.cs`

```csharp
{ "output_year", "2026" },
```

The `AppDbContext` seeds using `DateTime.UtcNow.Year.ToString()` which is evaluated at migration
generation time and baked in as a literal. Anyone running this migration in 2027+ will get a
stale default. This is a known EF Core limitation with `HasData` seeding. Once the
`implementation_plan.md` typed-settings migration is applied, this becomes moot.

---

## Priority Order for Fixes

| # | Issue | Effort |
|---|---|---|
| 1 | Remove duplicate `PlaidService` DI registration | 1 line |
| 9 | Remove or gate `DebugController` | 5 min |
| 2 | Paginate `FetchTransactions` | 30 min |
| 11 | Bulk-load existing IDs in `MergeWithTotal` | 20 min |
| 8 | Store `ItemId` for group-delete safety | 1–2 hrs (+ migration) |
| 10 | Force-remove accounts even if Plaid call fails | 20 min |
| 7 | Save sync cursor per-account, not in batch | 15 min |
| 6 | Fix `HttpClient` base address in dev | 15 min |
| 12 | Implement dynamic year from last transaction | 1–2 hrs |
| 17 | Add `ErrorBoundary` to layout | 10 min |
