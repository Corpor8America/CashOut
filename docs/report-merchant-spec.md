# Spending by Merchant Report Implementation Spec

**Status:** Implementation-ready  
**Target route:** `/reports/merchant`  
**Target page:** `CashOut/Pages/ReportMerchant.razor`  
**Related scaffold:** `docs/reports-ui-scaffold-spec.md`  
**Related feature note:** `docs/report-features.md`

## 1. Goal

Implement the full Spending by Merchant report described in `docs/report-features.md` inside the report scaffold described in `docs/reports-ui-scaffold-spec.md`.

The report must show which merchants receive the most expense spending for a selected year, using the app's merchant normalization and alias mapping system.

The report must include:

- merchant totals
- transaction counts
- average spend per transaction
- primary category
- previous-year comparison
- normalized vs unmapped merchant identity
- transaction drill-down for a selected merchant
- configurable top-N limit
- CSV export

The scaffold spec already defines where this report lives in the UI. Do not redesign the report navigation shell. Replace the stub content in `CashOut/Pages/ReportMerchant.razor` with the implementation described here.

## 2. Important Existing Convention

`docs/report-features.md` says expense transactions are `amount < 0`, but the current CashOut model uses the opposite convention.

Use the convention implemented in `CashOut/Models/Transaction.cs`:

- `Amount > 0` means money leaving the account, so it is an expense.
- `Amount < 0` means money entering the account, so it is income, refund, or credit.
- `Debit` stores outflow amount.
- `Credit` stores inflow amount.

All Spending by Merchant backend queries must include only `Transaction.Amount > 0`.

## 3. Current State

The app currently has a basic merchant endpoint:

```http
GET /api/reports/merchants?year=2026&topN=10
GET /api/reports/merchants?year=2026&topN=10&format=csv
```

The current `ReportService.GetTopMerchants` returns:

```csharp
public record MerchantRow(
    string Name,
    decimal Total,
    int Count,
    decimal AvgPerVisit);
```

This is not enough for the full report because it lacks previous-period comparison, normalized identity details, categories, and transaction drill-down data.

The scaffold spec expects:

- `CashOut/Pages/ReportMerchant.razor` at route `/reports/merchant`
- `ReportShell` wrapping the report
- the page initially created as a stub

## 4. Merchant Identity Rules

Merchant grouping must respect the normalization system implemented by `MerchantNormalizationService`.

Relevant transaction fields:

- `AliasId`: populated when a transaction matched a `BusinessAlias`.
- `Alias`: navigation to the canonical merchant alias.
- `RawBusinessId`: populated for unaliased transactions that are tracked as raw businesses.
- `Name`: effective display merchant name. For aliased transactions, this is the alias name.
- `RawName`: original merchant string received from Plaid or CSV.
- `NormalizedName`: normalized merchant string produced by `MerchantNormalizationService.Normalize`.
- `Category`: effective transaction category. Alias category wins; otherwise `Unassigned`.

Grouping rules:

1. If `Transaction.AliasId` is not null, group by that alias id.
2. If `Transaction.AliasId` is null, group by `NormalizedName` when it is not blank.
3. If both `AliasId` and `NormalizedName` are missing, group by `Name`.
4. Aliased rows display `BusinessAlias.AliasName` when available, otherwise `Transaction.Name`.
5. Unmapped rows display the most common `Name` value in that group; if tied, use the alphabetically first name.
6. Unmapped rows must expose the normalized key so the user can tell when multiple raw names were grouped together.
7. Do not attempt to create or modify aliases from this report. Alias management remains in the Merchants page.

The report should include an `IsMapped` boolean:

- `true` for rows grouped by `AliasId`
- `false` for rows grouped by normalized/raw merchant identity

## 5. Implementation Approach

Extend the existing merchants endpoint and service method instead of adding a separate endpoint.

The existing endpoint path should remain:

```http
GET /api/reports/merchants?year={year}&topN={topN}
GET /api/reports/merchants?year={year}&topN={topN}&format=csv
```

Returning a richer JSON shape from this endpoint is acceptable because the old tab-based `Reports.razor` is replaced by the scaffold. The new merchant report page will be the only first-party consumer of the JSON merchants endpoint.

The CSV export should also be extended so it contains the comparison and identity fields.

## 6. Backend DTOs

Replace the current `MerchantRow` record at the bottom of `CashOut/Services/ReportService.cs` with richer records.

Use these names and fields:

