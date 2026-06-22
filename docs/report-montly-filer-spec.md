# Monthly Filter Drill-Down Reports — Implementation Spec

**Status:** Implementation-ready  
**Scope:** Add month-level filtering to all five existing report pages without adding new routes or breaking existing behavior.  
**Related scaffold:** `docs/reports-ui-scaffold-spec.md`  
**Related specs:** `docs/report-category-spec.md`, `docs/report-merchant-spec.md`, `docs/report-income-spec.md`, `docs/report-cashflow-spec.md`, `docs/report-executive-summary-spec.md`

---

## 1. Goal

Every report page currently operates at the year level: a user picks a year and sees aggregated annual data. This spec adds a **month picker** to each report page so the user can filter to any individual month within the selected year.

The month picker is an additional filter that sits alongside the existing year picker. When a month is selected, the report reloads and shows only data for that month. When no month is selected (the default), the report continues to show full-year data exactly as it does today.

No new routes are added. No new backend report methods are added for the basic filtering case — the existing endpoints already support `month` as a query parameter or can be trivially extended to do so. No existing behavior changes when no month is selected.

---

## 2. Sign Convention Reminder

All monetary logic must follow the convention in `CashOut/Models/Transaction.cs`:

- `Amount > 0` means money leaving the account (expense/debit).
- `Amount < 0` means money entering the account (income/credit/refund).
- Displayed income totals are always positive (`Math.Abs(Amount)`).
- Displayed expense totals are always positive (`Amount` when `Amount > 0`).

This is unchanged from the existing reports. The month filter does not affect the sign convention.

---

## 3. Shared Shell Change — `ReportShell.razor`

`CashOut/Shared/ReportShell.razor` must gain a month picker that all report pages can opt into.

### 3a. New Parameters

Add these parameters to the `@code` block:

```csharp
[Parameter] public int? Month { get; set; }
[Parameter] public EventCallback<int?> OnMonthChanged { get; set; }
[Parameter] public bool ShowMonthPicker { get; set; } = false;
```

### 3b. Month Picker Markup

Inside the existing `<MudPaper Class="pa-4 mb-4" Elevation="1">` filter bar, add the month picker immediately after the year `<MudSelect>`. Only render it when `ShowMonthPicker` is true:

```razor
@if (ShowMonthPicker)
{
    <MudSelect T="int?" Label="Month" Value="Month" ValueChanged="OnMonthChanged"
               Dense="true" Margin="Margin.Dense" Style="width:130px" Clearable="true">
        <MudSelectItem T="int?" Value="@((int?)null)">All months</MudSelectItem>
        <MudSelectItem T="int?" Value="@((int?)1)">January</MudSelectItem>
        <MudSelectItem T="int?" Value="@((int?)2)">February</MudSelectItem>
        <MudSelectItem T="int?" Value="@((int?)3)">March</MudSelectItem>
        <MudSelectItem T="int?" Value="@((int?)4)">April</MudSelectItem>
        <MudSelectItem T="int?" Value="@((int?)5)">May</MudSelectItem>
        <MudSelectItem T="int?" Value="@((int?)6)">June</MudSelectItem>
        <MudSelectItem T="int?" Value="@((int?)7)">July</MudSelectItem>
        <MudSelectItem T="int?" Value="@((int?)8)">August</MudSelectItem>
        <MudSelectItem T="int?" Value="@((int?)9)">September</MudSelectItem>
        <MudSelectItem T="int?" Value="@((int?)10)">October</MudSelectItem>
        <MudSelectItem T="int?" Value="@((int?)11)">November</MudSelectItem>
        <MudSelectItem T="int?" Value="@((int?)12)">December</MudSelectItem>
    </MudSelect>
}
```

The `Clearable="true"` attribute lets users return to full-year view without needing a separate button. When cleared, `OnMonthChanged` fires with `null`.

### 3c. Complete Updated Filter Bar

The filter bar paper element should look like this after the change:

```razor
<MudPaper Class="pa-4 mb-4" Elevation="1">
    <MudStack Row="true" AlignItems="AlignItems.Center" Spacing="4">
        <MudSelect T="int" Label="Year" Value="Year" ValueChanged="OnYearChanged"
                   Dense="true" Margin="Margin.Dense" Style="width:100px">
            @foreach (var y in AvailableYears)
            {
                <MudSelectItem Value="@y">@y</MudSelectItem>
            }
        </MudSelect>
        @if (ShowMonthPicker)
        {
            <MudSelect T="int?" Label="Month" Value="Month" ValueChanged="OnMonthChanged"
                       Dense="true" Margin="Margin.Dense" Style="width:130px" Clearable="true">
                <MudSelectItem T="int?" Value="@((int?)null)">All months</MudSelectItem>
                <MudSelectItem T="int?" Value="@((int?)1)">January</MudSelectItem>
                <MudSelectItem T="int?" Value="@((int?)2)">February</MudSelectItem>
                <MudSelectItem T="int?" Value="@((int?)3)">March</MudSelectItem>
                <MudSelectItem T="int?" Value="@((int?)4)">April</MudSelectItem>
                <MudSelectItem T="int?" Value="@((int?)5)">May</MudSelectItem>
                <MudSelectItem T="int?" Value="@((int?)6)">June</MudSelectItem>
                <MudSelectItem T="int?" Value="@((int?)7)">July</MudSelectItem>
                <MudSelectItem T="int?" Value="@((int?)8)">August</MudSelectItem>
                <MudSelectItem T="int?" Value="@((int?)9)">September</MudSelectItem>
                <MudSelectItem T="int?" Value="@((int?)10)">October</MudSelectItem>
                <MudSelectItem T="int?" Value="@((int?)11)">November</MudSelectItem>
                <MudSelectItem T="int?" Value="@((int?)12)">December</MudSelectItem>
            </MudSelect>
        }
        @if (!string.IsNullOrEmpty(ExportHref))
        {
            <MudSpacer />
            <MudButton Variant="Variant.Outlined" StartIcon="@Icons.Material.Filled.Download"
                       Href="@ExportHref" Target="_blank">
                Export CSV
            </MudButton>
        }
    </MudStack>
</MudPaper>
```

### 3d. What Does Not Change in ReportShell

The `IsStub`, `Loading`, `Error`, `Title`, `ChildContent`, and `AvailableYears` parameters are unchanged. The `OnYearChanged` callback behavior is unchanged. When a year changes, the consuming page is responsible for also resetting `_month` to `null` if desired.

---

## 4. Backend Changes

### 4a. Category Report — `GET /api/reports/category`

The existing endpoint accepts `year` only. Add a `month` query parameter:

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

Update `ReportService.GetByCategory` signature:

```csharp
public async Task<CategoryReportResult> GetByCategory(int? year = null, int? month = null)
```

Inside `GetByCategory`, change the `GetExpenses` call for current-year expenses:

```csharp
var currentExpenses = month.HasValue
    ? await GetExpenses(y, month.Value)
    : await GetExpenses(y);
```

Also update `CategoryCsv`:

```csharp
public async Task<byte[]> CategoryCsv(int? year = null, int? month = null)
{
    var result = await GetByCategory(year, month);
    // rest unchanged
}
```

**Important behavior when month is set — trailing 12-month average becomes a rolling window:**

When a month is selected, `trailingExpenses` loads the **12-month period ending with the selected month** (e.g., if January 2025 is selected, trailing expenses span February 2024 through January 2025). This ensures `TwelveMonthAverage` is a true trailing average, not a year-to-date figure.

Only `currentExpenses` is filtered to the selected month. `previousExpenses` remains the full previous year. `previousExpenses` is not adjusted — the previous-year comparison stays full-year so the user can see how a single month compares to the full prior year.

The `GetExpenses` call for trailing expenses when a month is selected:

```csharp
if (month.HasValue)
{
    // trailing 12 months: the 12-month period ending with the selected month
    // e.g., for Jan 2025: Feb 2024 through Jan 2025
    var trailingStart = new DateTime(y - 1, month.Value, 1).AddMonths(1);
    var trailingEnd = new DateTime(y, month.Value, 1).AddMonths(1).AddDays(-1);
    trailingExpenses = await GetExpensesInRange(trailingStart, trailingEnd);
}
else
{
    trailingExpenses = await GetExpenses(y);
}
```

*(`GetExpensesInRange` is described in section 4g.)*

