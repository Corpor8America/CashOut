# Spening — Code Review #2

Review of the codebase as of the `FeatureExpansion` + `creditdebitnormlize` migrations,
including the new Merchants page and BusinessNormalizationController additions discussed in chat.

Severity levels: **🔴 Bug** (will break at runtime), **🟡 Risk** (breaks under specific conditions),
**🔵 Improvement** (quality, correctness, or maintainability).

---

## 🔴 Bugs

### 1. `Merchants.razor` — `MappingDto` deserialization will always fail

**File:** `Spening/Pages/Merchants.razor` (new), `BusinessNormalizationController.cs`

The page declares:
```csharp
private record MappingDto(int Id, int RawBusinessId, string RawBusinessName, int AliasId, string AliasName);
```

But `BusinessNormalizationController.ListMappings` returns the result of `_svc.GetAllMappings()`,
which returns `List<RawBusinessAliasMap>` with `.Include(m => m.RawBusiness).Include(m => m.Alias)`
navigation properties. The JSON shape is:

```json
{ "id": 1, "rawBusinessId": 5, "rawBusiness": { "rawName": "..." }, "aliasId": 2, "alias": { "aliasName": "..." } }
```

The flat fields `RawBusinessName` and `AliasName` do not exist at the top level.
`GetFromJsonAsync` will succeed (no exception) but both string fields will deserialize as `null`,
so the "Mapped From" column will always be empty and alias name lookups will silently fail.

**Fix:** Project in the controller before returning:
```csharp
[HttpGet("mappings")]
public async Task<IActionResult> ListMappings()
{
    var maps = await _svc.GetAllMappings();
    return Ok(maps.Select(m => new
    {
        m.Id,
        RawBusinessId = m.RawBusinessId,
        RawBusinessName = m.RawBusiness.RawName,
        AliasId = m.AliasId,
        AliasName = m.Alias.AliasName
    }));
}
```

---

### 2. `CsvImportController.ExportSkipped` — download link will 405

**File:** `Spening/Controllers/CsvImportController.cs`, `Spening/Pages/CsvImport.razor`

The controller defines `ExportSkipped` as `[HttpPost]`:
```csharp
[HttpPost("{accountId}/skipped-export")]
public IActionResult ExportSkipped([FromBody] List<SkippedRow> skippedRows)
```

But the Razor page links to it with a plain anchor tag:
```razor
<a href="api/csv-import/@AccountId/skipped-export" target="_blank">
    <button type="button" class="btn-sm">Download Skipped Rows CSV</button>
</a>
```

A browser `<a href>` issues a GET request. The server will return `405 Method Not Allowed`.

**Fix (option A):** Change the endpoint to `[HttpGet]` and pass skipped row data via query string
or temporary server-side storage (e.g. a short-lived in-memory cache keyed by a token).

**Fix (option B — simpler):** Generate the CSV client-side in Blazor and trigger a JS download,
removing the server round-trip entirely:
```csharp
var csv = "Row,RawData,Reason\n" + string.Join("\n",
    _result.SkippedRows.Select(r => $"{r.RowNumber},{Esc(r.RawData)},{Esc(r.Reason)}"));
await JS.InvokeVoidAsync("speningBlazor.downloadText", "skipped-rows.csv", csv);
```

---

### 3. `Merchants.razor` — alias category edits are lost on page refresh

**File:** `Spening/Pages/Merchants.razor` (new)

`SaveAliasCategory` calls `PATCH /api/normalization/aliases/{id}/category`. That endpoint does
not exist on the current controller. The method catches the non-success response and updates local
component state as a fallback, but any navigating away or refreshing the page will revert to the
server value because the DB was never updated.

**Fix:** Add the missing endpoint to `BusinessNormalizationController`:
```csharp
private readonly AppDbContext _db; // inject alongside _svc

[HttpPatch("aliases/{id:int}/category")]
public async Task<IActionResult> UpdateAliasCategory(int id, [FromBody] UpdateCategoryRequest req)
{
    var alias = await _db.BusinessAliases.FindAsync(id);
    if (alias == null) return NotFound();
    alias.Category = req.Category ?? "";
    await _db.SaveChangesAsync();
    return Ok(alias);
}
```

Alternatively, add `UpdateAliasCategory(int id, string category)` to
`BusinessNormalizationService` and call it from the controller to keep the pattern consistent.

---

