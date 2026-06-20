# Spending by Category Report Implementation Spec

**Status:** Implementation-ready  
**Target route:** `/reports/category`  
**Target page:** `CashOut/Pages/ReportCategory.razor`  
**Related scaffold:** `docs/reports-ui-scaffold-spec.md`  
**Related feature note:** `docs/report-features.md`

## 1. Goal

Implement the full Spending by Category report described in `docs/report-features.md` inside the report scaffold described in `docs/reports-ui-scaffold-spec.md`.

The report must show how expense spending is distributed across categories for a selected year, with:

- category totals
- percent of total spending
- 12-month rolling average spend by category
- previous-year comparison
- transaction counts
- drill-down transaction list for a selected category
- CSV export

The scaffold spec already defines where this report lives in the UI. Do not redesign the report navigation shell. Replace the stub content in `CashOut/Pages/ReportCategory.razor` with the implementation described here.

## 2. Important Existing Convention

`docs/report-features.md` says expense transactions are `amount < 0`, but the current CashOut model uses the opposite convention.

Use the convention implemented in `CashOut/Models/Transaction.cs`:

- `Amount > 0` means money leaving the account, so it is an expense.
- `Amount < 0` means money entering the account, so it is income, refund, or credit.
- `Debit` stores outflow amount.
- `Credit` stores inflow amount.

All Spending by Category backend queries must include only `Transaction.Amount > 0`.

## 3. Current State

The app currently has a basic category endpoint:

- `GET /api/reports/category?year=2026`
- `GET /api/reports/category?year=2026&format=csv`

The current `ReportService.GetByCategory` returns:

```csharp
public record CategoryRow(
    string Category,
    decimal Total,
    int Count,
    decimal PctOfSpend);
```

This is not enough for the full report because it lacks previous-period comparison and drill-down transaction data.

The scaffold spec expects:

- `CashOut/Pages/ReportCategory.razor` at route `/reports/category`
- `ReportShell` wrapping the report
- `ExportHref="@($"api/reports/category?year={_year}&format=csv")"`

## 4. Implementation Approach

Extend the existing category report endpoint and service method instead of adding a separate endpoint.

The existing endpoint path should remain:

```http
GET /api/reports/category?year={year}
GET /api/reports/category?year={year}&format=csv
```

Returning a richer JSON shape from this endpoint is acceptable because the old tab-based `Reports.razor` is replaced by the scaffold. The new category page will be the only first-party consumer of the JSON category endpoint.

The CSV export should also be extended so it contains the comparison fields.

## 5. Backend DTOs

Replace the current `CategoryRow` record at the bottom of `CashOut/Services/ReportService.cs` with richer records.

Use these names and fields:

```csharp
public record CategoryReportResult(
    int Year,
    int PreviousYear,
    decimal TotalSpend,
    decimal PreviousTotalSpend,
    decimal TotalChangeAmount,
    decimal TotalChangePercent,
    int TransactionCount,
    List<CategoryReportRow> Categories);

public record CategoryReportRow(
    string Category,
    decimal Total,
    int Count,
    decimal PctOfSpend,
    decimal TwelveMonthAverage,
    decimal TwelveMonthTotal,
    int TwelveMonthCount,
    decimal VsTwelveMonthAverageAmount,
    decimal VsTwelveMonthAveragePercent,
    decimal PreviousTotal,
    int PreviousCount,
    decimal ChangeAmount,
    decimal ChangePercent,
    List<CategoryTransactionRow> Transactions);

public record CategoryTransactionRow(
    string TransactionId,
    string AccountId,
    DateOnly Date,
    string Name,
    string RawName,
    string NormalizedName,
    decimal Amount,
    decimal? Debit,
    decimal? Credit,
    string Category,
    TransactionSource Source);
```

Notes:

