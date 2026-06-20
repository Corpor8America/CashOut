# Expense-to-Income Ratio Report Implementation Spec

**Status:** Implementation-ready  
**Recommended display name:** `Expense-to-Income Ratio`  
**Recommended route:** `/reports/expense-income`  
**Recommended page:** `CashOut/Pages/ReportExpenseIncome.razor`  
**Related scaffold:** `docs/reports-ui-scaffold-spec.md`  
**Related specs:** `docs/report-income-spec.md`, `docs/report-category-spec.md`, `docs/report-cashflow-spec.md`

## 1. Goal

Implement a report that answers:

> What percentage of my income did my expenses consume?

This is different from the existing category report's "percent of spending" metric. The category report answers, "What share of my total spending went to groceries?" This report answers, "What share of my income went to groceries, rent, restaurants, or all expenses combined?"

The report must show expense-to-income ratios:

- by month
- by quarter
- by year
- by category within each selected period
- with transaction drill-down for selected period/category
- with CSV export

## 2. Placement Decision

Add this as a separate report, not as a section inside Income, Category, or Cash Flow.

Reasoning:

- The Income report is source-focused: where money came from.
- The Category report is expense distribution-focused: where spending went.
- The Cash Flow report is net-focused: income minus expenses.
- This report is ratio-focused: how much income was consumed by expenses.

Recommended navigation placement:

```razor
<MudNavLink Href="/reports/income"
            Icon="@Icons.Material.Filled.TrendingUp">
    Income
</MudNavLink>
<MudNavLink Href="/reports/expense-income"
            Icon="@Icons.Material.Filled.PieChart">
    Expense / Income
</MudNavLink>
<MudNavLink Href="/reports/cashflow"
            Icon="@Icons.Material.Filled.SwapVert">
    Cash Flow
</MudNavLink>
```

Place it between Income and Cash Flow because it bridges both concepts.

Update `docs/reports-ui-scaffold-spec.md` later if desired, but this spec is sufficient for implementation.

## 3. Important Existing Convention

Use the convention implemented in `CashOut/Models/Transaction.cs`:

- `Amount > 0` means money leaving the account, so it is an expense.
- `Amount < 0` means money entering the account, so it is income, refund, or credit.
- `Debit` stores outflow amount.
- `Credit` stores inflow amount.

For this report:

- income is the sum of `Math.Abs(Amount)` for transactions where `Amount < 0`
- expenses are the sum of `Amount` for transactions where `Amount > 0`
- expense-to-income percent is `expenses / income * 100`

All displayed income and expense totals are positive values.

## 4. Backend Endpoint

Add a new endpoint to `ReportsController`:

```http
GET /api/reports/expense-income?year={year}&period={period}
GET /api/reports/expense-income?year={year}&period={period}&format=csv
```

Query parameters:

- `year`: optional `int`; defaults to `SettingsService.GetOutputYear()`
- `period`: optional `string`; allowed values are `monthly`, `quarterly`, `yearly`; default is `monthly`
- `format`: optional `string`; `csv` returns CSV

Controller implementation:

```csharp
[HttpGet("expense-income")]
public async Task<IActionResult> ExpenseIncome(
    [FromQuery] int? year,
    [FromQuery] string period = "monthly",
    [FromQuery] string? format = null)
{
    if (format == "csv")
        return File(await _reports.ExpenseIncomeCsv(year, period), "text/csv", "expense-income.csv");
    return Ok(await _reports.GetExpenseIncome(year, period));
}
```

No existing endpoints should be removed.

## 5. Backend DTOs

Add these records at the bottom of `CashOut/Services/ReportService.cs`:

```csharp
public record ExpenseIncomeReportResult(
    int Year,
    string Period,
    decimal TotalIncome,
    decimal TotalExpenses,
    decimal NetCashFlow,
    decimal? ExpenseToIncomePercent,
    decimal AveragePeriodPercent,
    decimal? HighestPeriodPercent,
    string? HighestPeriodLabel,
    decimal? LowestPeriodPercent,
    string? LowestPeriodLabel,
    int IncomeTransactionCount,
    int ExpenseTransactionCount,
    List<ExpenseIncomePeriodRow> Periods);

public record ExpenseIncomePeriodRow(
    string PeriodKey,
    string Label,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal Income,
    decimal Expenses,
    decimal NetCashFlow,
    decimal? ExpenseToIncomePercent,
    int IncomeTransactionCount,
    int ExpenseTransactionCount,
    List<ExpenseIncomeCategoryRow> Categories);

public record ExpenseIncomeCategoryRow(
    string Category,
    decimal Expenses,
    decimal PctOfIncome,
    decimal PctOfExpenses,
    int TransactionCount,
    List<ExpenseIncomeTransactionRow> Transactions);

public record ExpenseIncomeTransactionRow(
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

- `ExpenseToIncomePercent` is nullable because the ratio is undefined when income is zero.
- `PctOfIncome` for categories is `category expenses / period income * 100`.
- `PctOfExpenses` for categories is `category expenses / period expenses * 100`.
- `DisplayAmount` is positive.
- Category transactions include expenses only.

## 6. Zero-Income Handling

Do not return `0%` when income is zero. That would be misleading.

Rules:

- If income is `0`, `ExpenseToIncomePercent` is `null`.
- If income is `0`, category `PctOfIncome` is `0`.
- UI should render ratio as `N/A` when nullable percent is null.
- CSV should render an empty value for nullable percent fields.

Examples:

- income `$5,000`, expenses `$2,500` => `50%`
- income `$5,000`, expenses `$6,000` => `120%`
- income `$0`, expenses `$500` => `N/A`
- income `$0`, expenses `$0` => `N/A`

## 7. Backend Service Logic

Add `ReportService.GetExpenseIncome(int? year = null, string period = "monthly")` returning `ExpenseIncomeReportResult`.

Expected algorithm:

1. Resolve `y` from method argument or `_settings.GetOutputYear()`.
2. Normalize period:
   - null/blank => `monthly`
   - accepted case-insensitive values: `monthly`, `quarterly`, `yearly`
   - invalid value => default to `monthly`
3. Load selected-year non-zero transactions:
   - `Date.Year == y`
   - `Amount != 0`
4. Build period ranges:
   - monthly: 12 rows, January through December
   - quarterly: 4 rows, Q1 through Q4
   - yearly: 1 row, full year
5. For each period:
   - select transactions whose `Date` is between `StartDate` and `EndDate`
   - income = sum `Math.Abs(Amount)` where `Amount < 0`
   - expenses = sum `Amount` where `Amount > 0`
   - net = income - expenses
   - expense-to-income percent = `PercentNullable(expenses, income)`
   - income count = count where `Amount < 0`
   - expense count = count where `Amount > 0`
   - build category rows from expense transactions only
6. Report totals:
   - total income = sum period income
   - total expenses = sum period expenses
   - net cash flow = total income - total expenses
   - overall expense-to-income percent = `PercentNullable(total expenses, total income)`
7. `AveragePeriodPercent`:
   - average only non-null period percentages
   - if every period percent is null, return `0`
8. Highest/lowest period percentages:
   - consider only non-null percentages
   - if every period percent is null, return null for percent and label

Use helpers inside `ReportService`:

```csharp
private static decimal IncomeAmount(Transaction t) =>
    t.Amount < 0 ? Math.Abs(t.Amount) : 0m;

private static decimal ExpenseAmount(Transaction t) =>
    t.Amount > 0 ? t.Amount : 0m;

private static decimal? PercentNullable(decimal numerator, decimal denominator) =>
    denominator == 0 ? null : Math.Round(numerator / denominator * 100m, 1);

private static decimal Percent(decimal numerator, decimal denominator) =>
    denominator == 0 ? 0 : Math.Round(numerator / denominator * 100m, 1);

private static string CategoryKey(Transaction t) =>
    string.IsNullOrWhiteSpace(t.Category) ? "Unassigned" : t.Category;
