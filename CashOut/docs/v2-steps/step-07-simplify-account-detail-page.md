# Step 7: Simplify Account Detail Page

## Goal

Remove the Cash Flow and By Category tabs from the Account Detail page. Keep only the Transactions tab.

## File: `CashOut/Pages/AccountDetail.razor`

### Remove the Cash Flow Tab (lines 73-118)

Delete the entire `MudTabPanel` for Cash Flow:

```razor
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
                @* ... summary panels ... *@
            </div>
            <MudTable Items="@_cashFlow.Months" ...>
                @* ... monthly table ... *@
            </MudTable>
        }
    </MudPaper>
</MudTabPanel>
```

### Remove the By Category Tab (lines 120-152)

Delete the entire `MudTabPanel` for By Category:

```razor
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
            @* ... category table ... *@
        }
    </MudPaper>
</MudTabPanel>
```

### Remove the `<style>` Block (lines 155-179)

The CSS styles for `.report-summary-grid`, `.metric-panel`, `.metric-label`, `.metric-value`, `.income-value`, `.expense-value`, `.net-positive`, `.net-negative` were only used by the removed tabs. Remove them all.

### Remove the `MudTabs` Wrapper (lines 26, 153)

Since there's only one tab left (Transactions), remove the outer `MudTabs` entirely. The Transactions tab content should sit directly in the page without a tab wrapper.

**Before:**
```razor
<MudTabs Elevation="1" Rounded="true" Color="Color.Primary" @bind-ActivePanelIndex="_activeTabIndex">
    <MudTabPanel Text="Transactions">
        <MudPaper Elevation="0" Class="pa-4">
            @* month tabs + table *@
        </MudPaper>
    </MudTabPanel>
    <MudTabPanel Text="Cash Flow">...</MudTabPanel>
    <MudTabPanel Text="By Category">...</MudTabPanel>
</MudTabs>
```

**After:**
```razor
<MudPaper Elevation="1" Class="pa-4">
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
            @* ... same table as current ... *@
        </MudTable>
    }
</MudPaper>
```

### Remove Unused Fields and Methods

**Remove these fields (lines 188-199):**

```csharp
private int _activeTabIndex = 0;                                    // line 188

private AccountCashFlowDto? _cashFlow;                             // line 193
private bool _cashFlowLoading;                                     // line 194

private AccountCategoryDto? _categoryReport;                       // line 196
private bool _categoryLoading;                                     // line 197
```

**Remove these methods:**

- `LoadCashFlow()` (lines 268-278)
- `LoadCategoryReport()` (lines 280-290)
- `NetClass()` (lines 292-297)

**Remove `await LoadCashFlow()` and `await LoadCategoryReport()` calls from:**
- `OnInitializedAsync()` (lines 206-207)
- `OnYearChanged()` (lines 243-244)
- `OnMonthTabChanged()` (line 253)

**Remove the `AccountId` subtitle text (line 7):**

```razor
<MudText Typo="Typo.body2" Class="text-muted mb-4">@AccountId</MudText>
```

This shows the raw Plaid account ID which isn't user-friendly.

### Remove Unused DTOs (lines 310-322)

Delete these records:

```csharp
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
```

### Add Year Selector Back to the Transactions Tab

The current page has a year selector in `MudPaper` above the tabs. After removing the tabs wrapper, move the year selector into the same `MudPaper` as the month tabs:

```razor
<MudPaper Elevation="1" Class="pa-4 mb-4">
    <MudStack Row="true" AlignItems="AlignItems.Center" Class="mb-3">
        <MudSelect T="int" Label="Year" Value="_year" ValueChanged="OnYearChanged" Dense="true"
                   Margin="Margin.Dense" Style="width:100px; flex-shrink:0" Variant="Variant.Outlined">
            @foreach (var y in _availableYears)
            {
                <MudSelectItem Value="@y">@y</MudSelectItem>
            }
        </MudSelect>
    </MudStack>

    <MudTabs Elevation="0" Rounded="false" Color="Color.Secondary"
             ActivePanelIndex="@(_month - 1)" ActivePanelIndexChanged="OnMonthTabChanged">
        @for (int m = 1; m <= 12; m++)
        {
            <MudTabPanel Text="@CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedMonthName(m)" />
        }
    </MudTabs>
</MudPaper>

@* Transaction table directly below *@
@if (_txnLoading) { ... }
else if (_transactions.Count == 0) { ... }
else { <MudTable ...> ... </MudTable> }
```

## Verification

```bash
dotnet build CashOut/CashOut.csproj
```

Navigate to `/accounts/{id}` — should show only the year selector, month tabs, and transaction table. No Cash Flow or By Category tabs.