### 4. `Transaction.Amount` sign contract is violated for credits

**File:** `Spening/Models/Transaction.cs`

The model XML doc comment states: *"Amount is always >= 0"*. But `NormalizeSingleAmount`
returns a negative `Amount` for credits:
```csharp
var credit = Math.Abs(externalAmount);
return (credit, null, -credit); // Amount = -credit (negative)
```

`ReportService.GetExpenses` correctly filters `t.Amount > 0`, so reports work. But the comment
is wrong, the `Transactions.razor` display logic relies on the sign inconsistency, and any future
code assuming the documented invariant will break silently.

**Fix:** Decide on one contract and enforce it everywhere. The cleanest option is:
- `Amount` = `Debit - Credit` (can be negative for credits — remove the "always >= 0" claim)
- Update the model comment accordingly
- The display expression in `Transactions.razor` will then work naturally as-is

---

### 5. `Transactions.razor` — expenses display as negative numbers

**File:** `Spening/Pages/Transactions.razor`

```razor
<td style="color:@(t.Credit != null ? "black" : "red")">
    @((t.Credit ?? -t.Debit ?? 0).ToString("C"))
</td>
```

Operator precedence: `t.Credit ?? (-t.Debit ?? 0)`. When `t.Debit` is non-null (an expense),
`-t.Debit` negates it, so a $50 purchase displays as `−$50.00` in red. Most users expect
expenses to show as positive red numbers (the colour already conveys direction).

**Fix:**
```razor
@{
    var display = t.Credit.HasValue ? t.Credit.Value : (t.Debit ?? 0);
}
<td style="color:@(t.Credit != null ? "green" : "red")">
    @display.ToString("C")
</td>
```

---

## 🟡 Risks

### 6. Severe N+1 queries in `TransactionService.MergePlaid`

**File:** `Spening/Services/TransactionService.cs`

Inside the merge loop over incoming transactions:
```csharp
var rawBusinessId = await _normalization.GetOrCreateRawBusiness(txn.Name, txn.Category);
var aliasId = await _normalization.GetAliasId(rawBusinessId);
var alias = await _db.BusinessAliases.FindAsync(aliasId.Value);
var raw = await _db.RawBusinesses.FindAsync(rawBusinessId);
```

Each of those is a separate DB round-trip. A sync returning 500 transactions issues up to
**2,000 individual queries** before the `SaveChangesAsync`. On a remote DB this will time out.
`GetOrCreateRawBusiness` also calls `SaveChangesAsync` per new merchant.

**Fix:** Batch-load all relevant `RawBusiness` and `BusinessAlias` rows before the loop:
```csharp
var rawNames = incoming.Select(t => BusinessNormalizationService.NormalizeName(t.Name)).ToHashSet();
var existingRaw = await _db.RawBusinesses
    .Where(b => rawNames.Contains(b.RawName))
    .ToDictionaryAsync(b => b.RawName);

var mappings = await _db.RawBusinessAliasMaps
    .Include(m => m.Alias)
    .Where(m => existingRaw.Values.Select(r => r.Id).Contains(m.RawBusinessId))
    .ToDictionaryAsync(m => m.RawBusinessId);
```
Then resolve from dictionaries in-process. Flush new businesses in one `SaveChangesAsync` at the
end.

The same pattern applies to `CsvImportService.Import`.

---

### 7. `CsvImportController` — missing `[FromForm]` / multipart wiring for `IFormFile`

**File:** `Spening/Controllers/CsvImportController.cs`

The `Preview` and `Import` endpoints accept `IFormFile file` without `[FromForm]`:
```csharp
public async Task<IActionResult> Preview(string accountId, IFormFile file)
```

`IFormFile` binding requires the request to be `multipart/form-data`. The Blazor page sends
`MultipartFormDataContent` which is correct, but without explicit `[FromForm]` the model binder
may not resolve correctly in some hosting configurations. More importantly, the controller is
missing `[Consumes("multipart/form-data")]` which means Swagger/OpenAPI will not document it
correctly and the binder may reject requests if content negotiation is strict.

**Fix:** Add `[FromForm]` to the parameter:
```csharp
public async Task<IActionResult> Preview(string accountId, [FromForm] IFormFile file)
```

---

### 8. `BusinessNormalizationService.GetOrCreateRawBusiness` — race condition on concurrent imports

**File:** `Spening/Services/BusinessNormalizationService.cs`

