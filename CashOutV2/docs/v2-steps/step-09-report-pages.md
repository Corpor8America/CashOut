# Step 09 — Report Pages

**Goal:** Simplified ReportCategory.razor (remove trailing-12-month columns, remove NormalizedName) and ReportCashFlow.razor (remove NormalizedName from transaction rows). Keep ReportShell.razor unchanged.

**Prerequisites:** Steps 01–08 complete.

---

## 9.1 ReportCategory.razor (simplified)

**File:** `CashOut/Pages/ReportCategory.razor`

```razor
@page "/reports/category"
@inject HttpClient Http

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

    @if (_report is null || _report.Categories.Count == 0)
    {
        <MudAlert Severity="Severity.Info" Variant="Variant.Outlined">
            No category spending found for @(_month.HasValue ? $"{CashOut.Helpers.DateHelper.MonthName(_month.Value)} {_year}" : _year.ToString()).
        </MudAlert>
    }
    else
    {
        <div class="report-summary-grid">
            <MudPaper Class="metric-panel pa-4" Elevation="1">
                <MudText Typo="Typo.body2" Color="Color.Secondary">Total Spend</MudText>
                <MudText Typo="Typo.h5">@_report.GrandTotal.ToString("C")</MudText>
            </MudPaper>
            <MudPaper Class="metric-panel pa-4" Elevation="1">
                <MudText Typo="Typo.body2" Color="Color.Secondary">Transactions</MudText>
                <MudText Typo="Typo.h5">@_report.TransactionCount</MudText>
            </MudPaper>
            <MudPaper Class="metric-panel pa-4" Elevation="1">
                <MudText Typo="Typo.body2" Color="Color.Secondary">Change vs @_report.PreviousYear</MudText>
                <MudText Typo="Typo.h5" Class="@ChangeColorClass(_report.ChangeAmount)">
                    @(_report.ChangeAmount.ToString("C"))
                    (@_report.ChangePercent.ToString("F1")%)
                </MudText>
            </MudPaper>
        </div>

        <MudTable T="CategoryReportRow"
                  Items="@_report.Categories"
                  Dense="true"
                  Hover="true"
                  Breakpoint="Breakpoint.Sm"
                  Elevation="0"
                  OnRowClick="OnCategoryRowClick"
                  RowClassFunc="GetRowClass"
                  Class="my-4">
            <ColGroup>
                <col style="width: 30%" />
                <col style="width: 15%" />
                <col style="width: 10%" />
                <col style="width: 10%" />
                <col style="width: 10%" />
                <col style="width: 25%" />
            </ColGroup>
            <HeaderContent>
                <MudTh><MudTableSortLabel SortBy="new Func<CategoryReportRow, object>(r => r.Category)">Category</MudTableSortLabel></MudTh>
                <MudTh Style="text-align:right"><MudTableSortLabel InitialDirection="SortDirection.Descending" SortBy="new Func<CategoryReportRow, object>(r => r.Total)">Total</MudTableSortLabel></MudTh>
                <MudTh Style="text-align:right">% of Spend</MudTh>
                <MudTh Style="text-align:right">Transactions</MudTh>
                <MudTh Style="text-align:right">@_report.PreviousYear</MudTh>
                <MudTh Style="text-align:right">Change</MudTh>
            </HeaderContent>
            <RowTemplate>
                <MudTd DataLabel="Category">@context.Category</MudTd>
                <MudTd DataLabel="Total" Style="text-align:right">@context.Total.ToString("C")</MudTd>
                <MudTd DataLabel="%" Style="text-align:right">@context.PctOfSpend.ToString("F1")%</MudTd>
                <MudTd DataLabel="Txns" Style="text-align:right">@context.Count</MudTd>
                <MudTd DataLabel="Prev Yr" Style="text-align:right">@context.PreviousTotal.ToString("C")</MudTd>
                <MudTd DataLabel="Change" Style="text-align:right" Class="@ChangeColorClass(context.ChangeAmount)">
                    @(context.ChangeAmount.ToString("C"))
                    (@context.ChangePercent.ToString("F1")%)
                </MudTd>
            </RowTemplate>
        </MudTable>

        @if (_selectedCategory is not null)
        {
            <MudText Typo="Typo.h6" Class="mt-4 mb-2">
                @_selectedCategory.Category
                (@_selectedCategory.Total.ToString("C"), @_selectedCategory.Count transactions)
            </MudText>

            @if (_selectedCategory.Transactions.Count == 0)
            {
                <MudText Typo="Typo.body2" Color="Color.Secondary">
                    No transactions for this category in @_year.
                </MudText>
            }
            else
            {
                <MudTable Items="@_selectedCategory.Transactions"
                          Dense="true"
                          Hover="true"
                          Breakpoint="Breakpoint.Sm"
                          Elevation="0">
                    <ColGroup>
                        <col style="width: 15%" />
                        <col style="width: 40%" />
                        <col style="width: 15%" />
                        <col style="width: 15%" />
                        <col style="width: 15%" />
                    </ColGroup>
                    <HeaderContent>
                        <MudTh>Date</MudTh>
                        <MudTh>Name</MudTh>
                        <MudTh>Source</MudTh>
                        <MudTh Style="text-align:right">Amount</MudTh>
                        <MudTh>Raw Name</MudTh>
                    </HeaderContent>
                    <RowTemplate>
                        <MudTd DataLabel="Date">@context.Date.ToString("MMM dd, yyyy")</MudTd>
                        <MudTd DataLabel="Name">@context.Name</MudTd>
                        <MudTd DataLabel="Source">@context.Source</MudTd>
                        <MudTd DataLabel="Amount" Style="text-align:right">@context.Amount.ToString("C")</MudTd>
                        <MudTd DataLabel="Raw Name">@context.RawName</MudTd>
                    </RowTemplate>
                </MudTable>
            }
        }
        else
        {
            <MudText Typo="Typo.body2" Color="Color.Secondary" Class="mt-4">
                Select a category to view transactions.
            </MudText>
        }
    }
</ReportShell>

<style>
    .report-summary-grid {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
        gap: 16px;
        margin-bottom: 24px;
    }
    .metric-panel {
        border-radius: 8px;
    }
    .positive-change {
        color: #4caf50;
    }
    .negative-change {
        color: #f44336;
    }
    .neutral-change {
        color: inherit;
    }
    ::deep .selected-row {
        background-color: rgba(46, 125, 50, 0.08);
    }
    ::deep .selected-row:hover {
        background-color: rgba(46, 125, 50, 0.12);
    }
</style>

@code {
    private int _year = DateTime.Now.Year;
    private int? _month;
    private List<int> _availableYears = new() { DateTime.Now.Year };
    private bool _loading;
    private string? _error;
    private CategoryReportResult? _report;
    private CategoryReportRow? _selectedCategory;

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
        _month = null;
        await LoadReport();
    }

    private async Task OnMonthChanged(int? month)
    {
        _month = month;
        await LoadReport();
    }

    private string ExportUrl =>
        _month.HasValue
            ? $"api/reports/category?year={_year}&month={_month}&format=csv"
            : $"api/reports/category?year={_year}&format=csv";

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

    private void OnCategoryRowClick(TableRowClickEventArgs<CategoryReportRow> args)
    {
        _selectedCategory = args.Item == _selectedCategory ? null : args.Item;
    }

    private string GetRowClass(CategoryReportRow row, int index)
    {
        return row == _selectedCategory ? "selected-row" : "";
    }

    private string ChangeColorClass(decimal amount) => amount switch
    {
        > 0 => "negative-change",
        < 0 => "positive-change",
        _ => "neutral-change"
    };

    // ── UI DTOs (match server DTOs from ReportDtos.cs) ───────────────────

    private record CategoryReportResult(
        int Year, int PreviousYear,
        decimal GrandTotal, decimal PreviousGrandTotal,
        decimal ChangeAmount, decimal ChangePercent,
        int TransactionCount,
        List<CategoryReportRow> Categories);

    private record CategoryReportRow(
        string Category, decimal Total, int Count, decimal PctOfSpend,
        decimal PreviousTotal, int PreviousCount,
        decimal ChangeAmount, decimal ChangePercent,
        List<CategoryTransactionRow> Transactions);

    private record CategoryTransactionRow(
        string TransactionId, string AccountId, DateOnly Date,
        string Name, string RawName,
        decimal Amount, decimal? Debit, decimal? Credit,
        string Category, TransactionSource Source);
}
```