```csharp
public record MerchantReportResult(
    int Year,
    int PreviousYear,
    int TopN,
    decimal TotalSpend,
    int TransactionCount,
    int MerchantCount,
    List<MerchantReportRow> Merchants);

public record MerchantReportRow(
    string MerchantKey,
    int? AliasId,
    int? RawBusinessId,
    string Name,
    string NormalizedName,
    bool IsMapped,
    string PrimaryCategory,
    decimal Total,
    int Count,
    decimal AvgPerVisit,
    decimal PctOfSpend,
    decimal PreviousTotal,
    int PreviousCount,
    decimal ChangeAmount,
    decimal ChangePercent,
    List<MerchantTransactionRow> Transactions);

public record MerchantTransactionRow(
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
    int? AliasId,
    int? RawBusinessId,
    TransactionSource Source);
```

Notes:

- `MerchantReportResult.Merchants` is ordered by `Total` descending.
- `MerchantReportResult.MerchantCount` is the number of merchant groups before applying `topN`.
- `MerchantReportResult.TotalSpend` is total selected-year expense spend across all merchants, not only the top-N rows.
- `PctOfSpend` uses total selected-year expense spend across all merchants as the denominator.
- `PreviousYear` is `Year - 1`.
- `PreviousTotal` and `PreviousCount` are computed for the same merchant key in the previous year.
- `ChangeAmount` is `Total - PreviousTotal`.
- `ChangePercent` is `(Total - PreviousTotal) / PreviousTotal * 100`, rounded to one decimal place. If previous total is zero, return `0`.
- `Transactions` contains only selected-year transactions for that merchant row.
- `Transactions` is ordered by `Date` descending, then `Amount` descending.

## 7. Backend Service Logic

Modify `ReportService.GetTopMerchants(int topN = 10, int? year = null)` so it returns `MerchantReportResult`.

Expected algorithm:

1. Resolve `y` from the method argument or `_settings.GetOutputYear()`.
2. Validate `topN`:
   - if `topN < 1`, use `10`
   - if `topN > 100`, clamp to `100`
3. Set `previousYear = y - 1`.
4. Load current-year expenses:
   - `Date.Year == y`
   - `Amount > 0`
   - include `Alias` if using EF navigation data
5. Load previous-year expenses:
   - `Date.Year == previousYear`
   - `Amount > 0`
   - include `Alias` if using EF navigation data
6. Build merchant groups for current-year expenses using the merchant identity rules from section 4.
7. Build merchant groups for previous-year expenses using the same key rules.
8. Compute selected-year grand total and transaction count.
9. For each current-year group:
   - compute total
   - compute count
   - compute average spend per visit
   - compute percent of total spend
   - compute previous total/count by matching `MerchantKey`
   - compute change amount and change percent
   - compute primary category
   - include selected-year transactions
10. Sort by `Total` descending.
11. Take the validated `topN`.
12. Return an empty `Merchants` list when there are no current-year expenses.

Use helpers inside `ReportService`:

```csharp
private static string MerchantKey(Transaction t)
{
    if (t.AliasId.HasValue) return $"alias:{t.AliasId.Value}";
    if (!string.IsNullOrWhiteSpace(t.NormalizedName)) return $"raw:{t.NormalizedName}";
    return $"name:{t.Name}";
}

private static string MerchantDisplayName(IEnumerable<Transaction> transactions)
{
    return transactions
        .GroupBy(t => string.IsNullOrWhiteSpace(t.Name) ? "(unknown merchant)" : t.Name)
        .OrderByDescending(g => g.Count())
        .ThenBy(g => g.Key)
        .First()
        .Key;
}

private static string PrimaryCategory(IEnumerable<Transaction> transactions)
{
    return transactions
        .GroupBy(t => string.IsNullOrWhiteSpace(t.Category) ? "Unassigned" : t.Category)
        .OrderByDescending(g => g.Sum(t => t.Amount))
        .ThenBy(g => g.Key)
        .First()
        .Key;
}

private static decimal Percent(decimal numerator, decimal denominator) =>
    denominator == 0 ? 0 : Math.Round(numerator / denominator * 100m, 1);

private static decimal ChangePercent(decimal current, decimal previous) =>
    previous == 0 ? 0 : Math.Round((current - previous) / previous * 100m, 1);
```

If the category report implementation already added `Percent` and `ChangePercent`, reuse those helpers instead of duplicating names.

Keep `GetExpenses(int year)` as the shared expense query helper, but confirm it still filters `Amount > 0`. If the merchant report needs alias navigation data, either update `GetExpenses` to include `Alias` or load merchant report transactions with a report-specific query.

