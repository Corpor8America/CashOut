# Income Report Implementation Spec

**Status:** Implementation-ready  
**Target route:** `/reports/income`  
**Target page:** `CashOut/Pages/ReportIncome.razor`  
**Related scaffold:** `docs/reports-ui-scaffold-spec.md`  
**Related feature note:** `docs/report-features.md`

## 1. Goal

Implement the full Income report described in `docs/report-features.md` inside the report scaffold described in `docs/reports-ui-scaffold-spec.md`.

The report must summarize money entering accounts for a selected year, grouped by income source, with:

- income totals
- percent of total income
- transaction counts
- average income transaction amount
- previous-year comparison
- category visibility
- transaction drill-down for a selected income source
- CSV export

The scaffold spec already defines where this report lives in the UI. Do not redesign the report navigation shell. Replace the stub content in `CashOut/Pages/ReportIncome.razor` with the implementation described here.

## 2. Important Existing Convention

`docs/report-features.md` says income transactions are `amount > 0`, but the current CashOut model uses the opposite convention.

Use the convention implemented in `CashOut/Models/Transaction.cs`:

- `Amount > 0` means money leaving the account, so it is an expense.
- `Amount < 0` means money entering the account, so it is income, refund, or credit.
- `Debit` stores outflow amount.
- `Credit` stores inflow amount.

All Income report backend queries must include only `Transaction.Amount < 0`.

Displayed income totals must be positive values. Internally, income transactions have negative `Amount`; convert them with `Math.Abs(t.Amount)` when summing or displaying income.

## 3. Current State

There is no existing Income report endpoint.

The app currently has:

- `ReportsController` under `CashOut/Controllers/ReportsController.cs`
- `ReportService` under `CashOut/Services/ReportService.cs`
- a scaffolded Income page target at `CashOut/Pages/ReportIncome.razor`
- report shell expectations in `docs/reports-ui-scaffold-spec.md`

The scaffold spec expects:

- route `/reports/income`
- page file `CashOut/Pages/ReportIncome.razor`
- `ReportShell` wrapping the report
- initial page may be a stub

This spec adds the backend endpoint and replaces the page stub with the full report.

## 4. Income Source Grouping Rules

The report should group income by income source. In this app, the most useful source is the effective merchant/name field, because payroll providers, transfers, refunds, interest, and external deposits arrive as transactions with merchant-like names.

Grouping rules:

1. If `Transaction.AliasId` is not null, group by that alias id.
2. If `Transaction.AliasId` is null and `NormalizedName` is not blank, group by `NormalizedName`.
3. If both `AliasId` and `NormalizedName` are missing, group by `Name`.
4. Aliased rows display `BusinessAlias.AliasName` when available, otherwise `Transaction.Name`.
5. Unmapped rows display the most common `Name` value in that group; if tied, use the alphabetically first name.
6. Include the source's primary category using `Transaction.Category`.
7. Do not create or modify aliases from this report. Alias management remains in the Merchants page.

This intentionally mirrors the merchant report grouping rules so normalized names remain consistent across reports.

## 5. Backend Endpoint

Add a new endpoint to `ReportsController`:

```http
GET /api/reports/income?year={year}
GET /api/reports/income?year={year}&format=csv
```

Controller implementation:

```csharp
[HttpGet("income")]
public async Task<IActionResult> Income(
    [FromQuery] int? year, [FromQuery] string? format)
{
    if (format == "csv")
        return File(await _reports.IncomeCsv(year), "text/csv", "income.csv");
    return Ok(await _reports.GetIncome(year));
}
```

No existing endpoint should be removed.

## 6. Backend DTOs

Add these records at the bottom of `CashOut/Services/ReportService.cs` with the other report records:

```csharp
public record IncomeReportResult(
    int Year,
    int PreviousYear,
    decimal TotalIncome,
    decimal PreviousTotalIncome,
    decimal TotalChangeAmount,
    decimal TotalChangePercent,
    int TransactionCount,
    int SourceCount,
    List<IncomeReportRow> Sources);

public record IncomeReportRow(
    string SourceKey,
    int? AliasId,
    int? RawBusinessId,
    string Name,
    string NormalizedName,
    bool IsMapped,
    string PrimaryCategory,
    decimal Total,
    int Count,
    decimal AvgAmount,
    decimal PctOfIncome,
    decimal PreviousTotal,
    int PreviousCount,
    decimal ChangeAmount,
    decimal ChangePercent,
    List<IncomeTransactionRow> Transactions);

public record IncomeTransactionRow(
    string TransactionId,
    string AccountId,
    DateOnly Date,
    string Name,
    string RawName,
    string NormalizedName,
    decimal Amount,
    decimal DisplayAmount,
    decimal? Debit,
    decimal? Credit,
    string Category,
    int? AliasId,
    int? RawBusinessId,
    TransactionSource Source);
```

Notes:

- `Amount` keeps the stored transaction amount, which is negative for income.
- `DisplayAmount` is `Math.Abs(Amount)` and is what the UI should render as money received.
- `Total`, `PreviousTotal`, `AvgAmount`, and all income summary values are positive numbers.
- `Sources` is ordered by `Total` descending.
- `SourceCount` is the number of income source groups before any UI filtering. There is no top-N limit in this report.
- `PreviousYear` is `Year - 1`.
- `TotalChangeAmount` is `TotalIncome - PreviousTotalIncome`.
- `TotalChangePercent` is `(TotalIncome - PreviousTotalIncome) / PreviousTotalIncome * 100`, rounded to one decimal place. If previous total is zero, return `0`.
- The same change calculation applies to each income source row.
- `Transactions` contains only selected-year income transactions for that row.
- `Transactions` is ordered by `Date` descending, then `DisplayAmount` descending.

## 7. Backend Service Logic

Add `ReportService.GetIncome(int? year = null)` returning `IncomeReportResult`.

Add a shared income query helper near `GetExpenses`:

```csharp
/// <summary>
/// Returns income transactions for the year.
/// Amount < 0 means Credit > Debit (net inflow) in the current CashOut model.
/// </summary>
private async Task<List<Transaction>> GetIncomeTransactions(int year)
{
    return await _db.Transactions
        .Include(t => t.Alias)
        .Where(t => t.Date.Year == year && t.Amount < 0)
        .ToListAsync();
}
```

Expected `GetIncome` algorithm:

1. Resolve `y` from the method argument or `_settings.GetOutputYear()`.
2. Set `previousYear = y - 1`.
3. Load selected-year income transactions with `Amount < 0`.
4. Load previous-year income transactions with `Amount < 0`.
5. Build source groups for selected-year income using the source grouping rules from section 4.
6. Build source groups for previous-year income using the same source key rules.
7. Compute selected-year total income as the sum of `Math.Abs(t.Amount)`.
8. Compute previous-year total income as the sum of `Math.Abs(t.Amount)`.
9. For each selected-year source group:
   - compute positive total income
   - compute transaction count
   - compute average income amount
   - compute percent of total income
   - compute previous total/count by matching `SourceKey`
   - compute change amount and change percent
   - compute primary category
   - include selected-year transactions with both stored `Amount` and positive `DisplayAmount`
10. Sort source rows by `Total` descending.
11. Return an empty `Sources` list when there are no selected-year income transactions.

Use helpers inside `ReportService`:

```csharp
private static string SourceKey(Transaction t)
{
    if (t.AliasId.HasValue) return $"alias:{t.AliasId.Value}";
    if (!string.IsNullOrWhiteSpace(t.NormalizedName)) return $"raw:{t.NormalizedName}";
    return $"name:{t.Name}";
}

private static string SourceDisplayName(IEnumerable<Transaction> transactions)
{
    return transactions
        .GroupBy(t => string.IsNullOrWhiteSpace(t.Name) ? "(unknown source)" : t.Name)
        .OrderByDescending(g => g.Count())
        .ThenBy(g => g.Key)
        .First()
        .Key;
}

private static string PrimaryCategory(IEnumerable<Transaction> transactions)
{
    return transactions
        .GroupBy(t => string.IsNullOrWhiteSpace(t.Category) ? "Unassigned" : t.Category)
        .OrderByDescending(g => g.Sum(t => Math.Abs(t.Amount)))
        .ThenBy(g => g.Key)
        .First()
        .Key;
}

private static decimal PositiveAmount(Transaction t) => Math.Abs(t.Amount);

private static decimal Percent(decimal numerator, decimal denominator) =>
    denominator == 0 ? 0 : Math.Round(numerator / denominator * 100m, 1);

private static decimal ChangePercent(decimal current, decimal previous) =>
    previous == 0 ? 0 : Math.Round((current - previous) / previous * 100m, 1);
```