- `CategoryReportResult.Categories` is ordered by `Total` descending.
- `CategoryReportRow.Transactions` contains only transactions from the selected year for that category.
- `CategoryReportRow.Transactions` is ordered by `Date` descending, then `Amount` descending.
- `PreviousYear` is `Year - 1`.
- `PreviousTotalSpend` is the total expense spend for `Year - 1`.
- `TwelveMonthAverage` is the trailing 12-month average monthly spend for the category, ending on December 31 of the selected year.
- `TwelveMonthTotal` is the category's total expense spend across that same trailing 12-month window.
- `TwelveMonthCount` is the category's transaction count across that same trailing 12-month window.
- `VsTwelveMonthAverageAmount` is `Total / 12 - TwelveMonthAverage` for year mode. This compares the selected year average month to the trailing baseline.
- `VsTwelveMonthAveragePercent` is `(Total / 12 - TwelveMonthAverage) / TwelveMonthAverage * 100`, rounded to one decimal place. If the rolling average is zero, return `0`.
- `TotalChangeAmount` is `TotalSpend - PreviousTotalSpend`.
- `TotalChangePercent` is `(TotalSpend - PreviousTotalSpend) / PreviousTotalSpend * 100`, rounded to one decimal place. If the previous value is zero, return `0`.
- The same change calculation applies to each category row.

## 6. Backend Service Logic

Modify `ReportService.GetByCategory(int? year = null)` so it returns `CategoryReportResult`.

Expected algorithm:

1. Resolve `y` from the method argument or `_settings.GetOutputYear()`.
2. Set `previousYear = y - 1`.
3. Load current-year expenses:
   - `Date.Year == y`
   - `Amount > 0`
4. Load previous-year expenses:
   - `Date.Year == previousYear`
   - `Amount > 0`
5. Load trailing 12-month expenses:
   - start date is January 1 of selected year
   - end date is December 31 of selected year
   - `Amount > 0`
   - This is a 12-month rolling average in the current year-level report. If future UI adds month selection, redefine the window as the 12 months ending at the selected month.
6. Normalize empty category values to `"(uncategorized)"`.
7. Compute:
   - current grand total
   - previous grand total
   - current transaction count
   - per-category current totals and counts
   - per-category previous totals and counts
   - per-category trailing 12-month totals, counts, and monthly averages
8. Include categories that appear in the current year.
9. Do not include categories that only existed in the previous year or trailing window and have no current-year spending.
10. Round percentage fields to one decimal place.
11. Return an empty `Categories` list when there are no current-year expenses.

Use this helper inside `ReportService`:

```csharp
private static string CategoryKey(Transaction t) =>
    string.IsNullOrWhiteSpace(t.Category) ? "(uncategorized)" : t.Category;

private static decimal Percent(decimal numerator, decimal denominator) =>
    denominator == 0 ? 0 : Math.Round(numerator / denominator * 100m, 1);

private static decimal ChangePercent(decimal current, decimal previous) =>
    previous == 0 ? 0 : Math.Round((current - previous) / previous * 100m, 1);

private static decimal RollingAveragePercent(decimal currentAverage, decimal rollingAverage) =>
    rollingAverage == 0 ? 0 : Math.Round((currentAverage - rollingAverage) / rollingAverage * 100m, 1);
```

Keep `GetExpenses(int year)` as the shared expense query helper, but confirm it still filters `Amount > 0`.

## 7. CSV Export

Update `ReportService.CategoryCsv(int? year = null)` to use the richer result.

CSV headers:

```csv
Category,Total,PctOfSpend,Transactions,TwelveMonthAverage,TwelveMonthTotal,TwelveMonthTransactions,VsTwelveMonthAverageAmount,VsTwelveMonthAveragePercent,PreviousTotal,PreviousTransactions,ChangeAmount,ChangePercent
```

One row per category. Use invariant culture for decimal values. Keep using the existing `Esc` helper for category names.

CSV export endpoint remains:

```http
GET /api/reports/category?year={year}&format=csv
```

The downloaded filename can remain `category.csv`.

## 8. Controller

No new controller route is required.

Keep `ReportsController.Category` as:

```csharp
[HttpGet("category")]
public async Task<IActionResult> Category(
    [FromQuery] int? year, [FromQuery] string? format)
{
    if (format == "csv")
        return File(await _reports.CategoryCsv(year), "text/csv", "category.csv");
    return Ok(await _reports.GetByCategory(year));
}
```

