# Executive Summary Dashboard Implementation Spec

**Status:** Implementation-ready  
**Target route:** `/reports`  
**Target page:** `CashOut/Pages/Reports.razor`  
**Related scaffold:** `docs/reports-ui-scaffold-spec.md`  
**Related feature note:** `docs/report-features.md`

## 1. Goal

Implement the Executive Summary Dashboard described in `docs/report-features.md` inside the report scaffold described in `docs/reports-ui-scaffold-spec.md`.

The dashboard must provide a single-screen overview of the user's financial status for a selected year, focused on the latest month in that year that has transactions.

The dashboard must include:

- monthly overview
- total spending
- total income
- net cash flow
- month-over-month change
- top spending categories
- top spending merchants
- recurring charge candidates
- alert counts and alert details
- account summary
- CSV export

The scaffold spec already defines where this dashboard lives in the UI. Do not redesign the report navigation shell. Replace the Executive Summary stub in `CashOut/Pages/Reports.razor` with the implementation described here.

## 2. Important Existing Convention

`docs/report-features.md` uses the opposite sign convention from the current CashOut model.

Use the convention implemented in `CashOut/Models/Transaction.cs`:

- `Amount > 0` means money leaving the account, so it is an expense.
- `Amount < 0` means money entering the account, so it is income, refund, or credit.
- `Debit` stores outflow amount.
- `Credit` stores inflow amount.

For this dashboard:

- income is the sum of `Math.Abs(Amount)` for transactions where `Amount < 0`
- spending/expenses are the sum of `Amount` for transactions where `Amount > 0`
- net cash flow is `income - expenses`

Positive net cash flow means the user kept more money than they spent. Negative net cash flow means expenses exceeded income.

## 3. Current State

There is no Executive Summary endpoint.

The scaffold spec expects:

- route `/reports`
- page file `CashOut/Pages/Reports.razor`
- `ReportShell` wrapping the page
- initial page may be a stub

This spec adds a new backend endpoint and replaces the stub page with the full dashboard.

## 4. Dashboard Period Rules

The page has a year picker from `ReportShell`. The dashboard itself summarizes the latest month in the selected year that contains at least one non-zero transaction.

Rules:

1. Use the selected year from the year picker.
2. Find the latest month in that year where `Amount != 0`.
3. If there are no transactions in the selected year, use December of the selected year as the dashboard month and return zero totals.
4. The previous month comparison is the calendar month immediately before the dashboard month.
5. If the dashboard month is January, the previous month is December of the previous year.
6. Top categories and top merchants are for the dashboard month, not the whole selected year.
7. Account summary is for the selected year as a whole unless otherwise specified below.

Expose both the selected year and dashboard month in the DTO so the UI can say what period the summary represents.

## 5. Backend Endpoint

Add a new endpoint to `ReportsController`:

```http
GET /api/reports/summary?year={year}
GET /api/reports/summary?year={year}&format=csv
```

Controller implementation:

```csharp
[HttpGet("summary")]
public async Task<IActionResult> Summary(
    [FromQuery] int? year, [FromQuery] string? format)
{
    if (format == "csv")
        return File(await _reports.ExecutiveSummaryCsv(year), "text/csv", "executive-summary.csv");
    return Ok(await _reports.GetExecutiveSummary(year));
}
```

Do not remove any existing report endpoints.

## 6. Backend DTOs

Add these records at the bottom of `CashOut/Services/ReportService.cs` with the other report records:

