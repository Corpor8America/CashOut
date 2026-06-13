# Spening — Code Review (June 2026)

This review evaluates the current state of the Spening codebase, following up on previous review cycles. Significant progress has been made in addressing critical bugs, particularly in merchant normalization and transaction sign handling. However, several architectural risks and UI/UX issues remain.

Severity levels: **🔴 Bug** (runtime failure), **🟡 Risk** (architectural/performance debt), **🔵 Improvement** (quality/consistency).

---

## 🔴 Bugs

### 1. `CsvImport.razor` — Skipped rows export returns 405 Method Not Allowed
The "Download Skipped Rows CSV" button is wrapped in a standard anchor tag:
```razor
<a href="api/csv-import/@AccountId/skipped-export" target="_blank">
    <button type="button" class="btn-sm">Download Skipped Rows CSV</button>
</a>
```
Browser anchor tags always issue `GET` requests. However, the corresponding endpoint in `CsvImportController.cs` is marked as `[HttpPost]`. Clicking this button will result in a `405 Method Not Allowed` error.

**Fix:** Change the controller action to `[HttpGet]` or (better) generate the CSV client-side in Blazor to avoid the extra server round-trip.

### 2. `CsvImportController` — `IFormFile` binding failure
The `Preview` and `Import` actions in `CsvImportController` accept an `IFormFile file` parameter but lack the `[FromForm]` attribute.
```csharp
public async Task<IActionResult> Preview(string accountId, IFormFile file)
```
In many ASP.NET Core configurations, `IFormFile` parameters in `[ApiController]` classes require explicit `[FromForm]` to bind correctly from `multipart/form-data` requests.

**Fix:** Add `[FromForm]` to the `file` parameters in both actions.

---

## 🟡 Risks

### 3. High Memory Usage in `ReportService`
The `ReportService` still relies on fetching all expense transactions for a given year into memory before aggregating them via LINQ-to-Objects:
```csharp
private async Task<List<Transaction>> GetExpenses(int year)
{
    return await _db.Transactions
        .Where(t => t.Date.Year == year && t.Amount > 0)
        .ToListAsync();
}
```
For users with high transaction volumes, this will lead to significant memory pressure and slow report generation.

**Fix:** Refactor report methods to use `IQueryable` and perform aggregations (`Sum`, `Count`, `GroupBy`) directly in the database.

### 4. Bypassing Logging Infrastructure
Multiple services (`PlaidService`, `TransactionService`) continue to use `Console.Error.WriteLine` for error reporting:
```csharp
Console.Error.WriteLine($"[TransactionService] SyncAll: failed for account {acct.AccountId}: {ex.Message}");
```
This bypasses the standard .NET `ILogger` pipeline, making it impossible to capture these errors in structured logs, external sinks, or to filter them by level.

**Fix:** Inject `ILogger<T>` into these services and use structured logging (e.g., `_logger.LogError(ex, "Sync failed for account {AccountId}", acct.AccountId)`).

### 5. Race Condition in `MerchantNormalizationService.Resolve`
While `ResolveBulk` handles existing raw businesses efficiently, the `EnsureRawBusiness` method (used in non-bulk `Resolve`) is susceptible to a race condition:
```csharp
var existing = await _db.RawBusinesses.FirstOrDefaultAsync(b => b.RawNameNormalized == normalized);
if (existing != null) return existing;
// ... Insert new RawBusiness ...
```
If two imports run concurrently, both may see that a merchant doesn't exist and attempt to insert it, causing a unique constraint violation on `RawNameNormalized`.

**Fix:** Use a `try-catch` block around the insert to handle unique constraint violations gracefully by re-fetching the record.

---

## 🔵 Improvements

### 6. Invalid HTML Nesting in `ManualAccounts.razor`
The "Import CSV" button is nested inside an anchor tag:
```razor
<a href="/csv-import/@acct.Id">
    <button type="button" class="btn-sm btn-primary">Import CSV</button>
</a>
```
Nesting interactive elements (button inside a link) is invalid HTML and can cause unpredictable behavior in some browsers and screen readers.

**Fix:** Style the `<a>` tag to look like a button using CSS classes (e.g., `class="btn btn-sm btn-primary"`) and remove the inner `<button>`.

### 7. Redundant Amount Normalization Logic
`Transaction.NormalizeSingleAmount` and `Transaction.NormalizeSplitColumns` both contain similar logic for calculating the final `Amount` and sign.

**Fix:** Consolidate this logic into a single internal helper to ensure consistency, especially if the sign convention is ever changed in the future.

### 8. Hardcoded US Culture in `Program.cs`
The application forces `en-US` culture for all threads:
```csharp
var culture = new CultureInfo("en-US");
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;
```
While appropriate for a US-centric finance app, it limits the application's utility for international users who may prefer different currency or date formats.

**Fix:** Move culture settings to a configuration variable or user-specific setting.

---

## Summary of Progress

| Previous Issue | Status |
|---|---|
| N+1 Queries in `MergePlaid` | **Fixed** (Batch loading implemented) |
| Transaction Sign Inconsistency | **Fixed** (Contract updated in Model & UI) |
| Merchants page deserialization | **Fixed** (DTOs updated) |
| Alias category edits lost | **Fixed** (Patch endpoint added/used) |
| Category filter reset | **Fixed** (Resets on year change) |

The codebase has matured significantly. Addressing the remaining `ReportService` and `CsvImportController` issues will significantly improve the application's robustness and scalability.