# Phase 1 — Build Account-Centric Pages (Additive, Non-Breaking)

## Goal

Add a new `/accounts/{AccountId}` page with three tabs (Transactions, Cash
Flow, By Category), backed by a new `AccountReportService` and
`AccountReportsController`. **Nothing existing is deleted, renamed, or
behaviorally changed.** The old `/reports/*` pages, `/merchants` page, and
`/transactions` page all continue to work exactly as they do today.

This phase does not touch: `MerchantNormalizationService`,
`TransactionService`, `CsvImportService`, `ReportService`,
`ReportsController`, `BusinessNormalizationController`, any migration, or
any model.

## Prerequisites

None. This phase is purely additive.

## Step-by-step

### Step 1 — Create `CashOut/Services/AccountReportService.cs`

This is a brand new file. Create it with exactly this content:

```csharp
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Account-scoped reporting: cash flow and category breakdowns for a single
/// account. Deliberately independent of ReportService (which is global and
/// merchant-alias-aware) so it can be built and deleted without touching
/// that file.
/// </summary>
public class AccountReportService
{
    private readonly AppDbContext _db;
    private readonly SettingsService _settings;

    public AccountReportService(AppDbContext db, SettingsService settings)
    {
        _db = db;
        _settings = settings;
    }

    private async Task<List<string>> GetExcludedCategories() =>
        await _settings.GetExcludedCategories();

    /// <summary>
    /// Monthly income/expense/net breakdown for one account for one year.
    /// </summary>
    public async Task<AccountCashFlowResult> GetCashFlow(string accountId, int? year = null)
    {
        var y = year ?? await _settings.GetOutputYear();
        var excluded = await GetExcludedCategories();

        var query = _db.Transactions
            .Where(t => t.AccountId == accountId && t.Date.Year == y && t.Amount != 0);
        if (excluded.Count > 0)
            query = query.Where(t => !excluded.Contains(t.Category));

        var txns = await query.ToListAsync();
        var byMonth = txns.GroupBy(t => t.Date.Month).ToDictionary(g => g.Key, g => g.ToList());

        decimal totalIncome = 0, totalExpenses = 0;
        var months = new List<AccountCashFlowMonthRow>();

        for (int m = 1; m <= 12; m++)
        {
            var list = byMonth.GetValueOrDefault(m, new List<Transaction>());
            var income = list.Where(t => t.Amount < 0).Sum(t => Math.Abs(t.Amount));
            var expenses = list.Where(t => t.Amount > 0).Sum(t => t.Amount);
            var net = income - expenses;

            totalIncome += income;
            totalExpenses += expenses;

            months.Add(new AccountCashFlowMonthRow(
                Month: $"{y}-{m:D2}",
                Label: new DateOnly(y, m, 1).ToString("MMM yyyy"),
                Income: income,
                Expenses: expenses,
                Net: net,
                TransactionCount: list.Count));
        }

        return new AccountCashFlowResult(
            AccountId: accountId,
            Year: y,
            TotalIncome: totalIncome,
            TotalExpenses: totalExpenses,
            NetCashFlow: totalIncome - totalExpenses,
            TransactionCount: txns.Count,
            Months: months);
    }

    /// <summary>
    /// Category breakdown (expenses only) for one account, one year, and
    /// optionally one month. Groups directly on Transaction.Category — no
    /// merchant/alias grouping.
    /// </summary>
    public async Task<AccountCategoryResult> GetByCategory(
        string accountId, int? year = null, int? month = null)
    {
        var y = year ?? await _settings.GetOutputYear();
        var excluded = await GetExcludedCategories();

        var query = _db.Transactions
            .Where(t => t.AccountId == accountId && t.Date.Year == y && t.Amount > 0);
        if (month.HasValue)
            query = query.Where(t => t.Date.Month == month.Value);
        if (excluded.Count > 0)
            query = query.Where(t => !excluded.Contains(t.Category));

        var txns = await query.ToListAsync();
        var grandTotal = txns.Sum(t => t.Amount);

        var categories = txns
            .GroupBy(t => string.IsNullOrWhiteSpace(t.Category) ? "(uncategorized)" : t.Category)
            .Select(g =>
            {
                var total = g.Sum(t => t.Amount);
                return new AccountCategoryRow(
                    Category: g.Key,
                    Total: total,
                    Count: g.Count(),
                    PctOfSpend: grandTotal == 0 ? 0 : Math.Round(total / grandTotal * 100m, 1));
            })
            .OrderByDescending(r => r.Total)
            .ToList();

        return new AccountCategoryResult(
            AccountId: accountId,
            Year: y,
            Month: month,
            TotalSpend: grandTotal,
            TransactionCount: txns.Count,
            Categories: categories);
    }
}

public record AccountCashFlowMonthRow(
    string Month, string Label, decimal Income, decimal Expenses,
    decimal Net, int TransactionCount);

public record AccountCashFlowResult(
    string AccountId, int Year, decimal TotalIncome, decimal TotalExpenses,
    decimal NetCashFlow, int TransactionCount, List<AccountCashFlowMonthRow> Months);

public record AccountCategoryRow(string Category, decimal Total, int Count, decimal PctOfSpend);

public record AccountCategoryResult(
    string AccountId, int Year, int? Month, decimal TotalSpend,
    int TransactionCount, List<AccountCategoryRow> Categories);
```