```

## 8. Period Ranges

Monthly:

- `2026-01`, `Jan 2026`, Jan 1 through Jan 31
- ...
- `2026-12`, `Dec 2026`, Dec 1 through Dec 31

Quarterly:

- `2026-Q1`, `Q1 2026`, Jan 1 through Mar 31
- `2026-Q2`, `Q2 2026`, Apr 1 through Jun 30
- `2026-Q3`, `Q3 2026`, Jul 1 through Sep 30
- `2026-Q4`, `Q4 2026`, Oct 1 through Dec 31

Yearly:

- `2026`, `2026`, Jan 1 through Dec 31

Always return every period row, even when totals are zero.

## 9. Category Breakdown Logic

For each period, group expense transactions by category:

- include only `Amount > 0`
- category key is `Category`, falling back to `Unassigned`

For each category:

- `Expenses = sum Amount`
- `PctOfIncome = Percent(category expenses, period income)`
- `PctOfExpenses = Percent(category expenses, period expenses)`
- `TransactionCount = count`
- `Transactions = expense transactions in that period/category`

Sort categories by `Expenses` descending.

Sort category transactions by:

1. `Date` descending
2. `DisplayAmount` descending
3. `Name` ascending

## 10. CSV Export

Add `ReportService.ExpenseIncomeCsv(int? year = null, string period = "monthly")`.

CSV headers:

```csv
Period,Label,Income,Expenses,NetCashFlow,ExpenseToIncomePercent,IncomeTransactions,ExpenseTransactions,Category,CategoryExpenses,CategoryPctOfIncome,CategoryPctOfExpenses,CategoryTransactions
```

Export one row per period/category. If a period has no categories, export one row with blank category fields so period-level totals are still visible.

Use invariant culture for decimal values. For nullable percent fields, write an empty field when null.

CSV export endpoint:

```http
GET /api/reports/expense-income?year={year}&period={period}&format=csv
```

Downloaded filename can be `expense-income.csv`.

## 11. UI Page

Create `CashOut/Pages/ReportExpenseIncome.razor`.

The page must:

- use `@page "/reports/expense-income"`
- inject `HttpClient`
- render inside `<ReportShell>`
- load available years from `api/settings/years`
- load report data from `api/reports/expense-income?year={_year}&period={_period}`
- show loading and error states through `ReportShell`
- expose CSV export through `ExportHref="@($"api/reports/expense-income?year={_year}&period={_period}&format=csv")"`

Add the page to the sidebar navigation as described in section 2.

Suggested top-level structure:

```razor
@page "/reports/expense-income"
@inject HttpClient Http

<ReportShell Title="Expense-to-Income Ratio"
             Year="_year"
             AvailableYears="_availableYears"
             OnYearChanged="OnYearChanged"
             Loading="_loading"
             Error="_error"
             ExportHref="@($"api/reports/expense-income?year={_year}&period={_period}&format=csv")">
    <MudPaper Class="pa-4 mb-4" Elevation="0">
        <MudToggleGroup T="string" Value="_period" ValueChanged="OnPeriodChanged">
            <MudToggleItem Value="@("monthly")">Monthly</MudToggleItem>
            <MudToggleItem Value="@("quarterly")">Quarterly</MudToggleItem>
            <MudToggleItem Value="@("yearly")">Yearly</MudToggleItem>
        </MudToggleGroup>
    </MudPaper>

    @if (_report is null)
    {
        <MudAlert Severity="Severity.Info" Variant="Variant.Outlined">
            No expense-to-income data found for @_year.
        </MudAlert>
    }
    else
    {
        <!-- summary panels, period table, category breakdown, transactions -->
    }