**What changes in the response when month is set:**

- `TotalSpend` reflects only the selected month's expenses.
- `TransactionCount` reflects only the selected month.
- Each `CategoryReportRow.Total` and `.Count` reflects only the selected month.
- `PreviousTotal` remains the full previous year (unchanged).
- `TwelveMonthAverage` reflects the trailing 12-month period ending with the selected month.
- The `Transactions` list within each row contains only the selected month's transactions.

### 4b. Merchant Report — `GET /api/reports/merchants`

Add `month` parameter:

```csharp
[HttpGet("merchants")]
public async Task<IActionResult> Merchants(
    [FromQuery] int topN = 10, [FromQuery] int? year = null,
    [FromQuery] int? month = null,
    [FromQuery] string? format = null)
{
    if (format == "csv")
        return File(await _reports.MerchantsCsv(topN, year, month), "text/csv", "merchants.csv");
    return Ok(await _reports.GetTopMerchants(topN, year, month));
}
```

Update `ReportService.GetTopMerchants` signature:

```csharp
public async Task<MerchantReportResult> GetTopMerchants(int topN = 10, int? year = null, int? month = null)
```

Inside `GetTopMerchants`, filter `currentExpenses` by month when provided. The full inline example for the month-filtered current expenses query (mirroring the existing pattern):

```csharp
var currentExpenses = month.HasValue
    ? (excluded.Count == 0
        ? await _db.Transactions
            .Where(t => t.Date.Year == y && t.Date.Month == month.Value && t.Amount > 0)
            .Include(t => t.Alias)
            .ToListAsync()
        : await _db.Transactions
            .Where(t => t.Date.Year == y && t.Date.Month == month.Value && t.Amount > 0 && !excluded.Contains(t.Category))
            .Include(t => t.Alias)
            .ToListAsync())
    : (excluded.Count == 0
        ? await _db.Transactions
            .Where(t => t.Date.Year == y && t.Amount > 0)
            .Include(t => t.Alias)
            .ToListAsync()
        : await _db.Transactions
            .Where(t => t.Date.Year == y && t.Amount > 0 && !excluded.Contains(t.Category))
            .Include(t => t.Alias)
            .ToListAsync());
```

The `previousExpenses` load remains full-year (no month filter). `TotalSpend`, `TransactionCount`, grouping, and ranking all use only the month-filtered `currentExpenses`. Previous-year totals and per-merchant comparisons remain full-year context.

Also update `MerchantsCsv`:

```csharp
public async Task<byte[]> MerchantsCsv(int topN = 10, int? year = null, int? month = null)
{
    var result = await GetTopMerchants(topN, year, month);
    // rest unchanged
}
```

### 4c. Income Report — `GET /api/reports/income`

Add `month` parameter:

```csharp
[HttpGet("income")]
public async Task<IActionResult> Income(
    [FromQuery] int? year, [FromQuery] int? month, [FromQuery] string? format)
{
    if (format == "csv")
        return File(await _reports.IncomeCsv(year, month), "text/csv", "income.csv");
    return Ok(await _reports.GetIncome(year, month));
}
```

Update `ReportService.GetIncome` signature:

```csharp
public async Task<IncomeReportResult> GetIncome(int? year = null, int? month = null)
```

Inside `GetIncome`, add a private helper for month-filtered income transactions:

```csharp
private async Task<List<Transaction>> GetIncomeTransactions(int year, int? month)
{
    var excluded = await GetExcludedCategories();
    var query = _db.Transactions.Include(t => t.Alias).AsQueryable();
    query = query.Where(t => t.Date.Year == year && t.Amount < 0);
    if (month.HasValue)
        query = query.Where(t => t.Date.Month == month.Value);
    if (excluded.Count > 0)
        query = query.Where(t => !excluded.Contains(t.Category));
    return await query.ToListAsync();
}
```

**Remove the old `GetIncomeTransactions(int year)` overload** — the new `(int year, int? month)` signature replaces it entirely. Passing `null` for month reproduces the old full-year behavior.

Call the new overload for both current and previous income:

```csharp
var currentIncome = await GetIncomeTransactions(y, month);
var previousIncome = await GetIncomeTransactions(previousYear, null); // always full year
```

