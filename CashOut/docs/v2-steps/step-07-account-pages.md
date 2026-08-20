# Step 07 — Account Management Pages

**Goal:** Create Accounts.razor, ManualAccounts.razor, and simplified AccountDetail.razor. Remove per-account `AccountReportsController` dependency — AccountDetail uses the global transactions list filtered by accountId.

**Prerequisites:** Steps 01–06 complete.

---

## 7.1 Accounts.razor

**File:** `CashOut/Pages/Accounts.razor`

Keep exactly as-is from the existing codebase (lines 1–306). This page has no normalization references. It handles Plaid linking, account listing, account removal, and transaction sync.

## 7.2 ManualAccounts.razor

**File:** `CashOut/Pages/ManualAccounts.razor`

Keep exactly as-is from the existing codebase (lines 1–235). No normalization references.

## 7.3 AccountDetail.razor (simplified)

**File:** `CashOut/Pages/AccountDetail.razor`

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

@if (_txnLoading)
{
    <MudProgressLinear Color="Color.Primary" Indeterminate="true" Class="my-4" />
}
else if (_transactions.Count == 0)
{
    <MudText Color="Color.Secondary">No transactions for this account in @_year.</MudText>
}
else
{
    <MudTable Items="@_transactions" Hover="true" Breakpoint="Breakpoint.Sm" Elevation="1" Dense="true">
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

@code {
    [Parameter] public string AccountId { get; set; } = "";

    private string? _accountName;
    private int _year = DateTime.Now.Year;
    private List<int> _availableYears = new() { DateTime.Now.Year };

    private List<TransactionDto> _transactions = new();
    private bool _txnLoading;
    private string? _error;

    protected override async Task OnInitializedAsync()
    {
        await LoadAccountName();
        await LoadYears();
        await LoadTransactions();
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
    }

    private async Task LoadTransactions()
    {
        _txnLoading = true; _error = null;
        try
        {
            var url = $"api/transactions?year={_year}&accountId={Uri.EscapeDataString(AccountId)}";
            _transactions = await Http.GetFromJsonAsync<List<TransactionDto>>(url) ?? new();
        }
        catch (Exception ex) { _error = ex.Message; }
        finally { _txnLoading = false; }
    }

    private record TransactionDto(
        string TransactionId, string AccountId, string AccountName, DateOnly Date,
        string Name, decimal? Credit, decimal? Debit, decimal Amount, string Category);

    private record LinkedAccountDto(
        Guid Id, string AccountId, string Mask, string Name, string Subtype,
        string Institution, DateTime CreatedAt);

    private record ManualAccountDto(Guid Id, string Name, string Description, DateTime CreatedAt);
}
```

**Changes from v1:**
- Removed: Cash Flow tab (called deleted `AccountReportsController`)
- Removed: By Category tab (called deleted `AccountReportsController`)
- Removed: Month tab bar (simplified to year filter only)
- Kept: Transaction list filtered by AccountId and Year

## 7.4 Verify build

```bash
dotnet build CashOut/CashOut.csproj
```

---

## Verification

1. `AccountDetail.razor` has no `AccountReportsController` API calls
2. `AccountDetail.razor` has no month tabs — just year filter + transaction list
3. `Accounts.razor` and `ManualAccounts.razor` unchanged from existing
4. `dotnet build` succeeds