Only the service return type changes.

## 9. UI Page

Implement `CashOut/Pages/ReportCategory.razor`.

The page must:

- use `@page "/reports/category"`
- inject `HttpClient`
- render inside `<ReportShell>`
- load available years from `api/settings/years`
- load report data from `api/reports/category?year={_year}`
- remove `IsStub="true"`
- show loading and error states through `ReportShell`
- expose CSV export through `ExportHref="@($"api/reports/category?year={_year}&format=csv")"`

Suggested top-level page structure:

```razor
@page "/reports/category"
@inject HttpClient Http

<ReportShell Title="Spending by Category"
             Year="_year"
             AvailableYears="_availableYears"
             OnYearChanged="OnYearChanged"
             Loading="_loading"
             Error="_error"
             ExportHref="@($"api/reports/category?year={_year}&format=csv")">
    @if (_report is null || _report.Categories.Count == 0)
    {
        <MudAlert Severity="Severity.Info" Variant="Variant.Outlined">
            No category spending found for @_year.
        </MudAlert>
    }
    else
    {
        <!-- summary cards, category table, and drill-down panel go here -->
    }
</ReportShell>
```

## 10. UI Content Requirements

### 10a. Summary Metrics

At the top of the report body, show three compact summary cards or `MudPaper` panels:

- Total spending: `_report.TotalSpend.ToString("C")`
- Transactions: `_report.TransactionCount`
- Change vs previous year:
  - amount: `_report.TotalChangeAmount.ToString("C")`
  - percent: `_report.TotalChangePercent`

Use green/red semantic coloring carefully:

- Spending increase (`TotalChangeAmount > 0`) should be visually negative or warning-colored.
- Spending decrease (`TotalChangeAmount < 0`) should be visually positive.
- Zero change should be neutral.

Do not make a marketing-style hero section. This is an operational report page.

### 10b. Category Table

Render a `MudTable` over `_report.Categories`.

Columns:

- Category
- Total
- Percent of spend
- 12-month avg
- Vs 12-month avg
- Transactions
- Previous year
- Change

Expected formatting:

- currency values use `.ToString("C")`
- percent values show one decimal place and a percent sign
- count values are right-aligned
- money and percent columns are right-aligned
- table is dense and hoverable
- category rows are sorted by `Total` descending from the API
- `12-month avg` shows `TwelveMonthAverage.ToString("C")`
- `Vs 12-month avg` shows amount and percent difference; spending above average should be warning/negative-colored, and spending below average should be positive-colored

The selected row should drive the drill-down panel.

Implementation detail:

- Track `_selectedCategory` as `CategoryReportRow?`.
- On row click, set `_selectedCategory = context`.
- When data loads, default `_selectedCategory` to the first category, if present.

MudBlazor can use `RowClick`:

```razor
<MudTable Items="@_report.Categories"
          Dense="true"
          Hover="true"
          Breakpoint="Breakpoint.Sm"
          Elevation="0"
          RowClick="OnCategoryRowClick">
```

Then:

```csharp
private void OnCategoryRowClick(TableRowClickEventArgs<CategoryReportRow> args)
{
    _selectedCategory = args.Item;
}
```

### 10c. Drill-Down Transaction List

Below the category table, show a selected-category transaction list.

Header:

- selected category name
- selected category total
- selected category transaction count

Table columns:

- Date
- Merchant
- Source
- Amount

Optional columns if space is acceptable:

- Raw name
- Normalized name

Rules:

- Show only current-year transactions for the selected category.
- Sort by date descending from the API.
- If no category is selected, show a small neutral message.
- If the selected category has no transactions, show a small neutral message.

Use `TransactionId` plus `AccountId` as the stable identity if `@key` is needed:

```razor
@key $"{context.AccountId}:{context.TransactionId}"
```

### 10d. Empty State

If the API returns no categories, show:

```text
No category spending found for {year}.
```

Do not show empty tables in this state.

### 10e. Error State

If API loading fails:

- set `_error = ex.Message`
- keep existing data if it exists
- let `ReportShell` render the alert

### 10f. Loading State