</ReportShell>
```

If `MudToggleGroup` is not convenient in the installed MudBlazor version, use `MudSelect<string>` or three `MudButton` controls instead.

## 12. UI Content Requirements

### 12a. Summary Metrics

At the top of the report body, show four compact summary panels:

- Total income
- Total expenses
- Expense-to-income ratio
- Net cash flow

Formatting:

- money values use `.ToString("C")`
- percent values show one decimal place and `%`
- null percent values render as `N/A`

Semantic coloring:

- ratio under `70%`: positive/healthy
- ratio from `70%` through `100%`: warning
- ratio above `100%`: negative/over income
- positive net cash flow: positive
- negative net cash flow: negative

Do not hard-code labels like "good" or "bad"; use restrained visual color only.

### 12b. Period Table

Render a `MudTable` over `_report.Periods`.

Columns:

- Period
- Income
- Expenses
- Expense / Income
- Net Cash Flow
- Income Txns
- Expense Txns

Rows are ordered chronologically from the API.

The selected row should drive the category breakdown panel.

Implementation detail:

- Track `_selectedPeriod` as `ExpenseIncomePeriodRow?`.
- On row click, set `_selectedPeriod = context`.
- When data loads, default `_selectedPeriod` to the latest period with any income or expenses. If every period is empty, default to the first period.

### 12c. Category Breakdown

Below the period table, show category rows for `_selectedPeriod`.

Columns:

- Category
- Expenses
- Percent of income
- Percent of expenses
- Transactions

This is the core feature:

- `Percent of income` tells the user what share of income a category consumed.
- `Percent of expenses` preserves the older category-distribution context.

Clicking a category row should drive the transaction list.

Implementation detail:

- Track `_selectedCategory` as `ExpenseIncomeCategoryRow?`.
- When selected period changes, default selected category to the first category in that period.

### 12d. Transaction Drill-Down

Below the category breakdown, show transactions for the selected category.

Columns:

- Date
- Merchant
- Category
- Source
- Amount

Rules:

- Show only expenses for selected period/category.
- Render `DisplayAmount`, not stored `Amount`.
- If no category is selected, show a neutral message.
- If selected category has no transactions, show a neutral message.

### 12e. Empty State

The backend should return period rows even when there is no data. If report is null, show:

```text
No expense-to-income data found for {year}.
```

If report exists with all zero totals, show summary/table with zero values and `N/A` ratios.

## 13. UI DTO Records

Define page-local records in `ReportExpenseIncome.razor` matching the backend JSON:

```csharp
private record ExpenseIncomeReportResult(
    int Year,
    string Period,
    decimal TotalIncome,
    decimal TotalExpenses,
    decimal NetCashFlow,
    decimal? ExpenseToIncomePercent,
    decimal AveragePeriodPercent,
    decimal? HighestPeriodPercent,
    string? HighestPeriodLabel,
    decimal? LowestPeriodPercent,
    string? LowestPeriodLabel,
    int IncomeTransactionCount,
    int ExpenseTransactionCount,
    List<ExpenseIncomePeriodRow> Periods);

private record ExpenseIncomePeriodRow(
    string PeriodKey,
    string Label,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal Income,
    decimal Expenses,
    decimal NetCashFlow,
    decimal? ExpenseToIncomePercent,
    int IncomeTransactionCount,
    int ExpenseTransactionCount,
    List<ExpenseIncomeCategoryRow> Categories);

private record ExpenseIncomeCategoryRow(
    string Category,
    decimal Expenses,
    decimal PctOfIncome,
    decimal PctOfExpenses,
    int TransactionCount,
    List<ExpenseIncomeTransactionRow> Transactions);