```csharp
var existing = await _db.RawBusinesses.FirstOrDefaultAsync(b => b.RawName == normalized);
if (existing != null) return existing.Id;

var newBusiness = new RawBusiness { ... };
_db.RawBusinesses.Add(newBusiness);
await _db.SaveChangesAsync();
```

The `RawName` column has a unique index. If two concurrent import requests both reach the
`FirstOrDefaultAsync` check before either inserts, both will attempt to insert the same name and
one will throw a `PostgresException` (unique constraint violation), surfacing as a 500 to the
user.

**Fix:** Wrap the insert in a try-catch for unique violation and re-fetch on conflict:
```csharp
try
{
    _db.RawBusinesses.Add(newBusiness);
    await _db.SaveChangesAsync();
    return newBusiness.Id;
}
catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("unique") == true)
{
    _db.Entry(newBusiness).State = EntityState.Detached;
    var retry = await _db.RawBusinesses.FirstOrDefaultAsync(b => b.RawName == normalized);
    return retry!.Id;
}
```

---

### 9. `CsvImport.razor` — InputFile size limit mismatched with server limit

**File:** `Spening/Pages/CsvImport.razor`, `Spening/Program.cs`

```csharp
// CsvImport.razor
using var stream = file.OpenReadStream(maxAllowedSize: 10 * 1024 * 1024); // 10 MB

// Program.cs
o.MultipartBodyLengthLimit = 10 * 1024 * 1024; // 10 MB
```

The `MultipartBodyLengthLimit` applies to the entire multipart body including boundaries,
headers, and field names — not just the file bytes. A file of exactly 10 MB will exceed the
server limit and be rejected with a 413, while Blazor's `InputFile` component will have already
read it successfully. The mismatch creates a confusing failure mode where the file appears to
load but the upload silently fails or throws an unhelpful error.

**Fix:** Set the server limit slightly higher than the client limit:
```csharp
o.MultipartBodyLengthLimit = 11 * 1024 * 1024; // 11 MB to accommodate multipart overhead
```

---

### 10. `Dockerfile` — `dotnet nuget locals all --clear` breaks layer caching

**File:** `Spening/Dockerfile`

```dockerfile
COPY Spening/Spening.csproj ./
RUN dotnet nuget locals all --clear && dotnet restore
```

The entire point of copying only the `.csproj` first is so Docker caches the restore layer and
only re-runs it when dependencies change. Prepending `nuget locals all --clear` invalidates the
NuGet cache on every build, defeating this optimisation and adding 30–120 seconds to every CI
build or local `docker build`.

**Fix:** Remove the cache clear entirely. If a corrupted cache is suspected in CI, use
`--no-cache` on the `docker build` command instead of baking the clear into the image definition.
```dockerfile
RUN dotnet restore
```

---

### 11. No `.dockerignore` file

**File:** (missing)

Without a `.dockerignore`, `COPY Spening/ ./` in the Dockerfile copies `bin/`, `obj/`, any
`.env` file present in the directory, and all migration designer files into the build context.
This has two consequences: (1) the build context sent to Docker is much larger than necessary,
slowing every build; (2) if a developer has a local `.env` inside `Spening/`, its contents
(Plaid secrets, encryption key) are baked into the image layer.

**Fix:** Add `Spening/.dockerignore` (or root-level `.dockerignore`):
```
bin/
obj/
.env
*.user
```

---

### 12. `AppDbContextFactory` — hardcoded relative path for `.env`

**File:** `Spening/Data/AppDbContextFactory.cs`

```csharp
DotNetEnv.Env.Load("../.env");
```

This path is relative to the working directory at the time `dotnet ef` runs. It works when
running from `Spening/` but breaks if EF tools are invoked from the repo root or in a CI
pipeline where the working directory is different. The migration will fail with a confusing
"ConnectionStrings:Default is required" error.

**Fix:** Use a path relative to the executing assembly or resolve upward:
```csharp
var envPath = Path.Combine(
    Directory.GetCurrentDirectory(),
    Directory.GetCurrentDirectory().EndsWith("Spening") ? "../.env" : ".env");
if (File.Exists(envPath))
    DotNetEnv.Env.Load(envPath);
```

---

### 13. `ManualAccounts.razor` — invalid HTML nesting

**File:** `Spening/Pages/ManualAccounts.razor`