Set `_loading = true` before loading report data.

Set `_loading = false` in a `finally` block.

`ReportShell` owns the progress indicator.

## 11. UI DTO Records

Define page-local records in `ReportCategory.razor` matching the backend JSON:

```csharp
private record CategoryReportResult(
    int Year,
    int PreviousYear,
    decimal TotalSpend,
    decimal PreviousTotalSpend,
    decimal TotalChangeAmount,
    decimal TotalChangePercent,
    int TransactionCount,
    List<CategoryReportRow> Categories);

private record CategoryReportRow(
    string Category,
    decimal Total,
    int Count,
    decimal PctOfSpend,
    decimal TwelveMonthAverage,
    decimal TwelveMonthTotal,
    int TwelveMonthCount,
    decimal VsTwelveMonthAverageAmount,
    decimal VsTwelveMonthAveragePercent,
    decimal PreviousTotal,
    int PreviousCount,
    decimal ChangeAmount,
    decimal ChangePercent,
    List<CategoryTransactionRow> Transactions);

private record CategoryTransactionRow(
    string TransactionId,
    string AccountId,
    DateOnly Date,
    string Name,
    string RawName,
    string NormalizedName,
    decimal Amount,
    decimal? Debit,
    decimal? Credit,
    string Category,
    TransactionSource Source);
```

`TransactionSource` is available from the app namespace through `_Imports.razor`.

## 12. Page Loading Flow

Use this flow:

```csharp
protected override async Task OnInitializedAsync()
{
    await LoadYears();
    await LoadReport();
}

private async Task LoadYears()
{
    try
    {
        _availableYears = await Http.GetFromJsonAsync<List<int>>("api/settings/years")
                          ?? _availableYears;
        if (_availableYears.Count > 0)
            _year = _availableYears[0];
    }
    catch
    {
        _availableYears = Enumerable.Range(DateTime.Now.Year - 6, 7)
            .OrderByDescending(y => y)
            .ToList();
    }
}

private async Task OnYearChanged(int year)
{
    _year = year;
    await LoadReport();
}

private async Task LoadReport()
{
    _loading = true;
    _error = null;
    try
    {
        _report = await Http.GetFromJsonAsync<CategoryReportResult>(
            $"api/reports/category?year={_year}");
        _selectedCategory = _report?.Categories.FirstOrDefault();
    }
    catch (Exception ex)
    {
        _error = ex.Message;
    }
    finally
    {
        _loading = false;
    }
}
```

Initialize fields:

```csharp
private int _year = DateTime.Now.Year;
private List<int> _availableYears = new() { DateTime.Now.Year };
private bool _loading;
private string? _error;
private CategoryReportResult? _report;
private CategoryReportRow? _selectedCategory;
```

## 13. Styling Guidance

Keep styles local to `ReportCategory.razor` unless a reusable pattern already exists.

Use restrained utility classes and MudBlazor components. This page should feel like a financial reporting tool:

- compact
- readable
- table-first
- no oversized hero UI
- no decorative gradients
- no nested cards

Acceptable styling additions:

- `.report-summary-grid` with responsive columns
- `.metric-panel` for simple summary panels
- `.positive-change`, `.negative-change`, `.neutral-change`
- `.selected-category-row` if selection highlighting is easy to implement

If adding CSS, put it in a `<style>` block at the bottom of `ReportCategory.razor`.

## 14. Tests

Update `CashOut.Tests/ReportServiceTests.cs`.

### 14a. Existing Tests to Update

These tests currently expect `GetByCategory` to return `List<CategoryRow>`:

- `GetByCategory_GroupsAndSums`
- `GetByCategory_PctOfSpend_SumsToHundred`
- `GetByCategory_EmptyCategory_MarkedUncategorized`

Update them to access `result.Categories`.

Example:

```csharp
var result = await svc.GetByCategory(2025);
Assert.AreEqual(2, result.Categories.Count);
Assert.AreEqual("TRAVEL", result.Categories[0].Category);
```

### 14b. New Service Tests

Add these tests:

1. `GetByCategory_IncludesPreviousYearComparison`
   - 2025 category FOOD has total 200.
   - 2024 category FOOD has total 100.
   - Assert `PreviousTotal == 100`.
   - Assert `ChangeAmount == 100`.
   - Assert `ChangePercent == 100`.

2. `GetByCategory_PreviousZero_ReturnsZeroChangePercent`
   - 2025 category FOOD has total 50.
   - No 2024 FOOD transactions.
   - Assert `PreviousTotal == 0`.
   - Assert `ChangeAmount == 50`.
   - Assert `ChangePercent == 0`.

3. `GetByCategory_IncludesCurrentYearTransactionsForEachCategory`
   - Add two 2025 FOOD transactions and one 2025 TRAVEL transaction.
   - Assert FOOD row has exactly two transactions.
   - Assert all FOOD row transactions have `Category == "FOOD"`.

4. `GetByCategory_DoesNotIncludePreviousOnlyCategories`
   - Add 2024 TRAVEL transaction only.
   - Add 2025 FOOD transaction.
   - Assert report has FOOD only.

5. `GetByCategory_TotalsIncludeOnlyExpenses`
   - Add 2025 expense `Amount = 100`.
   - Add 2025 income/refund `Amount = -25`.
   - Assert total spend is 100.
   - Assert category count only includes the expense.

6. `GetByCategory_IncludesTwelveMonthRollingAverage`
   - Add one FOOD expense per month in 2025 for 12 months.
   - Use total spend of 1200.
   - Assert FOOD `TwelveMonthTotal == 1200`.
   - Assert FOOD `TwelveMonthAverage == 100`.
   - Assert FOOD `TwelveMonthCount == 12`.

7. `GetByCategory_ComputesVarianceFromTwelveMonthAverage`
   - Add FOOD expenses totaling 2400 across 2025.
   - Assert selected-year monthly average is 200.
   - Assert variance fields compare current average month to `TwelveMonthAverage`.
   - If the test data makes both values equal, assert amount `0` and percent `0`; otherwise assert the calculated delta.

### 14c. Optional UI Test

If the scaffold and app server are already testable in the current branch, add a Playwright UI test in `CashOut.Tests/UiTests.cs`:

```csharp
[TestMethod]
[TestCategory("UI")]
public async Task ReportsCategoryPage_ShowsSpendingByCategoryHeader()
{
    await Page.GotoAsync("http://localhost:8080/reports/category");
    var header = Page.GetByRole(AriaRole.Heading, new() { Name = "Spending by Category" });
    await Expect(header).ToBeVisibleAsync();
}
```

Do not make this UI test depend on seeded financial data unless the test harness already guarantees it.

## 15. Verification

Run:

```powershell
dotnet test
```

Manual checks:

- `/reports/category` renders "Spending by Category".
- Year picker loads from `api/settings/years`.
- Changing the year reloads the report.
- Summary totals match the category table totals.
- Category percentages add up to about 100, allowing one-decimal rounding drift.
- Category rows show 12-month rolling average spend and variance from that average.
- Clicking a category row updates the transaction drill-down.
- CSV export downloads from `api/reports/category?year={year}&format=csv`.
- Income/refund rows with `Amount < 0` are not counted as spending.

## 16. Files to Modify

Required:

- `CashOut/Services/ReportService.cs`
- `CashOut/Pages/ReportCategory.razor`
- `CashOut.Tests/ReportServiceTests.cs`

Usually unchanged:

- `CashOut/Controllers/ReportsController.cs`
- `CashOut/Shared/ReportShell.razor`
- `CashOut/Shared/MainLayout.razor`

Only modify the unchanged files if the scaffold has not yet been applied or the existing code does not match `docs/reports-ui-scaffold-spec.md`.

## 17. Acceptance Criteria

The implementation is complete when:

- The category report page is no longer a stub.
- The backend category endpoint returns `CategoryReportResult`.
- The page displays summary metrics, category totals, comparison values, and transaction drill-down.
- Category rows include 12-month rolling average fields and UI columns.
- CSV export includes comparison values.
- `dotnet test` passes.
- Existing report routes and settings year loading still work.
- No database migration is added.