### Step 2 — Create `CashOut/Controllers/AccountReportsController.cs`

Brand new file:

```csharp
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/accounts/{accountId}/reports")]
public class AccountReportsController : ControllerBase
{
    private readonly AccountReportService _reports;

    public AccountReportsController(AccountReportService reports) => _reports = reports;

    [HttpGet("cashflow")]
    public async Task<IActionResult> CashFlow(string accountId, [FromQuery] int? year) =>
        Ok(await _reports.GetCashFlow(accountId, year));

    [HttpGet("category")]
    public async Task<IActionResult> Category(
        string accountId, [FromQuery] int? year, [FromQuery] int? month) =>
        Ok(await _reports.GetByCategory(accountId, year, month));
}
```

### Step 3 — Register `AccountReportService` in `CashOut/Program.cs`

Find this exact line:

```csharp
builder.Services.AddScoped<ReportService>();
```

Add a new line immediately after it:

```csharp
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<AccountReportService>();
```

Do not remove the `ReportService` line — it's still used by the existing
global reports in this phase.

### Step 4 — Create `CashOut/Pages/AccountDetail.razor`

Brand new file:

```razor
@page "/accounts/{AccountId}"
@using System.Globalization
@inject HttpClient Http
@inject NavigationManager Nav

<MudText Typo="Typo.h4" GutterBottom="true">@(_accountName ?? "Account")</MudText>
<MudText Typo="Typo.body2" Class="text-muted mb-4">@AccountId</MudText>

@if (_error != null)
{
    <MudAlert Severity="Severity.Error" Variant="Variant.Filled" Class="my-4">@_error</MudAlert>
}

<MudPaper Elevation="1" Class="mb-4">
    <MudStack Row="true" AlignItems="AlignItems.Center" Class="px-4 pt-3 pb-3">
        <MudSelect T="int" Label="Year" Value="_year" ValueChanged="OnYearChanged" Dense="true"
                   Margin="Margin.Dense" Style="width:100px; flex-shrink:0" Variant="Variant.Outlined">
            @foreach (var y in _availableYears)
            {
                <MudSelectItem Value="@y">@y</MudSelectItem>
            }
        </MudSelect>
    </MudStack>
</MudPaper>

<MudTabs Elevation="1" Rounded="true" Color="Color.Primary" @bind-ActivePanelIndex="_activeTabIndex">
    <MudTabPanel Text="Transactions">
        <MudPaper Elevation="0" Class="pa-4">
            <MudTabs Elevation="0" Rounded="false" Color="Color.Secondary"
                     ActivePanelIndex="@(_month - 1)" ActivePanelIndexChanged="OnMonthTabChanged" Class="mb-4">
                @for (int m = 1; m <= 12; m++)
                {
                    <MudTabPanel Text="@CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedMonthName(m)" />
                }
            </MudTabs>

            @if (_txnLoading)
            {
                <MudProgressLinear Color="Color.Primary" Indeterminate="true" Class="my-4" />
            }
            else if (_transactions.Count == 0)
            {
                <MudText Color="Color.Secondary">No transactions for this month.</MudText>
            }
            else
            {
                <MudTable Items="@_transactions" Hover="true" Breakpoint="Breakpoint.Sm" Elevation="0" Dense="true">
                    <HeaderContent>
                        <MudTh>Date</MudTh>
                        <MudTh>Name</MudTh>
                        <MudTh>Category</MudTh>
                        <MudTh Style="text-align:right">Amount</MudTh>
                    </HeaderContent>
                    <RowTemplate>
                        @{
                            var isCredit = context.Credit.HasValue;
                            var displayAmount = isCredit ? context.Credit!.Value : (context.Debit ?? 0);
                        }
                        <MudTd DataLabel="Date">@context.Date.ToString("MMM d, yyyy")</MudTd>
                        <MudTd DataLabel="Name">@context.Name</MudTd>
                        <MudTd DataLabel="Category">@(string.IsNullOrEmpty(context.Category) ? "(uncategorized)" : context.Category)</MudTd>
                        <MudTd DataLabel="Amount" Style="text-align:right">
                            <MudText Color="@(isCredit ? Color.Success : Color.Error)" Style="font-weight:bold">
                                @displayAmount.ToString("C")
                            </MudText>
                        </MudTd>
                    </RowTemplate>
                </MudTable>
            }
        </MudPaper>
    </MudTabPanel>

    <MudTabPanel Text="Cash Flow">
        <MudPaper Elevation="0" Class="pa-4">
            @if (_cashFlowLoading)
            {
                <MudProgressLinear Color="Color.Primary" Indeterminate="true" Class="my-4" />
            }
            else if (_cashFlow is null)
            {
                <MudAlert Severity="Severity.Info" Variant="Variant.Outlined">No cash flow data for @_year.</MudAlert>
            }
            else
            {
                <div class="report-summary-grid mb-4">
                    <div class="metric-panel">
                        <div class="metric-label">Total Income</div>
                        <div class="metric-value income-value">@_cashFlow.TotalIncome.ToString("C")</div>
                    </div>
                    <div class="metric-panel">
                        <div class="metric-label">Total Expenses</div>
                        <div class="metric-value expense-value">@_cashFlow.TotalExpenses.ToString("C")</div>
                    </div>
                    <div class="metric-panel">
                        <div class="metric-label">Net Cash Flow</div>
                        <div class="metric-value @NetClass(_cashFlow.NetCashFlow)">@_cashFlow.NetCashFlow.ToString("C")</div>
                    </div>
                </div>

                <MudTable Items="@_cashFlow.Months" Dense="true" Hover="true" Breakpoint="Breakpoint.Sm" Elevation="0">
                    <HeaderContent>
                        <MudTh>Month</MudTh>
                        <MudTh Style="text-align:right">Income</MudTh>
                        <MudTh Style="text-align:right">Expenses</MudTh>
                        <MudTh Style="text-align:right">Net</MudTh>
                        <MudTh Style="text-align:right">Txns</MudTh>
                    </HeaderContent>
                    <RowTemplate>
                        <MudTd DataLabel="Month">@context.Label</MudTd>
                        <MudTd DataLabel="Income" Style="text-align:right" Class="income-value">@context.Income.ToString("C")</MudTd>
                        <MudTd DataLabel="Expenses" Style="text-align:right" Class="expense-value">@context.Expenses.ToString("C")</MudTd>
                        <MudTd DataLabel="Net" Style="text-align:right" Class="@NetClass(context.Net)">@context.Net.ToString("C")</MudTd>
                        <MudTd DataLabel="Txns" Style="text-align:right">@context.TransactionCount</MudTd>
                    </RowTemplate>
                </MudTable>
            }
        </MudPaper>
    </MudTabPanel>

    <MudTabPanel Text="By Category">
        <MudPaper Elevation="0" Class="pa-4">
            @if (_categoryLoading)
            {
                <MudProgressLinear Color="Color.Primary" Indeterminate="true" Class="my-4" />
            }
            else if (_categoryReport is null || _categoryReport.Categories.Count == 0)
            {
                <MudAlert Severity="Severity.Info" Variant="Variant.Outlined">No category spending for this period.</MudAlert>
            }
            else
            {
                <MudText Typo="Typo.body2" Class="mb-4">
                    Total spend: @_categoryReport.TotalSpend.ToString("C") across @_categoryReport.TransactionCount transaction(s).
                </MudText>

                <MudTable Items="@_categoryReport.Categories" Dense="true" Hover="true" Breakpoint="Breakpoint.Sm" Elevation="0">
                    <HeaderContent>
                        <MudTh>Category</MudTh>
                        <MudTh Style="text-align:right">Total</MudTh>
                        <MudTh Style="text-align:right">%</MudTh>
                        <MudTh Style="text-align:right">Txns</MudTh>
                    </HeaderContent>
                    <RowTemplate>
                        <MudTd DataLabel="Category">@context.Category</MudTd>
                        <MudTd DataLabel="Total" Style="text-align:right">@context.Total.ToString("C")</MudTd>
                        <MudTd DataLabel="%" Style="text-align:right">@context.PctOfSpend.ToString("F1")%</MudTd>
                        <MudTd DataLabel="Txns" Style="text-align:right">@context.Count</MudTd>
                    </RowTemplate>
                </MudTable>
            }
        </MudPaper>
    </MudTabPanel>
</MudTabs>

<style>
    .report-summary-grid {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
        gap: 16px;
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
        font-size: 1.4rem;
        font-weight: 600;
    }
    .income-value { color: #1b5e20; }
    .expense-value { color: #b71c1c; }
    .net-positive { color: #1b5e20; }
    .net-negative { color: #b71c1c; }
</style>

@code {
    [Parameter] public string AccountId { get; set; } = "";

    private string? _accountName;
    private int _year = DateTime.Now.Year;
    private int _month = DateTime.Now.Month;
    private List<int> _availableYears = new() { DateTime.Now.Year };
    private int _activeTabIndex = 0;

    private List<TransactionDto> _transactions = new();
    private bool _txnLoading;

    private AccountCashFlowDto? _cashFlow;
    private bool _cashFlowLoading;

    private AccountCategoryDto? _categoryReport;
    private bool _categoryLoading;

    private string? _error;

    protected override async Task OnInitializedAsync()
    {
        await LoadAccountName();
        await LoadYears();
        await LoadTransactions();
        await LoadCashFlow();
        await LoadCategoryReport();
    }

    private async Task LoadAccountName()
    {
        try
        {
            var linked = await Http.GetFromJsonAsync<List<LinkedAccountDto>>("api/accounts") ?? new();
            var match = linked.FirstOrDefault(a => a.AccountId == AccountId || a.Id.ToString() == AccountId);
            if (match != null) { _accountName = match.Name; return; }

            var manual = await Http.GetFromJsonAsync<List<ManualAccountDto>>("api/manual-accounts") ?? new();
            var manualMatch = manual.FirstOrDefault(a => a.Id.ToString() == AccountId);
            if (manualMatch != null) _accountName = manualMatch.Name;
        }
        catch { /* fall back to showing the raw AccountId */ }
    }

    private async Task LoadYears()
    {
        try
        {
            _availableYears = await Http.GetFromJsonAsync<List<int>>("api/settings/years") ?? _availableYears;
            if (_availableYears.Count > 0 && !_availableYears.Contains(_year))
                _year = _availableYears[0];
        }
        catch
        {
            _availableYears = Enumerable.Range(DateTime.Now.Year - 6, 7).OrderByDescending(y => y).ToList();
        }
    }

    private async Task OnYearChanged(int year)
    {
        _year = year;
        await LoadTransactions();
        await LoadCashFlow();
        await LoadCategoryReport();
    }

    private async Task OnMonthTabChanged(int newIndex)
    {
        var newMonth = newIndex + 1;
        if (_month == newMonth) return;
        _month = newMonth;
        await LoadTransactions();
        await LoadCategoryReport();
    }

    private async Task LoadTransactions()
    {
        _txnLoading = true; _error = null;
        try
        {
            var url = $"api/transactions?year={_year}&month={_month}&accountId={Uri.EscapeDataString(AccountId)}";
            _transactions = await Http.GetFromJsonAsync<List<TransactionDto>>(url) ?? new();
        }
        catch (Exception ex) { _error = ex.Message; }
        finally { _txnLoading = false; }
    }

    private async Task LoadCashFlow()
    {
        _cashFlowLoading = true;
        try
        {
            var url = $"api/accounts/{Uri.EscapeDataString(AccountId)}/reports/cashflow?year={_year}";
            _cashFlow = await Http.GetFromJsonAsync<AccountCashFlowDto>(url);
        }
        catch (Exception ex) { _error = ex.Message; }
        finally { _cashFlowLoading = false; }
    }

    private async Task LoadCategoryReport()
    {
        _categoryLoading = true;
        try
        {
            var url = $"api/accounts/{Uri.EscapeDataString(AccountId)}/reports/category?year={_year}&month={_month}";
            _categoryReport = await Http.GetFromJsonAsync<AccountCategoryDto>(url);
        }
        catch (Exception ex) { _error = ex.Message; }
        finally { _categoryLoading = false; }
    }

    private string NetClass(decimal value) => value switch
    {
        > 0 => "net-positive",
        < 0 => "net-negative",
        _ => ""
    };

    // ── DTOs (mirror server records for JSON deserialization) ──────────────
    private record TransactionDto(
        string TransactionId, string AccountId, string AccountName, DateOnly Date,
        string Name, decimal? Credit, decimal? Debit, decimal Amount, string Category);

    private record LinkedAccountDto(
        Guid Id, string AccountId, string Mask, string Name, string Subtype,
        string Institution, DateTime CreatedAt);

    private record ManualAccountDto(Guid Id, string Name, string Description, DateTime CreatedAt);

    private record AccountCashFlowMonthDto(
        string Month, string Label, decimal Income, decimal Expenses,
        decimal Net, int TransactionCount);

    private record AccountCashFlowDto(
        string AccountId, int Year, decimal TotalIncome, decimal TotalExpenses,
        decimal NetCashFlow, int TransactionCount, List<AccountCashFlowMonthDto> Months);

    private record AccountCategoryRowDto(string Category, decimal Total, int Count, decimal PctOfSpend);

    private record AccountCategoryDto(
        string AccountId, int Year, int? Month, decimal TotalSpend,
        int TransactionCount, List<AccountCategoryRowDto> Categories);
}
```