```razor
<a href="/csv-import/@acct.Id">
    <button type="button" class="btn-sm btn-primary">Import CSV</button>
</a>
```

An `<a>` element containing a `<button>` is invalid HTML (interactive content cannot be nested).
Most browsers will render it, but screen readers may announce it incorrectly, and some browser
engines will restructure the DOM, potentially breaking click handling.

**Fix:** Use `NavigationManager.NavigateTo` in an `@onclick` handler, or style the `<a>` as a
button directly:
```razor
<a href="/csv-import/@acct.Id" class="btn btn-sm btn-primary">Import CSV</a>
```

---

### 14. `Accounts.razor` — "Add Account" button is active before JS interop is ready

**File:** `Spening/Pages/Accounts.razor`

The button is enabled on render and only shows an error if clicked before `_isRendered` is set:
```csharp
if (!_isRendered)
{
    _error = "Page not fully loaded yet, please wait a moment and try again."
    ...
}
```

On slow connections (or Blazor Server cold start) the button is visually available but
non-functional, and the error message is confusing. The `_linking` bool disables the button
during a flow, but there's no `disabled` state for the pre-render window.

**Fix:** Disable the button until `_isRendered`:
```razor
<button class="btn-primary" @onclick="StartLinkFlow" disabled="@(!_isRendered || _linking)">
```

---

## 🔵 Improvements

### 15. `ReportService` — all aggregations load full table into memory

**File:** `Spening/Services/ReportService.cs`

All five report methods call `GetExpenses(year)` which fetches every expense transaction for the
year into a `List<Transaction>` and then groups/sorts in-process with LINQ. For a user with
several years of data and multiple accounts this could be tens of thousands of rows per report
call.

All five aggregations can be pushed to the DB. Example for monthly totals:
```csharp
return await _db.Transactions
    .Where(t => t.Date.Year == y && t.Amount > 0)
    .GroupBy(t => new { t.Date.Year, t.Date.Month })
    .Select(g => new MonthlyRow(
        $"{g.Key.Year}-{g.Key.Month:D2}",
        ...,
        g.Sum(t => t.Amount),
        g.Count()))
    .OrderBy(r => r.Month)
    .ToListAsync();
```
Note: `DateOnly` grouping requires EF Core 8+ with Npgsql — verify support before converting the
pivot query.

---

### 16. `Console.Error.WriteLine` used instead of `ILogger` throughout services

**Files:** `Spening/Services/PlaidService.cs`, `Spening/Services/TransactionService.cs`

```csharp
Console.Error.WriteLine($"[PlaidService] RemoveItem: Plaid revocation failed...");
Console.Error.WriteLine($"[TransactionService] SyncAll: failed for account...");
```

`Console.Error` bypasses the ASP.NET Core logging pipeline — log level filtering, structured
logging, and any configured sinks (file, Application Insights, etc.) are all skipped. The output
also goes to stderr in Docker, which may be ignored or separated from stdout logs.

**Fix:** Inject `ILogger<PlaidService>` and `ILogger<TransactionService>` and use
`_logger.LogError(...)`.

---

### 17. `SettingsServiceTests` — test names and assertions do not match test body

**File:** `Spening.Tests/SettingsServiceTests.cs`

`Set_ThenGet_RoundTrips` contains no `await`, no `Set` call, and no round-trip:
```csharp
public async Task Set_ThenGet_RoundTrips()
{
    var db = BuildDb(...);
    var svc = new SettingsService(db, BuildConfig());
    var result = svc.GetPlaidEnvironment();  // ← only a Get
    Assert.AreEqual("production", result);
}
```

The method is `async Task` but has no `await` — MSTest will compile and run it synchronously
with no warning. The test name is actively misleading for anyone maintaining the test suite.

**Fix:** Rename to `GetPlaidEnvironment_ReadsFromConfig` and remove the `async`/`Task`.

---

### 18. Test coverage gaps

**Files:** `Spening.Tests/`

The following services have zero test coverage:
- `CsvImportService` — complex parsing, dedup, amount normalization, skipped-row logic
- `TransactionService` — sync cursor handling, CSV protection, merge logic
- `BusinessNormalizationService` — category priority resolution
- `PlaidService` — any unit-testable helpers (e.g. `MapTransaction`, `NormalizeSingleAmount`)

`Transaction.NormalizeSingleAmount` and `NormalizeSplitColumns` are static methods with no
external dependencies — they are trivial to unit test and represent critical financial logic.