## 8. CSV Export

Update `ReportService.MerchantsCsv(int topN = 10, int? year = null)` to use the richer result.

CSV headers:

```csv
Merchant,IsMapped,AliasId,RawBusinessId,NormalizedName,PrimaryCategory,Total,PctOfSpend,Transactions,AvgPerVisit,PreviousTotal,PreviousTransactions,ChangeAmount,ChangePercent
```

One row per merchant in the top-N result. Use invariant culture for decimal values. Keep using the existing `Esc` helper for string fields.

CSV export endpoint remains:

```http
GET /api/reports/merchants?year={year}&topN={topN}&format=csv
```

The downloaded filename can remain `merchants.csv`.

## 9. Controller

No new controller route is required.

Keep `ReportsController.Merchants` as:

```csharp
[HttpGet("merchants")]
public async Task<IActionResult> Merchants(
    [FromQuery] int topN = 10, [FromQuery] int? year = null,
    [FromQuery] string? format = null)
{
    if (format == "csv")
        return File(await _reports.MerchantsCsv(topN, year), "text/csv", "merchants.csv");
    return Ok(await _reports.GetTopMerchants(topN, year));
}
```

Only the service return type changes.

## 10. UI Page

Implement `CashOut/Pages/ReportMerchant.razor`.

The page must:

- use `@page "/reports/merchant"`
- inject `HttpClient`
- render inside `<ReportShell>`
- load available years from `api/settings/years`
- load report data from `api/reports/merchants?year={_year}&topN={_topN}`
- remove `IsStub="true"`
- show loading and error states through `ReportShell`
- expose CSV export through `ExportHref="@($"api/reports/merchants?year={_year}&topN={_topN}&format=csv")"`

Suggested top-level page structure:

```razor
@page "/reports/merchant"
@inject HttpClient Http

<ReportShell Title="Spending by Merchant"
             Year="_year"
             AvailableYears="_availableYears"
             OnYearChanged="OnYearChanged"
             Loading="_loading"
             Error="_error"
             ExportHref="@($"api/reports/merchants?year={_year}&topN={_topN}&format=csv")">
    <MudPaper Class="pa-4 mb-4" Elevation="0">
        <MudStack Row="true" AlignItems="AlignItems.Center" Spacing="3">
            <MudNumericField @bind-Value="_topN"
                             Label="Top N"
                             Min="1"
                             Max="100"
                             Variant="Variant.Outlined"
                             Margin="Margin.Dense"
                             Style="width:120px" />
            <MudButton Variant="Variant.Filled"
                       Color="Color.Primary"
                       OnClick="LoadReport"
                       Disabled="@_loading">
                Apply
            </MudButton>
        </MudStack>
    </MudPaper>

    @if (_report is null || _report.Merchants.Count == 0)
    {
        <MudAlert Severity="Severity.Info" Variant="Variant.Outlined">
            No merchant spending found for @_year.
        </MudAlert>
    }
    else
    {
        <!-- summary panels, merchant table, and drill-down panel go here -->
    }
</ReportShell>
```

## 11. UI Content Requirements

### 11a. Top-N Control

Render a compact Top N control above the report content:

- numeric field bound to `_topN`
- min `1`
- max `100`
- default `10`
- Apply button reloads the report

Do not reload on every keystroke. Reload on Apply and year change.

### 11b. Summary Metrics

At the top of the report body, show three compact summary panels:

- Total spending: `_report.TotalSpend.ToString("C")`
- Transactions: `_report.TransactionCount`
- Merchants: show `_report.Merchants.Count` and `_report.MerchantCount`
  - example: `10 of 42`

Do not make a marketing-style hero section. This is an operational report page.

### 11c. Merchant Table

Render a `MudTable` over `_report.Merchants`.

Columns:

- Merchant
- Category
- Total
- Percent of spend
- Transactions
- Avg/visit
- Previous year
- Change
- Status

Expected formatting:

- currency values use `.ToString("C")`
- percent values show one decimal place and a percent sign
- count values are right-aligned
- money and percent columns are right-aligned
- table is dense and hoverable
- merchant rows are sorted by `Total` descending from the API
- `Status` is `Mapped` when `IsMapped == true`, otherwise `Unmapped`

For the merchant name cell:

- show `Name` as the primary text
- for unmapped rows, show `NormalizedName` as a smaller secondary line when it differs from `Name`
- do not link to merchant management in this implementation unless an existing route already supports editing a specific alias or raw business