If the category or merchant report implementation already added `Percent`, `ChangePercent`, or a compatible `PrimaryCategory`, reuse those helpers instead of duplicating names.

## 8. CSV Export

Add `ReportService.IncomeCsv(int? year = null)`.

CSV headers:

```csv
Source,IsMapped,AliasId,RawBusinessId,NormalizedName,PrimaryCategory,Total,PctOfIncome,Transactions,AvgAmount,PreviousTotal,PreviousTransactions,ChangeAmount,ChangePercent
```

One row per income source. Use invariant culture for decimal values. Keep using the existing `Esc` helper for string fields.

Implementation outline:

```csharp
public async Task<byte[]> IncomeCsv(int? year = null)
{
    var result = await GetIncome(year);
    var sb = new StringBuilder("Source,IsMapped,AliasId,RawBusinessId,NormalizedName,PrimaryCategory,Total,PctOfIncome,Transactions,AvgAmount,PreviousTotal,PreviousTransactions,ChangeAmount,ChangePercent\n");
    foreach (var r in result.Sources)
    {
        sb.AppendLine(
            $"{Esc(r.Name)},{r.IsMapped},{r.AliasId?.ToString() ?? ""},{r.RawBusinessId?.ToString() ?? ""}," +
            $"{Esc(r.NormalizedName)},{Esc(r.PrimaryCategory)}," +
            $"{r.Total.ToString(CultureInfo.InvariantCulture)}," +
            $"{r.PctOfIncome.ToString(CultureInfo.InvariantCulture)}," +
            $"{r.Count}," +
            $"{r.AvgAmount.ToString(CultureInfo.InvariantCulture)}," +
            $"{r.PreviousTotal.ToString(CultureInfo.InvariantCulture)}," +
            $"{r.PreviousCount}," +
            $"{r.ChangeAmount.ToString(CultureInfo.InvariantCulture)}," +
            $"{r.ChangePercent.ToString(CultureInfo.InvariantCulture)}");
    }
    return Encoding.UTF8.GetBytes(sb.ToString());
}
```

## 9. UI Page

Implement `CashOut/Pages/ReportIncome.razor`.

The page must:

- use `@page "/reports/income"`
- inject `HttpClient`
- render inside `<ReportShell>`
- load available years from `api/settings/years`
- load report data from `api/reports/income?year={_year}`
- remove `IsStub="true"`
- show loading and error states through `ReportShell`
- expose CSV export through `ExportHref="@($"api/reports/income?year={_year}&format=csv")"`

Suggested top-level page structure:

```razor
@page "/reports/income"
@inject HttpClient Http

<ReportShell Title="Income"
             Year="_year"
             AvailableYears="_availableYears"
             OnYearChanged="OnYearChanged"
             Loading="_loading"
             Error="_error"
             ExportHref="@($"api/reports/income?year={_year}&format=csv")">
    @if (_report is null || _report.Sources.Count == 0)
    {
        <MudAlert Severity="Severity.Info" Variant="Variant.Outlined">
            No income found for @_year.
        </MudAlert>
    }
    else
    {
        <!-- summary panels, income source table, and drill-down panel go here -->
    }
</ReportShell>
```

## 10. UI Content Requirements

### 10a. Summary Metrics

At the top of the report body, show three compact summary panels:

- Total income: `_report.TotalIncome.ToString("C")`
- Transactions: `_report.TransactionCount`
- Change vs previous year:
  - amount: `_report.TotalChangeAmount.ToString("C")`
  - percent: `_report.TotalChangePercent`

Use semantic coloring:

- Income increase (`TotalChangeAmount > 0`) should be visually positive.
- Income decrease (`TotalChangeAmount < 0`) should be visually negative or warning-colored.
- Zero change should be neutral.

Do not make a marketing-style hero section. This is an operational report page.

### 10b. Income Source Table

Render a `MudTable` over `_report.Sources`.

Columns:

- Source
- Category
- Total
- Percent of income
- Transactions
- Avg amount
- Previous year
- Change
- Status

Expected formatting:

- currency values use `.ToString("C")`
- percent values show one decimal place and a percent sign
- count values are right-aligned
- money and percent columns are right-aligned
- table is dense and hoverable
- rows are sorted by `Total` descending from the API
- `Status` is `Mapped` when `IsMapped == true`, otherwise `Unmapped`

For the source name cell:

- show `Name` as the primary text
- for unmapped rows, show `NormalizedName` as a smaller secondary line when it differs from `Name`

The selected row should drive the drill-down panel.

Implementation detail:

- Track `_selectedSource` as `IncomeReportRow?`.
- On row click, set `_selectedSource = context`.
- When data loads, default `_selectedSource` to the first source, if present.

MudBlazor can use `RowClick`:

```razor
<MudTable Items="@_report.Sources"
          Dense="true"
          Hover="true"
          Breakpoint="Breakpoint.Sm"
          Elevation="0"
          RowClick="OnSourceRowClick">
```

Then:

```csharp
private void OnSourceRowClick(TableRowClickEventArgs<IncomeReportRow> args)
{
    _selectedSource = args.Item;
}
```

### 10c. Drill-Down Transaction List

Below the income source table, show a selected-source transaction list.

Header:

- selected source name
- selected source total
- selected source transaction count
- mapped/unmapped status

Table columns:

- Date
- Source
- Raw name
- Category
- Source type
- Amount

Rules:

- Show only selected-year income transactions for the selected source.
- Render `DisplayAmount`, not stored `Amount`, in the Amount column.
- Sort by date descending from the API.
- If no source is selected, show a small neutral message.
- If the selected source has no transactions, show a small neutral message.

Use `TransactionId` plus `AccountId` as the stable identity if `@key` is needed:

```razor
@key $"{context.AccountId}:{context.TransactionId}"
```

### 10d. Empty State

If the API returns no sources, show:

```text
No income found for {year}.
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

Define page-local records in `ReportIncome.razor` matching the backend JSON:

```csharp
private record IncomeReportResult(
    int Year,
    int PreviousYear,
    decimal TotalIncome,
    decimal PreviousTotalIncome,
    decimal TotalChangeAmount,
    decimal TotalChangePercent,
    int TransactionCount,
    int SourceCount,
    List<IncomeReportRow> Sources);

private record IncomeReportRow(
    string SourceKey,
    int? AliasId,
    int? RawBusinessId,
    string Name,
    string NormalizedName,
    bool IsMapped,
    string PrimaryCategory,
    decimal Total,
    int Count,
    decimal AvgAmount,
    decimal PctOfIncome,
    decimal PreviousTotal,
    int PreviousCount,
    decimal ChangeAmount,
    decimal ChangePercent,
    List<IncomeTransactionRow> Transactions);