---

### 19. `CsvMappingProfile.MappedColumns()` yields empty strings

**File:** `Spening/Models/CsvMappingProfile.cs`

```csharp
public IEnumerable<string> MappedColumns()
{
    yield return DateColumn.ToLowerInvariant();        // always yielded, even if ""
    yield return DescriptionColumn.ToLowerInvariant(); // always yielded, even if ""
    ...
}
```

If a profile is saved with `DateColumn = ""` (which shouldn't happen but isn't validated
server-side), `ValidateProfile` checks whether `""` exists in the CSV headers. An empty CSV
header won't exist, so validation will always report the empty string as missing — a confusing
error. If the header list does somehow contain an empty string, validation passes silently with
a broken mapping.

**Fix:** Skip empty-string columns:
```csharp
if (!string.IsNullOrEmpty(DateColumn)) yield return DateColumn.ToLowerInvariant();
if (!string.IsNullOrEmpty(DescriptionColumn)) yield return DescriptionColumn.ToLowerInvariant();
```
And add server-side validation in `CsvImportController.SaveProfile` that rejects profiles with
empty required fields.

---

### 20. `ReportService.GetPivot` — `IndexOf` in nested loop is O(n²)

**File:** `Spening/Services/ReportService.cs`

```csharp
topCats.Select(c => rows.Sum(r => r.Values[topCats.IndexOf(c)])).ToList()
```

`topCats.IndexOf(c)` is O(n) inside a loop that iterates n=8 categories × m months. With a
fixed cap of 8 categories this will never cause a real performance problem, but it can be
trivially fixed:

```csharp
topCats.Select((c, i) => rows.Sum(r => r.Values[i])).ToList()
```

---

### 21. `docker-compose.yml` — placeholder image name not updated

**File:** `docker-compose.yml`

```yaml
image: yourusername/spening:latest
```

Anyone cloning the repo and running `docker compose up` will attempt to pull an image that does
not exist. This should either be updated to the real Docker Hub username or clearly documented
as a required substitution step in the README.

---

### 22. `Transactions.razor` — categories populated from loaded results only

**File:** `Spening/Pages/Transactions.razor`

The category filter dropdown is populated from the currently loaded transactions:
```csharp
_categories = _transactions.Select(t => t.Category)...Distinct()...ToList();
```

If the user filters by year and that year has no transactions, the category list empties.
Switching year then filtering by category requires two interactions (first reload year, then the
category appears). More importantly, if the user picks a category and then changes the year,
the previously selected `_filterCategory` may not exist in the new year's data — the API will
return an empty result with no indication of why.

**Fix:** Either reset `_filterCategory = ""` whenever `_filterYear` changes, or load the
category list from a dedicated `/api/transactions/categories?year=` endpoint that returns
available categories for the selected year before applying the filter.

---

## Priority Order

| # | Issue | Effort |
|---|---|---|
| 1 | Fix `ListMappings` projection (Merchants page broken) | 10 min |
| 2 | Add `PATCH aliases/{id}/category` endpoint | 15 min |
| 3 | Fix skipped-rows CSV download (405) | 30 min |
| 5 | Fix expense display sign in Transactions page | 5 min |
| 4 | Fix `Amount` model comment / sign contract | 5 min |
| 6 | Batch N+1 queries in MergePlaid and CsvImport | 2–3 hrs |
| 7 | Add `[FromForm]` to IFormFile parameters | 5 min |
| 14 | Disable Add Account button pre-render | 5 min |
| 13 | Fix invalid HTML nesting in ManualAccounts | 5 min |
| 11 | Add `.dockerignore` | 10 min |
| 10 | Remove NuGet cache clear from Dockerfile | 2 min |
| 9 | Fix multipart size limit mismatch | 2 min |
| 8 | Handle unique constraint race in GetOrCreateRawBusiness | 20 min |
| 12 | Fix AppDbContextFactory env path | 15 min |
| 16 | Replace Console.Error with ILogger | 30 min |
| 22 | Reset category filter on year change | 15 min |
| 19 | Fix MappedColumns empty-string yield | 10 min |
| 17 | Fix misleading test names | 10 min |
| 15 | Push report aggregations to DB | 2–3 hrs |
| 18 | Add missing unit tests | 4+ hrs |
| 20 | Fix IndexOf in pivot | 2 min |
| 21 | Update docker-compose placeholder image | 2 min |