`TotalIncome`, `TransactionCount`, source grouping, and `Sources` all reflect the filtered month. `PreviousTotal` and comparison fields remain full-year.

Also update `IncomeCsv`:

```csharp
public async Task<byte[]> IncomeCsv(int? year = null, int? month = null)
{
    var result = await GetIncome(year, month);
    // rest unchanged
}
```

### 4d. Cash Flow Report — `GET /api/reports/cashflow`

The cash flow report is already monthly in nature (it returns 12 rows, one per month). When a month is selected, the response should still return all 12 rows — but only the selected month's row will contain transactions. The UI uses the drill-down panel to show transactions, which already keys off `_selectedMonth`.

To avoid a backend change for this report, the UI will handle month selection by simply auto-selecting the chosen month's row in the drill-down. No backend parameter change is needed for cash flow.

The `ExportHref` always exports 12 months regardless of month picker state — no `month` parameter is added to the cash flow CSV endpoint.

**Cash flow month selection is UI-only.** See section 5d.

### 4e. Executive Summary — `GET /api/reports/summary`

The executive summary already operates on the "latest month with data" concept. When a month is explicitly selected by the user, it should override the auto-detected dashboard month.

Add `month` parameter:

```csharp
[HttpGet("summary")]
public async Task<IActionResult> Summary(
    [FromQuery] int? year, [FromQuery] int? month, [FromQuery] string? format)
{
    if (format == "csv")
        return File(await _reports.ExecutiveSummaryCsv(year, month), "text/csv", "executive-summary.csv");
    return Ok(await _reports.GetExecutiveSummary(year, month));
}
```

Update `ReportService.GetExecutiveSummary` signature:

```csharp
public async Task<ExecutiveSummaryResult> GetExecutiveSummary(int? year = null, int? month = null)
```

Inside `GetExecutiveSummary`, replace the auto-detect logic with:

```csharp
int dashMonth;
if (month.HasValue)
{
    dashMonth = month.Value;
}
else
{
    var latestWithData = transactions
        .OrderByDescending(t => t.Date.Month)
        .Select(t => t.Date.Month)
        .FirstOrDefault();
    dashMonth = latestWithData > 0 ? latestWithData : 12;
}
```

Everything else in `GetExecutiveSummary` uses `dashMonth` already, so no further changes are needed in the service logic.

Also update `ExecutiveSummaryCsv`:

```csharp
public async Task<byte[]> ExecutiveSummaryCsv(int? year = null, int? month = null)
{
    var result = await GetExecutiveSummary(year, month);
    // rest unchanged
}
```

### 4f. Shared `GetExpenses` Helper Overload

Add a private overload of `GetExpenses` that accepts a month (used for `currentExpenses` and `previousExpenses`):

```csharp
private async Task<List<Transaction>> GetExpenses(int year, int month)
{
    var excluded = await GetExcludedCategories();
    if (excluded.Count == 0)
        return await _db.Transactions
            .Where(t => t.Date.Year == year && t.Date.Month == month && t.Amount > 0)
            .ToListAsync();
    return await _db.Transactions
        .Where(t => t.Date.Year == year && t.Date.Month == month && t.Amount > 0 && !excluded.Contains(t.Category))
        .ToListAsync();
}
```

The existing `GetExpenses(int year)` overload remains unchanged.

### 4g. Shared `GetExpensesInRange` Helper for Trailing 12-Month Window

Add a private method that loads expenses between two dates, used when a month is selected and the trailing 12-month average must be calculated:

```csharp
private async Task<List<Transaction>> GetExpensesInRange(DateTime start, DateTime end)
{
    var excluded = await GetExcludedCategories();
    if (excluded.Count == 0)
        return await _db.Transactions
            .Where(t => t.Date >= start && t.Date <= end && t.Amount > 0)
            .ToListAsync();
    return await _db.Transactions
        .Where(t => t.Date >= start && t.Date <= end && t.Amount > 0 && !excluded.Contains(t.Category))
        .ToListAsync();
}
```

---

## 5. UI Page Changes

Each report page needs three things: a `_month` field, wiring to `ReportShell`, and updated API calls. The patterns are identical across pages, so the spec is explicit for each.

### 5a. Shared `DateHelper` Utility