The selected row should drive the drill-down panel.

Implementation detail:

- Track `_selectedMerchant` as `MerchantReportRow?`.
- On row click, set `_selectedMerchant = context`.
- When data loads, default `_selectedMerchant` to the first merchant, if present.

MudBlazor can use `RowClick`:

```razor
<MudTable Items="@_report.Merchants"
          Dense="true"
          Hover="true"
          Breakpoint="Breakpoint.Sm"
          Elevation="0"
          RowClick="OnMerchantRowClick">
```

Then:

```csharp
private void OnMerchantRowClick(TableRowClickEventArgs<MerchantReportRow> args)
{
    _selectedMerchant = args.Item;
}
```

### 11d. Drill-Down Transaction List

Below the merchant table, show a selected-merchant transaction list.

Header:

- selected merchant name
- selected merchant total
- selected merchant transaction count
- mapped/unmapped status

Table columns:

- Date
- Merchant
- Raw name
- Category
- Source
- Amount

Rules:

- Show only current-year transactions for the selected merchant.
- Sort by date descending from the API.
- If no merchant is selected, show a small neutral message.
- If the selected merchant has no transactions, show a small neutral message.

Use `TransactionId` plus `AccountId` as the stable identity if `@key` is needed:

```razor
@key $"{context.AccountId}:{context.TransactionId}"
```

### 11e. Empty State

If the API returns no merchants, show:

```text
No merchant spending found for {year}.
```

Do not show empty tables in this state.

### 11f. Error State

If API loading fails:

- set `_error = ex.Message`
- keep existing data if it exists
- let `ReportShell` render the alert

### 11g. Loading State

Set `_loading = true` before loading report data.

Set `_loading = false` in a `finally` block.

`ReportShell` owns the progress indicator.

## 12. UI DTO Records

Define page-local records in `ReportMerchant.razor` matching the backend JSON:

```csharp
private record MerchantReportResult(
    int Year,
    int PreviousYear,
    int TopN,
    decimal TotalSpend,
    int TransactionCount,
    int MerchantCount,
    List<MerchantReportRow> Merchants);

private record MerchantReportRow(
    string MerchantKey,
    int? AliasId,
    int? RawBusinessId,
    string Name,
    string NormalizedName,
    bool IsMapped,
    string PrimaryCategory,
    decimal Total,
    int Count,
    decimal AvgPerVisit,
    decimal PctOfSpend,
    decimal PreviousTotal,
    int PreviousCount,
    decimal ChangeAmount,
    decimal ChangePercent,
    List<MerchantTransactionRow> Transactions);

private record MerchantTransactionRow(
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
    int? AliasId,
    int? RawBusinessId,
    TransactionSource Source);
```

`TransactionSource` is available from the app namespace through `_Imports.razor`.

