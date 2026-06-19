# Net Cash Flow Report Implementation Spec

**Status:** Implementation-ready  
**Target route:** `/reports/cashflow`  
**Target page:** `CashOut/Pages/ReportCashFlow.razor`  
**Related scaffold:** `docs/reports-ui-scaffold-spec.md`  
**Related feature note:** `docs/report-features.md`

## 1. Goal

Implement the full Net Cash Flow report described in `docs/report-features.md` inside the report scaffold described in `docs/reports-ui-scaffold-spec.md`.

The report must show whether the user brought in more money than they spent during a selected year, with monthly cash flow rows and year-level summary metrics.

The report must include:

- total income
- total expenses
- net cash flow
- monthly income, expenses, and net cash flow
- rolling average net cash flow
- previous-year comparison
- transaction counts
- month drill-down transaction list
- CSV export

The scaffold spec already defines where this report lives in the UI. Do not redesign the report navigation shell. Replace the stub content in `CashOut/Pages/ReportCashFlow.razor` with the implementation described here.

## 2. Important Existing Convention

`docs/report-features.md` uses the opposite sign convention from the current CashOut model.

Use the convention implemented in `CashOut/Models/Transaction.cs`:

- `Amount > 0` means money leaving the account, so it is an expense.
- `Amount < 0` means money entering the account, so it is income, refund, or credit.
- `Debit` stores outflow amount.
- `Credit` stores inflow amount.

For this report:

- income is the sum of `Math.Abs(Amount)` for transactions where `Amount < 0`
- expenses are the sum of `Amount` for transactions where `Amount > 0`
- net cash flow is `income - expenses`

Positive net cash flow means the user kept more money than they spent. Negative net cash flow means expenses exceeded income.

## 3. Current State

There is no full Net Cash Flow endpoint.

There is an existing monthly endpoint:

```http
GET /api/reports/monthly?year={year}
```

That endpoint currently uses `ReportService.GetMonthly`, which calls `GetExpenses` and returns only monthly expense totals. It is not sufficient for Net Cash Flow because it does not include income or net values.

The scaffold spec expects:

- route `/reports/cashflow`
- page file `CashOut/Pages/ReportCashFlow.razor`
- `ReportShell` wrapping the report
- initial page may be a stub

This spec adds a new backend endpoint and replaces the page stub with the full report.

## 4. Backend Endpoint

Add a new endpoint to `ReportsController`:

```http
GET /api/reports/cashflow?year={year}
GET /api/reports/cashflow?year={year}&format=csv
```

Controller implementation:

```csharp
[HttpGet("cashflow")]
public async Task<IActionResult> CashFlow(
    [FromQuery] int? year, [FromQuery] string? format)
{
    if (format == "csv")
        return File(await _reports.CashFlowCsv(year), "text/csv", "cashflow.csv");
    return Ok(await _reports.GetCashFlow(year));
}
```

Do not remove `GET /api/reports/monthly`; it may still be used by older code or future summary pages.

## 5. Backend DTOs

Add these records at the bottom of `CashOut/Services/ReportService.cs` with the other report records:

```csharp
public record CashFlowReportResult(
    int Year,
    int PreviousYear,
    decimal TotalIncome,
    decimal TotalExpenses,
    decimal NetCashFlow,
    decimal PreviousNetCashFlow,
    decimal NetChangeAmount,
    decimal NetChangePercent,
    decimal AverageMonthlyNet,
    decimal BestMonthNet,
    string? BestMonth,
    decimal WorstMonthNet,
    string? WorstMonth,
    int TransactionCount,
    List<CashFlowMonthRow> Months);

public record CashFlowMonthRow(
    string Month,
    string Label,
    decimal Income,
    decimal Expenses,
    decimal Net,
    decimal RollingAverageNet,
    decimal PreviousYearNet,
    decimal ChangeAmount,
    decimal ChangePercent,
    int IncomeCount,
    int ExpenseCount,
    int TransactionCount,
    List<CashFlowTransactionRow> Transactions);

public record CashFlowTransactionRow(
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
    TransactionSource Source,
    string Direction);
```

Notes:

- `Month` is `yyyy-MM`.
- `Label` is `MMM yyyy`.
- `Income`, `Expenses`, and `DisplayAmount` are positive display values.
- `Net` is `Income - Expenses`.
- `PreviousYearNet` is the net value for the same calendar month in `PreviousYear`.
- `ChangeAmount` is `Net - PreviousYearNet`.
- `ChangePercent` is `(Net - PreviousYearNet) / Abs(PreviousYearNet) * 100`, rounded to one decimal place. If previous net is zero, return `0`.
- `RollingAverageNet` is a trailing 3-month average of `Net`, including the current month and up to two prior months in the selected year.
- `Transactions` includes both income and expense transactions for the month.
- `Direction` is `"Income"` for `Amount < 0` and `"Expense"` for `Amount > 0`.
- Exclude zero-amount transactions from income/expense totals, but they may be omitted entirely from report transaction lists for simplicity.

## 6. Backend Service Logic

Add `ReportService.GetCashFlow(int? year = null)` returning `CashFlowReportResult`.

Expected algorithm:

1. Resolve `y` from the method argument or `_settings.GetOutputYear()`.
2. Set `previousYear = y - 1`.
3. Load selected-year non-zero transactions:
   - `Date.Year == y`
   - `Amount != 0`
4. Load previous-year non-zero transactions:
   - `Date.Year == previousYear`
   - `Amount != 0`
5. Group selected-year transactions by month.
6. Group previous-year transactions by month.
7. Create rows for all twelve months of the selected year, January through December, even when a month has no transactions.
8. For each month:
   - `Income = sum(abs(Amount)) where Amount < 0`
   - `Expenses = sum(Amount) where Amount > 0`
   - `Net = Income - Expenses`
   - `IncomeCount = count where Amount < 0`
   - `ExpenseCount = count where Amount > 0`
   - `TransactionCount = IncomeCount + ExpenseCount`
   - `PreviousYearNet = previous-year same-month income - expenses`
   - `ChangeAmount = Net - PreviousYearNet`
   - `ChangePercent = ChangePercentFromNet(Net, PreviousYearNet)`
   - `Transactions = selected-year transactions for that month`
9. After month rows are built, compute `RollingAverageNet` for each row as trailing 3-month average.
10. Compute report-level totals from the month rows.
11. `PreviousNetCashFlow` is previous-year full-year income minus expenses.
12. `NetChangeAmount` is `NetCashFlow - PreviousNetCashFlow`.
13. `NetChangePercent` is `ChangePercentFromNet(NetCashFlow, PreviousNetCashFlow)`.
14. `AverageMonthlyNet` is average `Net` across all twelve months.
15. `BestMonthNet` and `BestMonth` come from the highest monthly `Net`.
16. `WorstMonthNet` and `WorstMonth` come from the lowest monthly `Net`.
17. Return all twelve month rows ordered by `Month`.

Use helpers inside `ReportService`:

```csharp
private static decimal IncomeAmount(Transaction t) =>
    t.Amount < 0 ? Math.Abs(t.Amount) : 0m;

private static decimal ExpenseAmount(Transaction t) =>
    t.Amount > 0 ? t.Amount : 0m;

private static decimal NetAmount(IEnumerable<Transaction> transactions) =>
    transactions.Sum(IncomeAmount) - transactions.Sum(ExpenseAmount);

private static decimal ChangePercentFromNet(decimal current, decimal previous)
{
    if (previous == 0) return 0;
    return Math.Round((current - previous) / Math.Abs(previous) * 100m, 1);
}

private static string MonthKey(int year, int month) => $"{year}-{month:D2}";

private static string MonthLabel(int year, int month) =>
    new DateOnly(year, month, 1).ToString("MMM yyyy");
```

If another report implementation already added compatible helpers, reuse them where practical. Keep the net-specific percent helper separate because it uses `Abs(previous)` for negative prior-year net values.

## 7. Transaction Row Mapping

For `CashFlowTransactionRow`:

- `Amount` is the stored transaction amount.
- `DisplayAmount` is:
  - `Math.Abs(Amount)` for income
  - `Amount` for expenses
- `Direction` is:
  - `"Income"` when `Amount < 0`
  - `"Expense"` when `Amount > 0`

Sort month transactions by:

1. `Date` descending
2. `DisplayAmount` descending
3. `Name` ascending