**Changes from v1:**
- Removed: 12-Mo Trailing Spend column (`TwelveMonthAverage`)
- Removed: Vs Trailing Avg column (`VsTwelveMonthAverageAmount`, `VsTwelveMonthAveragePercent`)
- Removed: `NormalizedName` from transaction detail table
- Removed: `TwelveMonthAverage`, `TwelveMonthTotal`, `TwelveMonthCount` from CategoryReportRow DTO
- Field names changed: `TotalSpend` → `GrandTotal`, `TotalChangeAmount` → `ChangeAmount`, `TotalChangePercent` → `ChangePercent`

## 9.2 ReportCashFlow.razor (simplified)

**File:** `CashOut/Pages/ReportCashFlow.razor`

```razor
@page "/reports/cashflow"
@inject HttpClient Http

<ReportShell Title="Inflow vs Outflow"
             Year="_year"
             AvailableYears="_availableYears"
             OnYearChanged="OnYearChanged"
             Month="_month"
             OnMonthChanged="OnMonthCashFlowChanged"
             ShowMonthPicker="true"
             Loading="_loading"
             Error="@_error"
             ExportHref="@($"api/reports/cashflow?year={_year}&format=csv")">
    @if (_report is null)
    {
        <MudAlert Severity="Severity.Info" Variant="Variant.Outlined">
            No cash flow data found for @_year.
        </MudAlert>
    }
    else
    {
        <div class="report-summary-grid">
            <div class="metric-panel">
                <div class="metric-label">Total Income</div>
                <div class="metric-value income-value">@_report.TotalIncome.ToString("C")</div>
            </div>
            <div class="metric-panel">
                <div class="metric-label">Total Expenses</div>
                <div class="metric-value expense-value">@_report.TotalExpenses.ToString("C")</div>
            </div>
            <div class="metric-panel">
                <div class="metric-label">Net Cash Flow</div>
                <div class="metric-value @NetClass(_report.NetCashFlow)">@_report.NetCashFlow.ToString("C")</div>
                <div class="metric-sub @NetClass(_report.NetCashFlow)">
                    vs prev year: @(_report.NetChangeAmount >= 0 ? "+" : "")@_report.NetChangeAmount.ToString("C")
                    (@(_report.NetChangePercent >= 0 ? "+" : "")@_report.NetChangePercent.ToString("F1")%)
                </div>
            </div>
            <div class="metric-panel">
                <div class="metric-label">Avg Monthly Net</div>
                <div class="metric-value @NetClass(_report.AverageMonthlyNet)">@_report.AverageMonthlyNet.ToString("C")</div>
                <div class="metric-sub">Best: @_report.BestMonthLabel — @_report.BestMonthNet.ToString("C")</div>
                <div class="metric-sub">Worst: @_report.WorstMonthLabel — @_report.WorstMonthNet.ToString("C")</div>
            </div>
        </div>

        <MudTable T="CashFlowMonthRow"
                  Items="@_report.Months"
                  Dense="true"
                  Hover="true"
                  Breakpoint="Breakpoint.Sm"
                  Elevation="0"
                  OnRowClick="OnMonthRowClick"
                  RowClassFunc="GetRowClass"
                  Class="mt-4">
            <ColGroup>
                <col style="width: 14%" />
                <col style="width: 14%" />
                <col style="width: 14%" />
                <col style="width: 12%" />
                <col style="width: 14%" />
                <col style="width: 14%" />
                <col style="width: 12%" />
                <col style="width: 6%" />
            </ColGroup>
            <HeaderContent>
                <MudTh>Month</MudTh>
                <MudTh Class="right-align">Income</MudTh>
                <MudTh Class="right-align">Expenses</MudTh>
                <MudTh Class="right-align">Net</MudTh>
                <MudTh Class="right-align">
                    3-Mo Trailing Net
                    <MudTooltip Text="Trailing 3-month average of net cash flow.">
                        <MudIcon Icon="@Icons.Material.Filled.Info" Size="Size.Small" Class="ml-1" Style="vertical-align:middle; opacity:0.6" />
                    </MudTooltip>
                </MudTh>
                <MudTh Class="right-align">Prev Year Net</MudTh>
                <MudTh Class="right-align">Change</MudTh>
                <MudTh Class="right-align">Txns</MudTh>
            </HeaderContent>
            <RowTemplate Context="month">
                <MudTd DataLabel="Month">@month.Label</MudTd>
                <MudTd DataLabel="Income" Class="right-align income-value">@month.Income.ToString("C")</MudTd>
                <MudTd DataLabel="Expenses" Class="right-align expense-value">@month.Expenses.ToString("C")</MudTd>
                <MudTd DataLabel="Net" Class="@NetAlignClass(month.Net)">@month.Net.ToString("C")</MudTd>
                <MudTd DataLabel="3-Mo Trailing Net" Class="right-align">@month.RollingAverageNet.ToString("C")</MudTd>
                <MudTd DataLabel="Prev Year Net" Class="right-align">@month.PreviousYearNet.ToString("C")</MudTd>
                <MudTd DataLabel="Change" Class="@ChangeAlignClass(month.ChangeAmount)">
                    @(month.ChangeAmount >= 0 ? "+" : "")@month.ChangeAmount.ToString("C")
                    <span class="change-pct">(@(month.ChangePercent >= 0 ? "+" : "")@month.ChangePercent.ToString("F1")%)</span>
                </MudTd>
                <MudTd DataLabel="Txns" Class="right-align">@month.TransactionCount.ToString("N0")</MudTd>
            </RowTemplate>
        </MudTable>

        @if (_selectedMonth is not null)
        {
            <div class="drilldown-header mt-4">
                <span class="drilldown-title">@_selectedMonth.Label</span>
                <span class="drilldown-meta">
                    Income: @_selectedMonth.Income.ToString("C") —
                    Expenses: @_selectedMonth.Expenses.ToString("C") —
                    Net: @_selectedMonth.Net.ToString("C") —
                    @_selectedMonth.TransactionCount.ToString("N0") transactions
                </span>
            </div>

            @if (_selectedMonth.Transactions.Count == 0)
            {
                <MudText Typo="Typo.body2" Class="mt-2">No transactions for this month.</MudText>
            }
            else
            {
                <MudTable Items="@_selectedMonth.Transactions"
                          Dense="true"
                          Hover="true"
                          Breakpoint="Breakpoint.Sm"
                          Elevation="0"
                          Class="mt-2">
                    <HeaderContent>
                        <MudTh>Date</MudTh>
                        <MudTh>Direction</MudTh>
                        <MudTh>Name</MudTh>
                        <MudTh>Category</MudTh>
                        <MudTh>Source</MudTh>
                        <MudTh Class="right-align">Amount</MudTh>
                    </HeaderContent>
                    <RowTemplate Context="txn">
                        <MudTd DataLabel="Date">@txn.Date.ToString("MMM dd, yyyy")</MudTd>
                        <MudTd DataLabel="Direction">
                            <span class="@DirectionClass(txn.Direction)">@txn.Direction</span>
                        </MudTd>
                        <MudTd DataLabel="Name">@txn.Name</MudTd>
                        <MudTd DataLabel="Category">@txn.Category</MudTd>
                        <MudTd DataLabel="Source">@txn.Source</MudTd>
                        <MudTd DataLabel="Amount" Class="right-align">@txn.Amount.ToString("C")</MudTd>
                    </RowTemplate>
                </MudTable>
            }
        }
        else
        {
            <MudText Typo="Typo.body2" Class="mt-4">Select a month to view transactions.</MudText>
        }
    }
</ReportShell>

<style>
    .report-summary-grid {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
        gap: 16px;
        margin-bottom: 16px;
    }
    .metric-panel {
        padding: 12px 16px;
        border: 1px solid var(--mud-palette-lines-inputs, #e0e0e0);
        border-radius: 8px;
    }
    .metric-label {
        font-size: 0.8rem;
        color: var(--mud-palette-text-secondary, #757575);
        margin-bottom: 4px;
    }
    .metric-value {
        font-size: 1.5rem;
        font-weight: 600;
    }
    .metric-sub {
        font-size: 0.85rem;
        margin-top: 2px;
    }
    .income-value { color: #1b5e20; }
    .expense-value { color: #b71c1c; }
    .net-positive { color: #1b5e20; }
    .net-negative { color: #b71c1c; }
    .positive-change { color: #1b5e20; }
    .negative-change { color: #b71c1c; }
    .neutral-change { color: var(--mud-palette-text-secondary, #757575); }
    .direction-income { color: #1b5e20; font-weight: 500; }
    .direction-expense { color: #b71c1c; font-weight: 500; }
    .right-align { text-align: right; }
    .change-pct {
        display: inline-block;
        margin-left: 4px;
    }
    .drilldown-header {
        padding: 12px 0 4px 0;
        border-bottom: 1px solid var(--mud-palette-lines-inputs, #e0e0e0);
    }
    .drilldown-title {
        font-size: 1rem;
        font-weight: 600;
        margin-right: 12px;
    }
    .drilldown-meta {
        font-size: 0.85rem;
        color: var(--mud-palette-text-secondary, #757575);
    }
    ::deep .mud-table-root .right-align {
        text-align: right;
    }
    ::deep .selected-row {
        background-color: rgba(46, 125, 50, 0.08);
    }
    ::deep .selected-row:hover {
        background-color: rgba(46, 125, 50, 0.12);
    }
</style>

@code {
    private int _year = DateTime.Now.Year;
    private int? _month;
    private List<int> _availableYears = new() { DateTime.Now.Year };
    private bool _loading;
    private string? _error;
    private CashFlowReportResult? _report;
    private CashFlowMonthRow? _selectedMonth;

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
        _month = null;
        await LoadReport();
    }

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

    private void OnMonthRowClick(TableRowClickEventArgs<CashFlowMonthRow> args)
    {
        _selectedMonth = args.Item;
    }

    private string GetRowClass(CashFlowMonthRow row, int index)
    {
        return row == _selectedMonth ? "selected-row" : "";
    }

    private string NetClass(decimal value) => value switch
    {
        > 0 => "net-positive",
        < 0 => "net-negative",
        _ => "neutral-change"
    };

    private string NetAlignClass(decimal value) => $"right-align {NetClass(value)}";

    private string ChangeAlignClass(decimal value) => $"right-align {ChangeClass(value)}";

    private string ChangeClass(decimal value) => value switch
    {
        > 0 => "positive-change",
        < 0 => "negative-change",
        _ => "neutral-change"
    };

    private string DirectionClass(string direction) =>
        direction == "Income" ? "direction-income" : "direction-expense";

    // ── DTOs (match server DTOs from ReportDtos.cs) ──────────────────────

    private record CashFlowReportResult(
        int Year, int PreviousYear,
        decimal TotalIncome, decimal TotalExpenses, decimal NetCashFlow,
        decimal PreviousYearNet,
        decimal NetChangeAmount, decimal NetChangePercent,
        decimal AverageMonthlyNet,
        decimal BestMonthNet, string BestMonthLabel,
        decimal WorstMonthNet, string WorstMonthLabel,
        int TransactionCount,
        List<CashFlowMonthRow> Months);

    private record CashFlowMonthRow(
        string Month, string Label,
        decimal Income, decimal Expenses, decimal Net,
        decimal RollingAverageNet,
        decimal PreviousYearNet,
        decimal ChangeAmount, decimal ChangePercent,
        int IncomeCount, int ExpenseCount, int TransactionCount,
        List<CashFlowTransactionRow> Transactions);

    private record CashFlowTransactionRow(
        string TransactionId, string AccountId,
        DateOnly Date, string Name, string RawName,
        decimal Amount, decimal? Debit, decimal? Credit,
        string Category, TransactionSource Source, string Direction);
}
```

**Changes from v1:**
- Removed: `NormalizedName` from `CashFlowTransactionRow`
- Removed: `AliasId`, `RawBusinessId` from `CashFlowTransactionRow`
- Removed: `DisplayAmount` (use `Amount` directly)
- Renamed: `BestMonth` → `BestMonthLabel`, `WorstMonth` → `WorstMonthLabel`
- Title changed from "Net Cash Flow" → "Inflow vs Outflow"

## 9.3 Verify build

```bash
dotnet build CashOut/CashOut.csproj
```

---

## Verification

1. `ReportCategory.razor` has no trailing-12-month or Vs-Average columns
2. `ReportCashFlow.razor` transaction drilldown has no `NormalizedName` column
3. Both pages use DTOs that match the simplified server DTOs from Step 04
4. `dotnet build` succeeds