Create `CashOut/Helpers/DateHelper.cs` with a static `MonthName` method used by multiple pages:

```csharp
namespace CashOut.Helpers;

public static class DateHelper
{
    public static string MonthName(int month) =>
        new DateOnly(2000, month, 1).ToString("MMMM");
}
```

All page-specific specs below reference `DateHelper.MonthName` instead of defining their own copy.

### 5b. `ReportCategory.razor`

**New field:**

```csharp
private int? _month;
```

**Updated `ReportShell` opening tag:**

```razor
<ReportShell Title="Spending by Category"
             Year="_year"
             AvailableYears="_availableYears"
             OnYearChanged="OnYearChanged"
             Month="_month"
             OnMonthChanged="OnMonthChanged"
             ShowMonthPicker="true"
             Loading="_loading"
             Error="@_error"
             ExportHref="@ExportUrl">
```

**New `ExportUrl` computed property** (add to `@code`):

```csharp
private string ExportUrl =>
    _month.HasValue
        ? $"api/reports/category?year={_year}&month={_month}&format=csv"
        : $"api/reports/category?year={_year}&format=csv";
```

**New `OnMonthChanged` method:**

```csharp
private async Task OnMonthChanged(int? month)
{
    _month = month;
    await LoadReport();
}
```

**Updated `OnYearChanged`** (reset month when year changes):

```csharp
private async Task OnYearChanged(int year)
{
    _year = year;
    _month = null;
    await LoadReport();
}
```

**Updated `LoadReport` API call:**

```csharp
private async Task LoadReport()
{
    _loading = true;
    _error = null;
    try
    {
        var url = _month.HasValue
            ? $"api/reports/category?year={_year}&month={_month}"
            : $"api/reports/category?year={_year}";
        _report = await Http.GetFromJsonAsync<CategoryReportResult>(url);
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

**Updated empty state message:**

```razor
<MudAlert Severity="Severity.Info" Variant="Variant.Outlined">
    No category spending found for @(_month.HasValue ? $"{DateHelper.MonthName(_month.Value)} {_year}" : _year.ToString()).
</MudAlert>
```

**No `MonthName` helper needed on this page** — use `DateHelper.MonthName` from the shared utility (section 5a). Add `@using CashOut.Helpers` to the top of the page if not already present.

**Optional context label** (place directly above the summary metrics grid when a month is selected):

```razor
@if (_month.HasValue)
{
    <MudText Typo="Typo.caption" Color="Color.Secondary" Class="mb-2">
        Showing data for @DateHelper.MonthName(_month.Value) @_year only. Previous year total reflects full-year context; trailing 12-month average is the rolling window ending with this month.
    </MudText>
}
```

### 5c. `ReportMerchant.razor`

**New field:**

```csharp
private int? _month;
```

**Updated `ReportShell` opening tag:**

```razor
<ReportShell Title="Spending by Merchant"
             Year="_year"
             AvailableYears="_availableYears"
             OnYearChanged="OnYearChanged"
             Month="_month"
             OnMonthChanged="OnMonthChanged"
             ShowMonthPicker="true"
             Loading="_loading"
             Error="@_error"
             ExportHref="@ExportUrl">
```

**New `ExportUrl` computed property:**

```csharp
private string ExportUrl =>
    _month.HasValue
        ? $"api/reports/merchants?year={_year}&topN={_topN}&month={_month}&format=csv"
        : $"api/reports/merchants?year={_year}&topN={_topN}&format=csv";