## 8. CSV Export

Add `ReportService.CashFlowCsv(int? year = null)`.

CSV headers:

```csv
Month,Label,Income,Expenses,Net,RollingAverageNet,PreviousYearNet,ChangeAmount,ChangePercent,IncomeTransactions,ExpenseTransactions,Transactions
```

One row per month. Use invariant culture for decimal values.

Implementation outline:

```csharp
public async Task<byte[]> CashFlowCsv(int? year = null)
{
    var result = await GetCashFlow(year);
    var sb = new StringBuilder("Month,Label,Income,Expenses,Net,RollingAverageNet,PreviousYearNet,ChangeAmount,ChangePercent,IncomeTransactions,ExpenseTransactions,Transactions\n");
    foreach (var r in result.Months)
    {
        sb.AppendLine(
            $"{r.Month},{r.Label}," +
            $"{r.Income.ToString(CultureInfo.InvariantCulture)}," +
            $"{r.Expenses.ToString(CultureInfo.InvariantCulture)}," +
            $"{r.Net.ToString(CultureInfo.InvariantCulture)}," +
            $"{r.RollingAverageNet.ToString(CultureInfo.InvariantCulture)}," +
            $"{r.PreviousYearNet.ToString(CultureInfo.InvariantCulture)}," +
            $"{r.ChangeAmount.ToString(CultureInfo.InvariantCulture)}," +
            $"{r.ChangePercent.ToString(CultureInfo.InvariantCulture)}," +
            $"{r.IncomeCount},{r.ExpenseCount},{r.TransactionCount}");
    }
    return Encoding.UTF8.GetBytes(sb.ToString());
}
```

CSV export endpoint:

```http
GET /api/reports/cashflow?year={year}&format=csv
```

The downloaded filename should be `cashflow.csv`.

## 9. UI Page

Implement `CashOut/Pages/ReportCashFlow.razor`.

The page must:

- use `@page "/reports/cashflow"`
- inject `HttpClient`
- render inside `<ReportShell>`
- load available years from `api/settings/years`
- load report data from `api/reports/cashflow?year={_year}`
- remove `IsStub="true"`
- show loading and error states through `ReportShell`
- expose CSV export through `ExportHref="@($"api/reports/cashflow?year={_year}&format=csv")"`

Suggested top-level page structure:

```razor
@page "/reports/cashflow"
@inject HttpClient Http

<ReportShell Title="Net Cash Flow"
             Year="_year"
             AvailableYears="_availableYears"
             OnYearChanged="OnYearChanged"
             Loading="_loading"
             Error="_error"
             ExportHref="@($"api/reports/cashflow?year={_year}&format=csv")">
    @if (_report is null)
    {
        <MudAlert Severity="Severity.Info" Variant="Variant.Outlined">
            No cash flow data found for @_year.
        </MudAlert>
    }
    else
    {
        <!-- summary panels, monthly table, and drill-down panel go here -->
    }
</ReportShell>
```

Because the backend returns all twelve months even when empty, `_report.Months.Count` should normally be `12`. Treat a null report as empty.

## 10. UI Content Requirements

### 10a. Summary Metrics

At the top of the report body, show four compact summary panels:

- Total income: `_report.TotalIncome.ToString("C")`
- Total expenses: `_report.TotalExpenses.ToString("C")`
- Net cash flow: `_report.NetCashFlow.ToString("C")`
- Average monthly net: `_report.AverageMonthlyNet.ToString("C")`

Also show previous-year net comparison near the Net Cash Flow metric:

- `_report.NetChangeAmount.ToString("C")`
- `_report.NetChangePercent`

Use semantic coloring:

- Positive net cash flow should be visually positive.
- Negative net cash flow should be visually negative or warning-colored.
- Positive net change should be visually positive.
- Negative net change should be visually negative or warning-colored.
- Zero should be neutral.

Do not make a marketing-style hero section. This is an operational report page.

### 10b. Monthly Cash Flow Table

Render a `MudTable` over `_report.Months`.

Columns:

- Month
- Income
- Expenses
- Net
- Rolling avg
- Previous year net
- Change
- Transactions

Expected formatting:

- currency values use `.ToString("C")`
- percent values show one decimal place and a percent sign
- count values are right-aligned
- money and percent columns are right-aligned
- table is dense and hoverable
- rows are ordered January through December from the API
- positive `Net` values should be visually positive
- negative `Net` values should be visually negative or warning-colored

The selected row should drive the month drill-down panel.

Implementation detail:

- Track `_selectedMonth` as `CashFlowMonthRow?`.
- On row click, set `_selectedMonth = context`.
- When data loads, default `_selectedMonth` to the latest month in the selected year that has transactions. If no month has transactions, default to the first month.

MudBlazor can use `RowClick`:

```razor
<MudTable Items="@_report.Months"
          Dense="true"
          Hover="true"
          Breakpoint="Breakpoint.Sm"
          Elevation="0"
          RowClick="OnMonthRowClick">
```

Then:

```csharp
private void OnMonthRowClick(TableRowClickEventArgs<CashFlowMonthRow> args)
{
    _selectedMonth = args.Item;
}
```

### 10c. Month Drill-Down Transaction List

Below the monthly table, show a selected-month transaction list.

Header:

- selected month label
- income
- expenses
- net
- transaction count

Table columns:

- Date
- Direction
- Merchant
- Category
- Source
- Amount

Rules:

- Show both income and expense transactions for the selected month.
- Render `DisplayAmount`, not stored `Amount`, in the Amount column.
- Show direction so income and expenses are distinguishable.
- Sort by date descending from the API.
- If no month is selected, show a small neutral message.
- If the selected month has no transactions, show a small neutral message.

Use `TransactionId` plus `AccountId` as the stable identity if `@key` is needed:

```razor
@key $"{context.AccountId}:{context.TransactionId}"
```

### 10d. Empty State

If the API returns null or fails to return month rows, show:

```text
No cash flow data found for {year}.
```

If the report returns twelve months with zero totals, show the summary and table with zero values. Do not hide the table in that case.

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

Define page-local records in `ReportCashFlow.razor` matching the backend JSON:

```csharp
private record CashFlowReportResult(
    int Year,
    int PreviousYear,
    decimal TotalIncome,
    decimal TotalExpenses,
    decimal NetCashFlow,
    decimal PreviousNetCashFlow,
    decimal NetChangeAmount,
    decimal NetChangePercent,
    decimal AverageMonthlyNet,
    decimal BestMonthNet,
    string? BestMonth,
    decimal WorstMonthNet,
    string? WorstMonth,
    int TransactionCount,
    List<CashFlowMonthRow> Months);

private record CashFlowMonthRow(
    string Month,
    string Label,
    decimal Income,
    decimal Expenses,
    decimal Net,
    decimal RollingAverageNet,
    decimal PreviousYearNet,
    decimal ChangeAmount,
    decimal ChangePercent,
    int IncomeCount,
    int ExpenseCount,
    int TransactionCount,
    List<CashFlowTransactionRow> Transactions);

private record CashFlowTransactionRow(
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
    TransactionSource Source,
    string Direction);
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
        _report = await Http.GetFromJsonAsync<CashFlowReportResult>(
            $"api/reports/cashflow?year={_year}");
        _selectedMonth = _report?.Months
            .LastOrDefault(m => m.TransactionCount > 0)
            ?? _report?.Months.FirstOrDefault();
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
private CashFlowReportResult? _report;
private CashFlowMonthRow? _selectedMonth;
```

## 13. Styling Guidance

Keep styles local to `ReportCashFlow.razor` unless a reusable pattern already exists.

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
- `.income-value`, `.expense-value`, `.net-positive`, `.net-negative`
- `.muted-subtext`

If adding CSS, put it in a `<style>` block at the bottom of `ReportCashFlow.razor`.

## 14. Tests

Update `CashOut.Tests/ReportServiceTests.cs`.

### 14a. New Service Tests

Add these tests:

1. `GetCashFlow_ReturnsTwelveMonths`
   - Add no transactions or a small set of transactions.
   - Assert `result.Months.Count == 12`.
   - Assert first month is `{year}-01`.
   - Assert last month is `{year}-12`.