private record ExpenseIncomeTransactionRow(
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

## 14. Page Loading Flow

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

private async Task OnPeriodChanged(string period)
{
    _period = string.IsNullOrWhiteSpace(period) ? "monthly" : period;
    await LoadReport();
}

private async Task LoadReport()
{
    _loading = true;
    _error = null;
    try
    {
        _report = await Http.GetFromJsonAsync<ExpenseIncomeReportResult>(
            $"api/reports/expense-income?year={_year}&period={_period}");
        _selectedPeriod = _report?.Periods
            .LastOrDefault(p => p.Income > 0 || p.Expenses > 0)
            ?? _report?.Periods.FirstOrDefault();
        _selectedCategory = _selectedPeriod?.Categories.FirstOrDefault();
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
private string _period = "monthly";
private List<int> _availableYears = new() { DateTime.Now.Year };
private bool _loading;
private string? _error;
private ExpenseIncomeReportResult? _report;
private ExpenseIncomePeriodRow? _selectedPeriod;
private ExpenseIncomeCategoryRow? _selectedCategory;
```

## 15. Styling Guidance

Keep styles local to `ReportExpenseIncome.razor` unless a reusable pattern already exists.

Use restrained utility classes and MudBlazor components. This page should feel like a financial reporting tool:

- compact
- readable
- table-first
- no oversized hero UI
- no decorative gradients
- no nested cards

Acceptable styling additions:

- `.report-summary-grid`
- `.metric-panel`
- `.ratio-healthy`
- `.ratio-warning`
- `.ratio-over`
- `.positive-change`
- `.negative-change`
- `.muted-subtext`

## 16. Tests

Update `CashOut.Tests/ReportServiceTests.cs`.

Add these tests:

1. `GetExpenseIncome_Monthly_ReturnsTwelvePeriods`
   - Assert monthly result returns 12 period rows.
   - Assert first row is January and last is December.

2. `GetExpenseIncome_Quarterly_ReturnsFourPeriods`
   - Assert quarterly result returns Q1 through Q4.

3. `GetExpenseIncome_Yearly_ReturnsOnePeriod`
   - Assert yearly result returns one row for the full year.

4. `GetExpenseIncome_ComputesOverallRatio`
   - Add income `Amount = -5000`.
   - Add expenses totaling `2500`.
   - Assert `ExpenseToIncomePercent == 50`.

5. `GetExpenseIncome_AllowsOverIncomeRatio`
   - Add income `Amount = -5000`.
   - Add expenses totaling `6000`.
   - Assert `ExpenseToIncomePercent == 120`.

6. `GetExpenseIncome_ZeroIncome_ReturnsNullRatio`
   - Add expense `Amount = 500`.
   - No income.
   - Assert period `ExpenseToIncomePercent == null`.
   - Assert report `ExpenseToIncomePercent == null`.

7. `GetExpenseIncome_CategoryPctOfIncome_IsCorrect`
   - Add income `Amount = -5000`.
   - Add grocery expense `Amount = 500`.
   - Assert grocery `PctOfIncome == 10`.

8. `GetExpenseIncome_CategoryPctOfExpenses_IsCorrect`
   - Add income `Amount = -5000`.
   - Add grocery expense `Amount = 500`.
   - Add rent expense `Amount = 1500`.
   - Assert grocery `PctOfExpenses == 25`.

9. `GetExpenseIncome_ExcludesIncomeFromCategoryTransactions`
   - Add income and expenses.
   - Assert category transaction lists contain only `Amount > 0`.

10. `ExpenseIncomeCsv_IncludesExpectedHeaders`
   - Call `ExpenseIncomeCsv(2025, "monthly")`.
   - Decode UTF-8.
   - Assert header contains `Period,Label,Income,Expenses,NetCashFlow,ExpenseToIncomePercent`.

Optional UI test:

```csharp
[TestMethod]
[TestCategory("UI")]
public async Task ReportsExpenseIncomePage_ShowsHeader()
{
    await Page.GotoAsync("http://localhost:8080/reports/expense-income");
    var header = Page.GetByRole(AriaRole.Heading, new() { Name = "Expense-to-Income Ratio" });
    await Expect(header).ToBeVisibleAsync();
}
```

## 17. Verification

Run:

```powershell
dotnet test
```

Manual checks:

- `/reports/expense-income` renders "Expense-to-Income Ratio".
- Sidebar includes "Expense / Income" between Income and Cash Flow.
- Year picker loads from `api/settings/years`.
- Period control switches monthly, quarterly, and yearly.
- Income uses `Amount < 0` and renders positive.
- Expenses use `Amount > 0`.
- Overall ratio is expenses divided by income.
- Category ratio is category expenses divided by income.
- Zero-income periods show `N/A`, not `0%`.
- Clicking a period changes the category breakdown.
- Clicking a category changes the transaction list.
- CSV export downloads from `api/reports/expense-income?year={year}&period={period}&format=csv`.

## 18. Files to Modify

Required:

- `CashOut/Controllers/ReportsController.cs`
- `CashOut/Services/ReportService.cs`
- `CashOut/Pages/ReportExpenseIncome.razor`
- `CashOut/Shared/MainLayout.razor`
- `CashOut.Tests/ReportServiceTests.cs`

Usually unchanged:

- `CashOut/Shared/ReportShell.razor`

No database migration is required.

## 19. Acceptance Criteria

The implementation is complete when:

- A new `/reports/expense-income` page exists.
- Sidebar navigation includes the report.
- `GET /api/reports/expense-income?year={year}&period={period}` returns `ExpenseIncomeReportResult`.
- `GET /api/reports/expense-income?year={year}&period={period}&format=csv` downloads CSV.
- The backend computes income from `Amount < 0` and expenses from `Amount > 0`.
- The report supports monthly, quarterly, and yearly period modes.
- The UI shows overall and per-category percent of income consumed by expenses.
- Zero-income ratios render as `N/A`.
- `dotnet test` passes.
- Existing report routes and settings year loading still work.
- No database migration is added.
