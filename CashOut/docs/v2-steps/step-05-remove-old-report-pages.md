# Step 5: Remove Old Report Pages

## Goal

Delete the four old report pages and strip the `ReportService` down to only the methods needed by the two new reports (Inflow vs Outflow and Spending by Category). Also remove corresponding API endpoints.

## Pages to Delete Entirely

### 1. `CashOut/Pages/Reports.razor` (543 lines)
Route: `/reports` — Executive Summary dashboard. Contains: overview metrics, top categories, top merchants, recurring charges, alerts, account summary. All replaced by the two focused reports.

### 2. `CashOut/Pages/ReportMerchant.razor` (351 lines)
Route: `/reports/merchant` — Spending by Merchant with top-N selector, drill-down transactions.

### 3. `CashOut/Pages/ReportIncome.razor` (363 lines)
Route: `/reports/income` — Income report by source/merchant, drill-down transactions. Income is now just "inflow" on the cashflow report.

### 4. `CashOut/Pages/ReportCashFlow.razor` (366 lines)
Route: `/reports/cashflow` — Old Net Cash Flow with trailing averages, drill-down. Replaced by the new simplified Inflow vs Outflow report.

## Files to Modify

### 5. `CashOut/Controllers/ReportsController.cs` — Remove Old Endpoints

**Delete these action methods entirely:**

- `Monthly` (lines 16-23) — `GET /api/reports/monthly`
- `Income` (lines 34-41) — `GET /api/reports/income`
- `Pivot` (lines 43-51) — `GET /api/reports/pivot`
- `Merchants` (lines 53-62) — `GET /api/reports/merchants`
- `Largest` (lines 64-72) — `GET /api/reports/largest`
- `Summary` (lines 74-81) — `GET /api/reports/summary`
- `CashFlow` (lines 83-90) — `GET /api/reports/cashflow`
- `CategorySummary` (lines 92-98) — `GET /api/reports/category-summary`

**Keep only `Category` (lines 25-32):**

```csharp
[HttpGet("category")]
public async Task<IActionResult> Category(
    [FromQuery] int? year, [FromQuery] int? month, [FromQuery] string? format)
{
    if (format == "csv")
        return File(await _reports.CategoryCsv(year, month), "text/csv", "category.csv");
    return Ok(await _reports.GetByCategory(year, month));
}
```

**Add the new `Cashflow` endpoint** (built in Step 8):

```csharp
[HttpGet("cashflow")]
public async Task<IActionResult> Cashflow(
    [FromQuery] int? year, [FromQuery] int? month,
    [FromQuery] string? accountId, [FromQuery] string? format)
{
    if (format == "csv")
        return File(await _reports.CashflowCsv(year, month, accountId), "text/csv", "cashflow.csv");
    return Ok(await _reports.GetCashflow(year, month, accountId));
}
```

**Replace the `Category` endpoint** to match the new simplified signature:

```csharp
[HttpGet("category")]
public async Task<IActionResult> Category(
    [FromQuery] int? year, [FromQuery] int? month,
    [FromQuery] string? accountId, [FromQuery] string? format)
{
    if (format == "csv")
        return File(await _reports.CategoryCsv(year, month, accountId), "text/csv", "category.csv");
    return Ok(await _reports.GetByCategory(year, month, accountId));
}
```

### 6. `CashOut/Services/ReportService.cs` — Strip Down to Two Methods

**Remove these methods entirely** (and their associated CSV export methods):

- `GetMonthly()` (lines 90-104) + `MonthlyCsv()` (lines 1117-1123)
- `GetPivot()` (lines 226-266)
- `GetTopMerchants()` (lines 278-405) + `MerchantsCsv()` (lines 1135-1142)
- `GetIncome()` (lines 446-552) + `IncomeCsv()` (lines 554-573)
- `GetCashFlow()` (lines 577-695) + `CashFlowCsv()` (lines 697-715)
- `GetExecutiveSummary()` (lines 742-1013) + `ExecutiveSummaryCsv()` (lines 1015-1057)
- `GetLargest()` (lines 1064-1073) + `LargestCsv()` (lines 1144-1155)
- `GetCategorySummary()` (lines 1075-1113) — used by the old month summary on Transactions page

**Remove these private helper methods** (no longer needed without normalization):

- `MerchantKey()` (lines 407-412)
- `MerchantDisplayName()` (lines 414-422)
- `SourceDisplayName()` (lines 424-432)
- `PrimaryCategory()` (lines 434-442)
- `SummaryCategoryKey()` (lines 1059-1060)

**Remove these record types** at the bottom of the file (lines 1162-1444):

Remove all record types EXCEPT keep modified versions of:
- `CategoryReportResult` — will be simplified (see below)
- `CategoryReportRow` — will be simplified (see below)
- `CategoryTransactionRow` — will be simplified (see below)

**Delete these record types entirely:**
- `MonthlyRow`
- `MerchantReportResult`, `MerchantReportRow`, `MerchantTransactionRow`
- `PivotRow`, `PivotResult`
- `CategorySummaryRow`
- `IncomeReportResult`, `IncomeReportRow`, `IncomeTransactionRow`
- `CashFlowReportResult`, `CashFlowMonthRow`, `CashFlowTransactionRow`
- `ExecutiveSummaryResult`, `ExecutiveMonthlyOverview`, `ExecutiveTopCategoryRow`
- `ExecutiveTopMerchantRow`, `ExecutiveRecurringChargeRow`, `ExecutiveAlertSummary`
- `ExecutiveAlertRow`, `ExecutiveAccountSummaryRow`

**Remove the `GetExcludedCategories()` private method and `_excluded` field** (lines 9, 19-23) — excluded categories are being removed in Step 10.

**Keep these methods** (to be simplified in the same step or Step 8):

- `GetByCategory()` — simplify drastically (see below)
- `CategoryCsv()` — update signature

**Also remove all `GetExpenses()` and `GetExpensesInRange()` private methods** (lines 29-69) and `GetIncomeTransactions()` (lines 75-86) — these are used only by removed methods.

**After this step, `ReportService.cs` should be empty stubs or contain only the two new report methods** (built in Step 8). A clean approach: delete the entire file content and rebuild it fresh in Step 8 with only the two new methods and their record types.

### 7. `CashOut/Services/AccountReportService.cs` — Delete Entirely

This file (128 lines) provides per-account cash flow and category reports used by `AccountDetail.razor`'s Cash Flow and By Category tabs (both removed in Step 7). Delete the entire file.

### 8. `CashOut/Controllers/AccountReportsController.cs` — Delete Entirely

This file (19 lines) exposes `GET /api/accounts/{accountId}/reports/cashflow` and `GET /api/accounts/{accountId}/reports/category`. Both are consumed by the removed tabs in `AccountDetail.razor`. Delete the entire file.

### 9. `CashOut/Program.cs` — Remove DI Registration

**Remove line 51:**

```csharp
// REMOVE:
builder.Services.AddScoped<AccountReportService>();
```

## Verification

```bash
dotnet build CashOut/CashOut.csproj
```

After this step, the build should compile (after Step 8 adds back the simplified report methods). If you delete `ReportService` content before Step 8 writes new methods, the build will temporarily fail because `ReportsController` and `CategoryReport.razor` reference `ReportService`. 

**Recommended approach:** Do Steps 5 and 8 together as one atomic change — delete old content and write new content in the same pass.