2. `GetCashFlow_ComputesIncomeExpensesAndNet`
   - Add January income `Amount = -1000`.
   - Add January expense `Amount = 300`.
   - Assert January `Income == 1000`.
   - Assert January `Expenses == 300`.
   - Assert January `Net == 700`.

3. `GetCashFlow_YearTotalsMatchMonthlySums`
   - Add transactions across multiple months.
   - Assert `TotalIncome == Months.Sum(m => m.Income)`.
   - Assert `TotalExpenses == Months.Sum(m => m.Expenses)`.
   - Assert `NetCashFlow == Months.Sum(m => m.Net)`.

4. `GetCashFlow_IncludesPreviousYearComparison`
   - Add January 2025 net of 700.
   - Add January 2024 net of 500.
   - Assert January `PreviousYearNet == 500`.
   - Assert January `ChangeAmount == 200`.
   - Assert January `ChangePercent == 40`.

5. `GetCashFlow_PreviousNegativeNet_UsesAbsoluteDenominatorForPercent`
   - Add January 2025 net of 100.
   - Add January 2024 net of -100.
   - Assert `ChangeAmount == 200`.
   - Assert `ChangePercent == 200`.

6. `GetCashFlow_ComputesTrailingThreeMonthRollingAverage`
   - January net 100.
   - February net 200.
   - March net 300.
   - April net 600.
   - Assert January rolling average is 100.
   - Assert February rolling average is 150.
   - Assert March rolling average is 200.
   - Assert April rolling average is `(200 + 300 + 600) / 3`.

7. `GetCashFlow_IncludesMonthTransactionsWithDirection`
   - Add one income and one expense in January.
   - Assert January has two transactions.
   - Assert one has `Direction == "Income"` and positive `DisplayAmount`.
   - Assert one has `Direction == "Expense"` and positive `DisplayAmount`.

8. `CashFlowCsv_IncludesExpectedHeaders`
   - Call `CashFlowCsv(2025)`.
   - Decode UTF-8.
   - Assert the header line contains `Month,Label,Income,Expenses,Net`.

### 14b. Optional UI Test

If the scaffold and app server are already testable in the current branch, add a Playwright UI test in `CashOut.Tests/UiTests.cs`:

```csharp
[TestMethod]
[TestCategory("UI")]
public async Task ReportsCashFlowPage_ShowsNetCashFlowHeader()
{
    await Page.GotoAsync("http://localhost:8080/reports/cashflow");
    var header = Page.GetByRole(AriaRole.Heading, new() { Name = "Net Cash Flow" });
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

- `/reports/cashflow` renders "Net Cash Flow".
- Year picker loads from `api/settings/years`.
- Changing the year reloads the report.
- Monthly table shows January through December.
- Income values come from `Amount < 0` and render positive.
- Expense values come from `Amount > 0`.
- Net equals income minus expenses.
- Rolling average uses the current month and up to two prior months.
- Previous-year comparison uses the same calendar month in the previous year.
- Clicking a month row updates the transaction drill-down.
- CSV export downloads from `api/reports/cashflow?year={year}&format=csv`.

## 16. Files to Modify

Required:

- `CashOut/Controllers/ReportsController.cs`
- `CashOut/Services/ReportService.cs`
- `CashOut/Pages/ReportCashFlow.razor`
- `CashOut.Tests/ReportServiceTests.cs`

Usually unchanged:

- `CashOut/Shared/ReportShell.razor`
- `CashOut/Shared/MainLayout.razor`

Only modify the unchanged files if the scaffold has not yet been applied or the existing code does not match `docs/reports-ui-scaffold-spec.md`.

No database migration is required.

## 17. Acceptance Criteria

The implementation is complete when:

- The Net Cash Flow report page is no longer a stub.
- `GET /api/reports/cashflow?year={year}` returns `CashFlowReportResult`.
- `GET /api/reports/cashflow?year={year}&format=csv` downloads CSV.
- The backend computes income from `Amount < 0` and expenses from `Amount > 0`.
- The backend returns all twelve month rows for the selected year.
- The UI displays total income, expenses, net cash flow, monthly rows, rolling averages, previous-year comparison, and month transaction drill-down.
- CSV export includes monthly income, expenses, net, rolling average, and comparison values.
- `dotnet test` passes.
- Existing report routes and settings year loading still work.
- No database migration is added.