```

**New `OnMonthChanged` method:**

```csharp
private async Task OnMonthChanged(int? month)
{
    _month = month;
    await LoadReport();
}
```

**Updated `OnYearChanged`:**

```csharp
private async Task OnYearChanged(int year)
{
    _year = year;
    _month = null;
    await LoadReport();
}
```

**Updated `LoadReport` API call:**

```csharp
private async Task LoadReport()
{
    _loading = true;
    _error = null;
    try
    {
        var topN = Math.Clamp(_topN, 1, 100);
        _topN = topN;
        var url = _month.HasValue
            ? $"api/reports/merchants?year={_year}&topN={topN}&month={_month}"
            : $"api/reports/merchants?year={_year}&topN={topN}";
        _report = await Http.GetFromJsonAsync<MerchantReportResult>(url);
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

**Updated empty state:**

```razor
<MudAlert Severity="Severity.Info" Variant="Variant.Outlined">
    No merchant spending found for @(_month.HasValue ? $"{DateHelper.MonthName(_month.Value)} {_year}" : _year.ToString()).
</MudAlert>
```

**No `MonthName` helper needed** — use `DateHelper.MonthName` from the shared utility (section 5a). Add `@using CashOut.Helpers` if not already present.

**Optional context label** (same pattern as category page, placed above summary metrics grid).

### 5d. `ReportIncome.razor`

**New field:**

```csharp
private int? _month;
```

**Updated `ReportShell` opening tag:**

```razor
<ReportShell Title="Income"
             Year="_year"
             AvailableYears="_availableYears"
             OnYearChanged="OnYearChanged"
             Month="_month"
             OnMonthChanged="OnMonthChanged"
             ShowMonthPicker="true"
             Loading="_loading"
             Error="@_error"
             ExportHref="@ExportUrl">
```

**New `ExportUrl`:**

```csharp
private string ExportUrl =>
    _month.HasValue
        ? $"api/reports/income?year={_year}&month={_month}&format=csv"
        : $"api/reports/income?year={_year}&format=csv";
```

**New `OnMonthChanged`:**

```csharp
private async Task OnMonthChanged(int? month)
{
    _month = month;
    await LoadReport();
}
```

**Updated `OnYearChanged`:**

```csharp
private async Task OnYearChanged(int year)
{
    _year = year;
    _month = null;
    await LoadReport();
}
```

**Updated `LoadReport`:**

```csharp
private async Task LoadReport()
{
    _loading = true;
    _error = null;
    try
    {
        var url = _month.HasValue
            ? $"api/reports/income?year={_year}&month={_month}"
            : $"api/reports/income?year={_year}";
        _report = await Http.GetFromJsonAsync<IncomeReportResult>(url);
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

**Updated empty state:**

```razor
<MudAlert Severity="Severity.Info" Variant="Variant.Outlined">
    No income found for @(_month.HasValue ? $"{DateHelper.MonthName(_month.Value)} {_year}" : _year.ToString()).
</MudAlert>
```

**No `MonthName` helper needed** — use `DateHelper.MonthName` from the shared utility (section 5a). Add `@using CashOut.Helpers` if not already present.

**Optional context label** (same pattern as category page, placed above summary metrics grid).

### 5e. `ReportCashFlow.razor`

The cash flow page already shows a 12-row monthly table and has a drill-down panel for a selected month. The month picker here acts as a shortcut: selecting a month auto-selects that month's row and populates the drill-down, but the full 12-month table remains visible. No API reload occurs when the month picker changes on this page.

**Important:** `_month` on this page is purely UI state — it is never sent as an API parameter. The cash flow endpoint always returns all 12 months. The month picker only controls which row's drill-down panel is shown.

**New field:**

```csharp
private int? _month;
```

**Updated `ReportShell` opening tag:**

```razor
<ReportShell Title="Net Cash Flow"
             Year="_year"
             AvailableYears="_availableYears"
             OnYearChanged="OnYearChanged"
             Month="_month"
             OnMonthChanged="OnMonthCashFlowChanged"
             ShowMonthPicker="true"
             Loading="_loading"
             Error="@_error"
             ExportHref="@($"api/reports/cashflow?year={_year}&format=csv")">
```

**New `OnMonthCashFlowChanged`** (does not reload the full report; only updates the drill-down selection):

```csharp
private void OnMonthCashFlowChanged(int? month)
{
    _month = month;
    if (month.HasValue && _report != null)
    {
        _selectedMonth = _report.Months.FirstOrDefault(m => m.Month.EndsWith($"-{month.Value:D2}"));
    }
    else if (!month.HasValue && _report != null)
    {
        _selectedMonth = _report.Months
            .LastOrDefault(m => m.TransactionCount > 0)
            ?? _report.Months.FirstOrDefault();
    }
}
```

**Updated `OnYearChanged`:**

```csharp
private async Task OnYearChanged(int year)
{
    _year = year;
    _month = null;
    await LoadReport();
}
```

**`LoadReport` is unchanged** — cash flow always fetches all 12 months.

Note: The `ExportHref` for cash flow does not include a month parameter because the CSV always exports all 12 months. This is intentional.

### 5f. `Reports.razor` (Executive Summary)

**New field:**

```csharp
private int? _month;
```

**Updated `ReportShell` opening tag:**

```razor
<ReportShell Title="Executive Summary"
             Year="_year"
             AvailableYears="_availableYears"
             OnYearChanged="OnYearChanged"
             Month="_month"
             OnMonthChanged="OnMonthChanged"
             ShowMonthPicker="true"
             Loading="_loading"
             Error="@_error"
             ExportHref="@ExportUrl">
```

**New `ExportUrl`:**

```csharp
private string ExportUrl =>
    _month.HasValue
        ? $"api/reports/summary?year={_year}&month={_month}&format=csv"
        : $"api/reports/summary?year={_year}&format=csv";
```

**New `OnMonthChanged`:**

```csharp
private async Task OnMonthChanged(int? month)
{
    _month = month;
    await LoadSummary();
}
```

**Updated `OnYearChanged`:**

```csharp
private async Task OnYearChanged(int year)
{
    _year = year;
    _month = null;
    await LoadSummary();
}
```

**Updated `LoadSummary`:**

```csharp
private async Task LoadSummary()
{
    _loading = true;
    _error = null;
    try
    {
        var url = _month.HasValue
            ? $"api/reports/summary?year={_year}&month={_month}"
            : $"api/reports/summary?year={_year}";
        _summary = await Http.GetFromJsonAsync<ExecutiveSummaryResult>(url);
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

The dashboard month label (`_summary.MonthLabel`) will automatically reflect the selected month once the backend returns the correct data, so no markup changes are needed for the heading.

---

## 6. Behavior Rules

These rules apply to all report pages uniformly.

**When no month is selected (default):**
- All behavior is identical to the current implementation.
- The month picker shows "All months".
- API calls do not include a `month` parameter.

**When a month is selected:**
- The report reloads from the API with the selected month.
- Current-period totals, counts, and transaction lists reflect only the selected month.
- Previous-year comparisons retain full-year context (not filtered to the selected month).
- For the category report, the trailing 12-month average uses a rolling 12-month window ending with the selected month (not the full year).
- The empty state message names the specific month.
- The export CSV reflects the selected month's data (except cash flow, which always exports 12 months).

**When the year changes:**
- `_month` resets to `null`.
- The report reloads as a full-year report.

**When the month picker is cleared:**
- `OnMonthChanged` is called with `null`.
- `_month` is set to `null`.
- The report reloads as a full-year report.

---

## 7. Tests

Update `CashOut.Tests/ReportServiceTests.cs`.

### 7a. Category

**`GetByCategory_MonthFilter_ReturnsOnlyThatMonthsTransactions`**

Setup: Add a January expense and a February expense for category FOOD. Call `GetByCategory(2025, 1)`. Assert `TotalSpend` equals only the January amount. Assert `Categories[0].Transactions` contains only the January transaction.

**`GetByCategory_MonthFilter_PreviousYearTotalStillUsesFullYear`**

Setup: Add three January 2025 expenses for FOOD and one July 2024 expense for FOOD. Call `GetByCategory(2025, 1)`. Assert `Categories[0].PreviousTotal` equals the full 2024 FOOD total (not filtered to January 2024).

**`GetByCategory_MonthFilter_TrailingAverageUsesRolling12Months`**

Setup: Add expenses in January 2025, February 2025, and January 2024, all for category FOOD. Call `GetByCategory(2025, 1)`. Assert `Categories[0].TwelveMonthAverage` reflects only the trailing 12 months (Feb 2024 – Jan 2025), not the full year 2025.

**`GetByCategory_MonthFilter_NoDataInMonth_ReturnsEmpty`**

Setup: Add only a March expense. Call `GetByCategory(2025, 1)`. Assert `TotalSpend == 0`. Assert `Categories` is empty or contains the row with `Total == 0`. Assert no exception is thrown.

### 7b. Merchants

**`GetTopMerchants_MonthFilter_ReturnsOnlyThatMonthsMerchants`**

Setup: Add a January expense for merchant "Store A" and a February expense for merchant "Store B". Call `GetTopMerchants(10, 2025, 1)`. Assert `Merchants.Count == 1`. Assert `Merchants[0].Name == "Store A"`.

**`GetTopMerchants_MonthFilter_PreviousYearTotalStillUsesFullYear`**

Setup: Add January 2025 and February 2025 expenses for "Store A", and a June 2024 expense for "Store A". Call `GetTopMerchants(10, 2025, 1)`. Assert `Merchants[0].PreviousTotal` equals the full 2024 total for "Store A".

### 7c. Income

**`GetIncome_MonthFilter_ReturnsOnlyThatMonthsSources`**

Setup: Add a January income transaction and a March income transaction for the same normalized source. Call `GetIncome(2025, 1)`. Assert `TotalIncome` equals only the January income amount. Assert `Sources[0].Count == 1`.

**`GetIncome_MonthFilter_PreviousYearTotalStillUsesFullYear`**

Setup: Add two income transactions in different months of 2025 and one in 2024 for the same source. Call `GetIncome(2025, 1)`. Assert `Sources[0].PreviousTotal` reflects the full 2024 total.

### 7d. Executive Summary

**`GetExecutiveSummary_MonthParameter_OverridesAutoDetect`**

Setup: Add expenses in March and June 2025. Call `GetExecutiveSummary(2025, 3)`. Assert `Month == 3`. Assert `MonthKey == "2025-03"`.

**`GetExecutiveSummary_MonthParameter_TopCategoriesReflectThatMonth`**

Setup: Add a March expense for "FOOD" and a June expense for "TRAVEL". Call `GetExecutiveSummary(2025, 3)`. Assert `TopCategories.Count == 1`. Assert `TopCategories[0].Category == "FOOD"`.

---

## 8. Files to Modify

| File | Change |
|---|---|
| `CashOut/Shared/ReportShell.razor` | Add `Month`, `OnMonthChanged`, `ShowMonthPicker` parameters and month picker markup |
| `CashOut/Controllers/ReportsController.cs` | Add `month` parameter to `Category`, `Merchants`, `Income`, and `Summary` actions |
| `CashOut/Services/ReportService.cs` | Add `month` parameter to `GetByCategory`, `GetTopMerchants`, `GetIncome`, `GetExecutiveSummary`, their CSV counterparts, `GetExpenses(int year, int month)` overload, and `GetExpensesInRange(DateTime, DateTime)` helper |
| `CashOut/Pages/ReportCategory.razor` | Add `_month`, `OnMonthChanged`, `ExportUrl`; update `LoadReport` and `OnYearChanged` |
| `CashOut/Pages/ReportMerchant.razor` | Same set of changes as category |
| `CashOut/Pages/ReportIncome.razor` | Same set of changes as category |
| `CashOut/Pages/ReportCashFlow.razor` | Add `_month`, `OnMonthCashFlowChanged`; update `OnYearChanged` |
| `CashOut/Pages/Reports.razor` | Add `_month`, `OnMonthChanged`, `ExportUrl`; update `LoadSummary` and `OnYearChanged` |
| `CashOut/Helpers/DateHelper.cs` | New file — shared `MonthName` utility |
| `CashOut.Tests/ReportServiceTests.cs` | Add the ten new tests from section 7 |

No database migration is required. No new routes are added. No new page files are created.

---

## 9. Acceptance Criteria

The implementation is complete when:

- `ReportShell` renders a month picker when `ShowMonthPicker="true"` and omits it otherwise.
- Selecting a month on any report page reloads the report filtered to that month.
- Clearing the month picker reloads as a full-year report.
- Changing the year resets the month picker to "All months" and reloads as full-year.
- Previous-year comparison fields are not affected by the month filter.
- On the category report, the trailing 12-month average reflects the rolling 12-month window ending with the selected month (not the full year).
- The category, merchant, and income CSV exports include the `month` parameter when a month is selected.
- The cash flow CSV always exports 12 months regardless of month picker state.
- The executive summary `MonthLabel` reflects the explicitly selected month when one is chosen.
- Empty state messages name the specific month when one is selected.
- All ten new tests pass.
- All existing tests continue to pass.
- `dotnet test` exits with zero failures.