```csharp
public record ExecutiveSummaryResult(
    int Year,
    int Month,
    string MonthKey,
    string MonthLabel,
    ExecutiveMonthlyOverview MonthlyOverview,
    List<ExecutiveTopCategoryRow> TopCategories,
    List<ExecutiveTopMerchantRow> TopMerchants,
    List<ExecutiveRecurringChargeRow> RecurringCharges,
    ExecutiveAlertSummary Alerts,
    List<ExecutiveAccountSummaryRow> Accounts);

public record ExecutiveMonthlyOverview(
    decimal TotalSpending,
    decimal PreviousMonthSpending,
    decimal SpendingChangeAmount,
    decimal SpendingChangePercent,
    decimal TotalIncome,
    decimal PreviousMonthIncome,
    decimal IncomeChangeAmount,
    decimal IncomeChangePercent,
    decimal NetCashFlow,
    decimal PreviousMonthNetCashFlow,
    decimal NetCashFlowChangeAmount,
    decimal NetCashFlowChangePercent,
    int IncomeTransactionCount,
    int ExpenseTransactionCount,
    int TransactionCount);

public record ExecutiveTopCategoryRow(
    string Category,
    decimal Total,
    decimal PctOfSpend,
    int Count,
    decimal PreviousMonthTotal,
    decimal ChangeAmount,
    decimal ChangePercent);

public record ExecutiveTopMerchantRow(
    string MerchantKey,
    int? AliasId,
    string Name,
    string NormalizedName,
    bool IsMapped,
    string PrimaryCategory,
    decimal Total,
    decimal PctOfSpend,
    int Count,
    decimal PreviousMonthTotal,
    decimal ChangeAmount,
    decimal ChangePercent);

public record ExecutiveRecurringChargeRow(
    string MerchantKey,
    string Name,
    string Category,
    decimal LatestAmount,
    decimal AverageAmount,
    decimal AmountChange,
    int OccurrenceCount,
    DateOnly LatestDate,
    string Cadence,
    bool IsAmountChanged);

public record ExecutiveAlertSummary(
    int UnmatchedMerchantCount,
    int UncategorizedTransactionCount,
    int PossibleDuplicateCount,
    int RuleConflictCount,
    List<ExecutiveAlertRow> Items);

public record ExecutiveAlertRow(
    string Severity,
    string Type,
    string Title,
    string Detail,
    int Count);

public record ExecutiveAccountSummaryRow(
    string AccountId,
    string AccountName,
    string AccountType,
    decimal Income,
    decimal Expenses,
    decimal NetCashFlow,
    int TransactionCount);
```

Notes:

- All money values are display values.
- Spending/expense values are positive.
- Income values are positive.
- Net values can be positive or negative.
- Percent fields are rounded to one decimal place.
- Top category and top merchant lists should be limited to five rows each.
- Recurring charge candidates should be limited to five rows.

## 7. Backend Service Logic

Add `ReportService.GetExecutiveSummary(int? year = null)` returning `ExecutiveSummaryResult`.

Expected algorithm:

1. Resolve `y` from the method argument or `_settings.GetOutputYear()`.
2. Load selected-year non-zero transactions:
   - `Date.Year == y`
   - `Amount != 0`
   - include `Alias` if merchant display names need alias data
3. Determine dashboard month:
   - latest month in selected-year transactions
   - or month `12` when there are no selected-year transactions
4. Determine previous comparison month:
   - dashboard month minus one calendar month
5. Load current-month transactions:
   - selected dashboard month
   - `Amount != 0`
6. Load previous-month transactions:
   - comparison month, which may be in the previous calendar year
   - `Amount != 0`
7. Build the monthly overview from current and previous month transactions.
8. Build top categories from current-month expense transactions.
9. Build top merchants from current-month expense transactions.
10. Build recurring charge candidates from selected-year expense transactions.
11. Build alert summary from current database state and selected-year transactions.
12. Build account summary from selected-year transactions.

## 8. Shared Helpers

Use helpers inside `ReportService`:

```csharp
private static decimal IncomeAmount(Transaction t) =>
    t.Amount < 0 ? Math.Abs(t.Amount) : 0m;

private static decimal ExpenseAmount(Transaction t) =>
    t.Amount > 0 ? t.Amount : 0m;

private static decimal TotalIncome(IEnumerable<Transaction> transactions) =>
    transactions.Sum(IncomeAmount);

private static decimal TotalExpenses(IEnumerable<Transaction> transactions) =>
    transactions.Sum(ExpenseAmount);

private static decimal NetCashFlow(IEnumerable<Transaction> transactions) =>
    TotalIncome(transactions) - TotalExpenses(transactions);

private static decimal Percent(decimal numerator, decimal denominator) =>
    denominator == 0 ? 0 : Math.Round(numerator / denominator * 100m, 1);

private static decimal ChangePercent(decimal current, decimal previous) =>
    previous == 0 ? 0 : Math.Round((current - previous) / Math.Abs(previous) * 100m, 1);

private static string CategoryKey(Transaction t) =>
    string.IsNullOrWhiteSpace(t.Category) ? "Unassigned" : t.Category;

private static string MonthKey(int year, int month) => $"{year}-{month:D2}";

private static string MonthLabel(int year, int month) =>
    new DateOnly(year, month, 1).ToString("MMM yyyy");
```