### Step 5 — Point account row-clicks at the new page

**`CashOut/Pages/Accounts.razor`** — find this exact method:

```csharp
    private void HandleRowClick(TableRowClickEventArgs<AccountDto> args)
    {
        if (args.Item is null) return;
        Nav.NavigateTo($"/transactions?accountId={args.Item.AccountId}&accountName={Uri.EscapeDataString(args.Item.Name)}");
    }
```

Replace with:

```csharp
    private void HandleRowClick(TableRowClickEventArgs<AccountDto> args)
    {
        if (args.Item is null) return;
        Nav.NavigateTo($"/accounts/{Uri.EscapeDataString(args.Item.AccountId)}");
    }
```

**`CashOut/Pages/ManualAccounts.razor`** — find this exact method:

```csharp
    private void HandleRowClick(TableRowClickEventArgs<AccountDto> args)
    {
        if (args.Item is null) return;
        Nav.NavigateTo($"/transactions?accountId={args.Item.Id}&accountName={Uri.EscapeDataString(args.Item.Name)}");
    }
```

Replace with:

```csharp
    private void HandleRowClick(TableRowClickEventArgs<AccountDto> args)
    {
        if (args.Item is null) return;
        Nav.NavigateTo($"/accounts/{args.Item.Id}");
    }
```