private record IncomeTransactionRow(
    string TransactionId,
    string AccountId,
    DateOnly Date,
    string Name,
    string RawName,
    string NormalizedName,
    decimal Amount,
    decimal DisplayAmount,
    decimal? Debit,
    decimal? Credit,
    string Category,
    int? AliasId,
    int? RawBusinessId,
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
        _report = await Http.GetFromJsonAsync<IncomeReportResult>(
            $"api/reports/income?year={_year}");
        _selectedSource = _report?.Sources.FirstOrDefault();
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
private IncomeReportResult? _report;
private IncomeReportRow? _selectedSource;
```

## 13. Styling Guidance

Keep styles local to `ReportIncome.razor` unless a reusable pattern already exists.

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

If adding CSS, put it in a `<style>` block at the bottom of `ReportIncome.razor`.

## 14. Tests

Update `CashOut.Tests/ReportServiceTests.cs`.

### 14a. New Service Tests

Add these tests:

1. `GetIncome_GroupsByIncomeSource`
   - Add two 2025 income transactions with the same normalized source.
   - Add one 2025 income transaction for a different source.
   - Assert two source rows are returned.
   - Assert the first source total includes both matching transactions.

2. `GetIncome_UsesPositiveDisplayTotals`
   - Add a 2025 transaction with `Amount = -100`.
   - Assert `TotalIncome == 100`.
   - Assert source `Total == 100`.
   - Assert transaction `Amount == -100`.
   - Assert transaction `DisplayAmount == 100`.

3. `GetIncome_ExcludesExpenses`
   - Add 2025 income `Amount = -100`.
   - Add 2025 expense `Amount = 50`.
   - Assert total income is 100.
   - Assert transaction count is 1.

4. `GetIncome_IncludesPreviousYearComparison`
   - 2025 source total is 200.
   - 2024 same source key total is 100.
   - Assert `PreviousTotal == 100`.
   - Assert `ChangeAmount == 100`.
   - Assert `ChangePercent == 100`.

5. `GetIncome_PreviousZero_ReturnsZeroChangePercent`
   - 2025 source total is 50.
   - No previous-year transactions for that source.
   - Assert `PreviousTotal == 0`.
   - Assert `ChangeAmount == 50`.
   - Assert `ChangePercent == 0`.

6. `GetIncome_IncludesPrimaryCategory`
   - Add two transactions for one source.
   - Category `PAYROLL` has higher total than `REFUND`.
   - Assert `PrimaryCategory == "PAYROLL"`.

7. `GetIncome_GroupsAliasedTransactionsByAlias`
   - Create a `BusinessAlias` named `Employer`.
   - Add two 2025 income transactions with `AliasId` pointing to that alias and different raw names.
   - Assert one source row is returned.
   - Assert `Name == "Employer"`.
   - Assert `IsMapped == true`.

8. `IncomeCsv_IncludesExpectedHeaders`
   - Call `IncomeCsv(2025)`.
   - Decode UTF-8.
   - Assert the header line contains `Source,IsMapped,AliasId`.

### 14b. Optional UI Test

If the scaffold and app server are already testable in the current branch, add a Playwright UI test in `CashOut.Tests/UiTests.cs`:

```csharp
[TestMethod]
[TestCategory("UI")]
public async Task ReportsIncomePage_ShowsIncomeHeader()
{
    await Page.GotoAsync("http://localhost:8080/reports/income");
    var header = Page.GetByRole(AriaRole.Heading, new() { Name = "Income" });
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

- `/reports/income` renders "Income".
- Year picker loads from `api/settings/years`.
- Changing the year reloads the report.
- Summary total income matches the source table totals.
- Source percentages add up to about 100, allowing one-decimal rounding drift.
- Stored negative income amounts render as positive displayed income.
- Expense rows with `Amount > 0` are not counted as income.
- Clicking an income source row updates the transaction drill-down.
- CSV export downloads from `api/reports/income?year={year}&format=csv`.

## 16. Files to Modify

Required:

- `CashOut/Controllers/ReportsController.cs`
- `CashOut/Services/ReportService.cs`
- `CashOut/Pages/ReportIncome.razor`
- `CashOut.Tests/ReportServiceTests.cs`

Usually unchanged:

- `CashOut/Shared/ReportShell.razor`
- `CashOut/Shared/MainLayout.razor`

Only modify the unchanged files if the scaffold has not yet been applied or the existing code does not match `docs/reports-ui-scaffold-spec.md`.

No database migration is required.

## 17. Acceptance Criteria

The implementation is complete when:

- The income report page is no longer a stub.
- `GET /api/reports/income?year={year}` returns `IncomeReportResult`.
- `GET /api/reports/income?year={year}&format=csv` downloads CSV.
- The backend includes only `Amount < 0` transactions as income.
- The UI displays income as positive values.
- The page displays summary metrics, income source totals, categories, comparison values, mapped/unmapped status, and transaction drill-down.
- CSV export includes identity and comparison values.
- `dotnet test` passes.
- Existing report routes and settings year loading still work.
- No database migration is added.