If other report implementations already added compatible helpers, reuse them where practical.

## 9. Monthly Overview Logic

For the dashboard month:

- `TotalSpending = TotalExpenses(currentMonthTransactions)`
- `TotalIncome = TotalIncome(currentMonthTransactions)`
- `NetCashFlow = TotalIncome - TotalSpending`
- `IncomeTransactionCount = count where Amount < 0`
- `ExpenseTransactionCount = count where Amount > 0`
- `TransactionCount = IncomeTransactionCount + ExpenseTransactionCount`

For previous month:

- `PreviousMonthSpending = TotalExpenses(previousMonthTransactions)`
- `PreviousMonthIncome = TotalIncome(previousMonthTransactions)`
- `PreviousMonthNetCashFlow = NetCashFlow(previousMonthTransactions)`

Change fields:

- `SpendingChangeAmount = TotalSpending - PreviousMonthSpending`
- `SpendingChangePercent = ChangePercent(TotalSpending, PreviousMonthSpending)`
- `IncomeChangeAmount = TotalIncome - PreviousMonthIncome`
- `IncomeChangePercent = ChangePercent(TotalIncome, PreviousMonthIncome)`
- `NetCashFlowChangeAmount = NetCashFlow - PreviousMonthNetCashFlow`
- `NetCashFlowChangePercent = ChangePercent(NetCashFlow, PreviousMonthNetCashFlow)`

## 10. Top Categories Logic

Use current-month expense transactions only:

- `Amount > 0`

Group by effective category:

- `CategoryKey(t)`

For each category:

- `Total = sum Amount`
- `Count = transaction count`
- `PctOfSpend = Total / current month total spending * 100`
- `PreviousMonthTotal = same category total in previous month expense transactions`
- `ChangeAmount = Total - PreviousMonthTotal`
- `ChangePercent = ChangePercent(Total, PreviousMonthTotal)`

Sort by `Total` descending and take five rows.

## 11. Top Merchants Logic

Use current-month expense transactions only:

- `Amount > 0`

Merchant grouping rules:

1. If `Transaction.AliasId` is not null, group by that alias id.
2. If `Transaction.AliasId` is null and `NormalizedName` is not blank, group by `NormalizedName`.
3. If both `AliasId` and `NormalizedName` are missing, group by `Name`.
4. Aliased rows display `BusinessAlias.AliasName` when available, otherwise `Transaction.Name`.
5. Unmapped rows display the most common `Name` value in that group; if tied, use the alphabetically first name.

For each merchant:

- `MerchantKey`
- `AliasId`
- `Name`
- `NormalizedName`
- `IsMapped`
- `PrimaryCategory`
- `Total = sum Amount`
- `Count = transaction count`
- `PctOfSpend = Total / current month total spending * 100`
- `PreviousMonthTotal = same merchant key total in previous month expense transactions`
- `ChangeAmount = Total - PreviousMonthTotal`
- `ChangePercent = ChangePercent(Total, PreviousMonthTotal)`

Sort by `Total` descending and take five rows.

## 12. Recurring Charge Logic

This is a lightweight heuristic, not a full subscription detector.

Use selected-year expense transactions:

- `Amount > 0`

Group by merchant key using the same merchant grouping rules as top merchants.

A group is a recurring charge candidate when:

- it has at least three transactions in the selected year
- transactions occur in at least three distinct months
- average amount is greater than zero

For each candidate:

- `LatestAmount`: latest transaction amount
- `AverageAmount`: average amount across the group
- `AmountChange`: `LatestAmount - AverageAmount`
- `OccurrenceCount`: transaction count
- `LatestDate`: latest transaction date
- `Cadence`: `"Monthly"` when distinct active months are at least `OccurrenceCount - 1`; otherwise `"Recurring"`
- `IsAmountChanged`: absolute amount change is at least max of `$5` or `10%` of average amount

Sort candidates by:

1. `IsAmountChanged` descending
2. `LatestAmount` descending
3. `Name` ascending

Take five rows.

## 13. Alerts Logic