## 13. Page Loading Flow

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
        var topN = Math.Clamp(_topN, 1, 100);
        _topN = topN;
        _report = await Http.GetFromJsonAsync<MerchantReportResult>(
            $"api/reports/merchants?year={_year}&topN={topN}");
        _selectedMerchant = _report?.Merchants.FirstOrDefault();
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
private int _topN = 10;
private List<int> _availableYears = new() { DateTime.Now.Year };
private bool _loading;
private string? _error;
private MerchantReportResult? _report;
private MerchantReportRow? _selectedMerchant;
```

## 14. Styling Guidance

Keep styles local to `ReportMerchant.razor` unless a reusable pattern already exists.

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
- `.muted-subtext` for normalized names
- `.status-mapped`, `.status-unmapped`

If adding CSS, put it in a `<style>` block at the bottom of `ReportMerchant.razor`.

## 15. Tests

Update `CashOut.Tests/ReportServiceTests.cs`.

### 15a. Existing Tests to Update

These tests currently expect `GetTopMerchants` to return `List<MerchantRow>`:

- `GetTopMerchants_OrderedByTotalDesc_AndRespectsTopN`
- `GetTopMerchants_AvgPerVisit_IsCorrect`

Update them to access `result.Merchants`.

Example:

```csharp
var result = await svc.GetTopMerchants(topN: 2, year: 2025);
Assert.AreEqual(2, result.Merchants.Count);
Assert.AreEqual("Airline", result.Merchants[0].Name);
```

### 15b. New Service Tests

Add these tests:

1. `GetTopMerchants_GroupsAliasedTransactionsByAlias`
   - Create a `BusinessAlias` named `Amazon`.
   - Add two 2025 transactions with `AliasId` pointing to that alias and different raw names.
   - Assert one merchant row is returned.
   - Assert `Name == "Amazon"`.
   - Assert `IsMapped == true`.
   - Assert total and count include both transactions.

2. `GetTopMerchants_GroupsUnmappedTransactionsByNormalizedName`
   - Add two 2025 transactions with no alias, same `NormalizedName`, and different raw/name values.
   - Assert one merchant row is returned.
   - Assert `IsMapped == false`.
   - Assert count is two.

3. `GetTopMerchants_IncludesPrimaryCategory`
   - Add two transactions for one merchant.
   - Category `GROCERIES` has higher total than `SHOPPING`.
   - Assert `PrimaryCategory == "GROCERIES"`.

4. `GetTopMerchants_IncludesPreviousYearComparison`
   - 2025 merchant total is 200.
   - 2024 same merchant key total is 100.
   - Assert `PreviousTotal == 100`.
   - Assert `ChangeAmount == 100`.
   - Assert `ChangePercent == 100`.

5. `GetTopMerchants_PreviousZero_ReturnsZeroChangePercent`
   - 2025 merchant total is 50.
   - No previous-year transactions for that merchant.
   - Assert `PreviousTotal == 0`.
   - Assert `ChangeAmount == 50`.
   - Assert `ChangePercent == 0`.

6. `GetTopMerchants_IncludesCurrentYearTransactionsForEachMerchant`
   - Add two 2025 transactions for one merchant and one for another.
   - Assert selected merchant row contains only its two transactions.

7. `GetTopMerchants_TotalsIncludeOnlyExpenses`
   - Add 2025 expense `Amount = 100`.
   - Add 2025 income/refund `Amount = -25`.
   - Assert total spend is 100.
   - Assert merchant count only includes the expense.

8. `GetTopMerchants_ClampsTopN`
   - Pass `topN: 0` and assert result uses `TopN == 10`.
   - Pass `topN: 500` and assert result uses `TopN == 100`.

### 15c. Optional UI Test

If the scaffold and app server are already testable in the current branch, add a Playwright UI test in `CashOut.Tests/UiTests.cs`:

```csharp
[TestMethod]
[TestCategory("UI")]
public async Task ReportsMerchantPage_ShowsSpendingByMerchantHeader()
{
    await Page.GotoAsync("http://localhost:8080/reports/merchant");
    var header = Page.GetByRole(AriaRole.Heading, new() { Name = "Spending by Merchant" });
    await Expect(header).ToBeVisibleAsync();
}
```

Do not make this UI test depend on seeded financial data unless the test harness already guarantees it.

## 16. Verification

Run:

```powershell
dotnet test
```

Manual checks:

- `/reports/merchant` renders "Spending by Merchant".
- Year picker loads from `api/settings/years`.
- Top N defaults to 10.
- Changing the year reloads the report.
- Clicking Apply reloads the report with the requested Top N.
- Summary totals match the full report total returned by the API.
- Merchant rows are sorted by selected-year total descending.
- Aliased merchant transactions are grouped under the alias display name.
- Unmapped merchant transactions with the same normalized name are grouped together.
- Clicking a merchant row updates the transaction drill-down.
- CSV export downloads from `api/reports/merchants?year={year}&topN={topN}&format=csv`.
- Income/refund rows with `Amount < 0` are not counted as spending.

## 17. Files to Modify

Required:

- `CashOut/Services/ReportService.cs`
- `CashOut/Pages/ReportMerchant.razor`
- `CashOut.Tests/ReportServiceTests.cs`

Usually unchanged:

- `CashOut/Controllers/ReportsController.cs`
- `CashOut/Shared/ReportShell.razor`
- `CashOut/Shared/MainLayout.razor`

Only modify the unchanged files if the scaffold has not yet been applied or the existing code does not match `docs/reports-ui-scaffold-spec.md`.

No database migration is required.

## 18. Acceptance Criteria

The implementation is complete when:

- The merchant report page is no longer a stub.
- The backend merchants endpoint returns `MerchantReportResult`.
- The report groups mapped transactions by alias and unmapped transactions by normalized merchant name.
- The page displays summary metrics, merchant totals, categories, comparison values, mapped/unmapped status, and transaction drill-down.
- The Top N control works and clamps to `1..100`.
- CSV export includes identity and comparison values.
- `dotnet test` passes.
- Existing report routes and settings year loading still work.
- No database migration is added.