Do not touch anything else in either file. `/transactions` remains reachable
directly by URL and still works as before — only the row-click destination
changes.

## Verification

1. `dotnet build` — must succeed with no errors.
2. `dotnet test --filter "TestCategory!=UI"` — all existing tests must still
   pass unchanged (this phase adds no new unit tests and modifies no tested
   logic).
3. Run the app (`dotnet run` from `CashOut/`, or `docker compose -f
   docker-compose.dev.yml up -d --build`).
4. Go to `/accounts`, click a linked account row → should land on
   `/accounts/{plaid-account-id}` showing the account name, Transactions tab
   populated for the current month, Cash Flow tab, and By Category tab.
5. Go to `/manual-accounts`, click a manual account row → should land on
   `/accounts/{guid}` with the same three tabs working.
6. Confirm `/reports`, `/reports/category`, `/reports/merchant`,
   `/reports/income`, `/reports/cashflow`, `/merchants`, and `/transactions`
   (typed directly in the URL bar) all still load and work exactly as
   before — nothing regressed.
7. Switch the year dropdown on the new account page and confirm all three
   tabs reload with the new year's data.
8. Click through month tabs on the Transactions sub-tab and confirm the By
   Category tab updates to match (Cash Flow tab intentionally stays
   full-year regardless of month selection).

If all of the above pass, Phase 1 is complete. Do not proceed to Phase 2
until you've used the new account pages for a while and are satisfied with
the UX.