Build alert counts and details from current database state and selected-year transactions.

### 13a. Unmatched Merchants

Count selected-year transactions where:

- `Amount != 0`
- `AliasId == null`
- `RawBusinessId != null`

Also include `RawBusinesses` where `IsMapped == false` if that data is easier and already accurate in the current branch.

Alert row:

- Severity: `"Warning"`
- Type: `"UnmatchedMerchants"`
- Title: `"Unmatched merchants"`
- Detail: `"{count} transactions are not mapped to a merchant alias."`
- Count: count

### 13b. Uncategorized Transactions

Count selected-year transactions where:

- `Amount != 0`
- `Category` is null, blank, `"Unassigned"`, or `"(uncategorized)"`

Alert row:

- Severity: `"Warning"`
- Type: `"UncategorizedTransactions"`
- Title: `"Uncategorized transactions"`
- Detail: `"{count} transactions need a category."`
- Count: count

### 13c. Possible Duplicates

Detect possible duplicates within selected-year transactions using this conservative grouping:

- same `Date`
- same `Amount`
- same normalized/display `Name`
- same `AccountId`

Any group with count greater than one contributes `count - 1` to duplicate count.

Alert row:

- Severity: `"Info"`
- Type: `"PossibleDuplicates"`
- Title: `"Possible duplicates"`
- Detail: `"{count} possible duplicate transactions found."`
- Count: count

### 13d. Rule Conflicts

There is no explicit rule-conflict model in the current code. Return `0` for `RuleConflictCount` and omit the alert row unless a future implementation adds conflict data.

### 13e. Empty Alerts

If no alert counts are greater than zero, `Items` should be an empty list.

## 14. Account Summary Logic

Use selected-year non-zero transactions grouped by `AccountId`.

For each account:

- `AccountId`
- `Income = sum abs(Amount) where Amount < 0`
- `Expenses = sum Amount where Amount > 0`
- `NetCashFlow = Income - Expenses`
- `TransactionCount`

Account name/type lookup:

1. Try `LinkedAccounts` where `AccountId` matches.
2. Try `ManualAccounts` where `Id.ToString()` matches `AccountId`.
3. Fallback `AccountName = AccountId`.
4. Fallback `AccountType = ""`.

Sort by absolute transaction volume descending:

- `(Income + Expenses)` descending

Do not attempt to compute current account balances or credit utilization unless reliable fields already exist. The current model does not expose balance/utilization fields suitable for this dashboard.

## 15. CSV Export

Add `ReportService.ExecutiveSummaryCsv(int? year = null)`.

Since the dashboard contains multiple sections, export a sectioned CSV-like text file:

```csv
Section,Metric,Value
Overview,Month,Jun 2026
Overview,TotalSpending,1234.56
Overview,TotalIncome,4000.00
Overview,NetCashFlow,2765.44

Top Categories
Category,Total,PctOfSpend,Transactions,PreviousMonthTotal,ChangeAmount,ChangePercent
...

Top Merchants
Merchant,IsMapped,Category,Total,PctOfSpend,Transactions,PreviousMonthTotal,ChangeAmount,ChangePercent
...

Recurring Charges
Merchant,Category,LatestAmount,AverageAmount,AmountChange,OccurrenceCount,LatestDate,Cadence,IsAmountChanged
...

Alerts
Severity,Type,Title,Detail,Count
...

Accounts
AccountId,AccountName,AccountType,Income,Expenses,NetCashFlow,Transactions
...
```

Use invariant culture for decimal values. Use the existing `Esc` helper for string fields. A simple multi-section CSV is acceptable for this dashboard export.

## 16. UI Page

Implement `CashOut/Pages/Reports.razor`.

The page must:

- use `@page "/reports"`
- inject `HttpClient`
- render inside `<ReportShell>`
- load available years from `api/settings/years`
- load dashboard data from `api/reports/summary?year={_year}`
- remove `IsStub="true"`
- show loading and error states through `ReportShell`
- expose CSV export through `ExportHref="@($"api/reports/summary?year={_year}&format=csv")"`

Suggested top-level page structure:

```razor
@page "/reports"
@inject HttpClient Http

<ReportShell Title="Executive Summary"
             Year="_year"
             AvailableYears="_availableYears"
             OnYearChanged="OnYearChanged"
             Loading="_loading"
             Error="_error"
             ExportHref="@($"api/reports/summary?year={_year}&format=csv")">
    @if (_summary is null)
    {
        <MudAlert Severity="Severity.Info" Variant="Variant.Outlined">
            No summary data found for @_year.
        </MudAlert>
    }
    else
    {
        <!-- overview, top categories, top merchants, recurring charges, alerts, accounts -->
    }
</ReportShell>
```

## 17. UI Content Requirements

### 17a. Monthly Overview

Render the first section as compact summary panels:

- Total spending
- Total income
- Net cash flow
- Transactions

Also show the dashboard month label from `_summary.MonthLabel`.

Use semantic coloring:

- spending increase is visually negative or warning-colored
- income increase is visually positive
- positive net cash flow is visually positive
- negative net cash flow is visually negative or warning-colored

### 17b. Top Categories

Render a compact table over `_summary.TopCategories`.

Columns:

- Category
- Total
- Percent
- Transactions
- Change

If the list is empty, show a neutral text line: `No category spending for this month.`

### 17c. Top Merchants

Render a compact table over `_summary.TopMerchants`.

Columns:

- Merchant
- Category
- Total
- Percent
- Transactions
- Change

For unmapped merchants, show the normalized name as secondary muted text when it differs from the display name.

If the list is empty, show: `No merchant spending for this month.`

### 17d. Recurring Charges

Render a compact table over `_summary.RecurringCharges`.

Columns:

- Merchant
- Category
- Latest amount
- Average amount
- Occurrences
- Latest date
- Cadence

Highlight rows where `IsAmountChanged` is true with warning text or a small warning chip.

If the list is empty, show: `No recurring charge candidates found.`

### 17e. Alerts

Render alert summary counts and alert details.

If `_summary.Alerts.Items` is empty, show a success/info alert:

```text
No report alerts for this year.
```

If there are alerts, render `MudAlert` rows or a compact table with:

- Severity
- Title
- Detail
- Count

### 17f. Account Summary

Render a compact table over `_summary.Accounts`.

Columns:

- Account
- Type
- Income
- Expenses
- Net
- Transactions

If no accounts have transactions, show: `No account activity for this year.`

## 18. UI DTO Records

Define page-local records in `Reports.razor` matching the backend JSON.

Use the same record shapes listed in section 6, but make them `private record` declarations inside the page:

```csharp
private record ExecutiveSummaryResult(...);
private record ExecutiveMonthlyOverview(...);
private record ExecutiveTopCategoryRow(...);
private record ExecutiveTopMerchantRow(...);
private record ExecutiveRecurringChargeRow(...);
private record ExecutiveAlertSummary(...);
private record ExecutiveAlertRow(...);
private record ExecutiveAccountSummaryRow(...);
```

A lesser agent should copy the full field lists from section 6 exactly.

## 19. Page Loading Flow

Use this flow:

```csharp
protected override async Task OnInitializedAsync()
{
    await LoadYears();
    await LoadSummary();
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
    await LoadSummary();
}

private async Task LoadSummary()
{
    _loading = true;
    _error = null;
    try
    {
        _summary = await Http.GetFromJsonAsync<ExecutiveSummaryResult>(
            $"api/reports/summary?year={_year}");
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
private ExecutiveSummaryResult? _summary;
```

## 20. Styling Guidance

Keep styles local to `Reports.razor` unless a reusable pattern already exists.

Use restrained utility classes and MudBlazor components. This page should feel like a financial dashboard for repeated use:

- compact
- readable
- summary-first
- table-backed
- no oversized hero UI
- no decorative gradients
- no nested cards

Acceptable styling additions:

- `.summary-grid` with responsive columns
- `.metric-panel` for simple summary panels
- `.dashboard-section`
- `.positive-change`, `.negative-change`, `.neutral-change`
- `.muted-subtext`
- `.warning-chip`

If adding CSS, put it in a `<style>` block at the bottom of `Reports.razor`.

## 21. Tests

Update `CashOut.Tests/ReportServiceTests.cs`.

### 21a. New Service Tests

Add these tests:

1. `GetExecutiveSummary_UsesLatestTransactionMonth`
   - Add transactions in January and March of 2025.
   - Assert `Month == 3`.
   - Assert `MonthKey == "2025-03"`.

2. `GetExecutiveSummary_EmptyYear_UsesDecemberWithZeroTotals`
   - Add no selected-year transactions.
   - Assert `Month == 12`.
   - Assert overview totals are zero.

3. `GetExecutiveSummary_ComputesMonthlyOverview`
   - Add current-month income `Amount = -1000`.
   - Add current-month expense `Amount = 300`.
   - Assert spending is 300.
   - Assert income is 1000.
   - Assert net is 700.

4. `GetExecutiveSummary_ComputesPreviousMonthComparison`
   - Add March 2025 net of 700.
   - Add February 2025 net of 500.
   - Assert net change amount is 200.
   - Assert net change percent is 40.

5. `GetExecutiveSummary_TopCategories_ReturnsTopFive`
   - Add six expense categories in dashboard month.
   - Assert five rows are returned.
   - Assert ordered by total descending.

6. `GetExecutiveSummary_TopMerchants_GroupsByAliasOrNormalizedName`
   - Add aliased merchant transactions and unmapped same-normalized-name transactions.
   - Assert grouping follows alias/normalized rules.

7. `GetExecutiveSummary_RecurringCharges_DetectsThreeMonthMerchant`
   - Add same merchant expense in three distinct months.
   - Assert one recurring candidate is returned.

8. `GetExecutiveSummary_Alerts_CountsUncategorizedTransactions`
   - Add selected-year transaction with category `Unassigned`.
   - Assert alert count is one and an alert item exists.

9. `GetExecutiveSummary_AccountSummary_GroupsByAccount`
   - Add transactions for two accounts.
   - Assert two account summary rows.
   - Assert income, expenses, and net are correct.

10. `ExecutiveSummaryCsv_IncludesExpectedSections`
   - Call `ExecutiveSummaryCsv(2025)`.
   - Decode UTF-8.
   - Assert it contains `Overview`, `Top Categories`, `Top Merchants`, `Alerts`, and `Accounts`.

### 21b. Optional UI Test

If the scaffold and app server are already testable in the current branch, add a Playwright UI test in `CashOut.Tests/UiTests.cs`:

```csharp
[TestMethod]
[TestCategory("UI")]
public async Task ReportsPage_ShowsExecutiveSummaryHeader()
{
    await Page.GotoAsync("http://localhost:8080/reports");
    var header = Page.GetByRole(AriaRole.Heading, new() { Name = "Executive Summary" });
    await Expect(header).ToBeVisibleAsync();
}
```

Do not make this UI test depend on seeded financial data unless the test harness already guarantees it.

## 22. Verification

Run:

```powershell
dotnet test
```

Manual checks:

- `/reports` renders "Executive Summary".
- Year picker loads from `api/settings/years`.
- Changing the year reloads the dashboard.
- Dashboard month is latest month with transactions in the selected year.
- Spending uses `Amount > 0`.
- Income uses `Amount < 0` and renders positive.
- Net cash flow equals income minus spending.
- Top categories and merchants show at most five rows each.
- Alerts appear when unmatched or uncategorized transactions exist.
- CSV export downloads from `api/reports/summary?year={year}&format=csv`.

## 23. Files to Modify

Required:

- `CashOut/Controllers/ReportsController.cs`
- `CashOut/Services/ReportService.cs`
- `CashOut/Pages/Reports.razor`
- `CashOut.Tests/ReportServiceTests.cs`

Usually unchanged:

- `CashOut/Shared/ReportShell.razor`
- `CashOut/Shared/MainLayout.razor`

Only modify the unchanged files if the scaffold has not yet been applied or the existing code does not match `docs/reports-ui-scaffold-spec.md`.

No database migration is required.

## 24. Acceptance Criteria

The implementation is complete when:

- The Executive Summary page is no longer a stub.
- `GET /api/reports/summary?year={year}` returns `ExecutiveSummaryResult`.
- `GET /api/reports/summary?year={year}&format=csv` downloads CSV.
- The backend computes income from `Amount < 0` and spending from `Amount > 0`.
- The dashboard shows monthly overview, top categories, top merchants, recurring charge candidates, alerts, and account summary.
- CSV export includes all dashboard sections.
- `dotnet test` passes.
- Existing report routes and settings year loading still work.
- No database migration is added.
