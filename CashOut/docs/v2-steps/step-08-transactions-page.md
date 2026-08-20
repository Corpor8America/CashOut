# Step 08 — Transactions Page

**Goal:** Simplified Transactions.razor — remove Month Summary (GetCategorySummary dependency), remove NormalizedName column, keep category filter and transaction list.

**Prerequisites:** Steps 01–07 complete.

---

## 8.1 Transactions.razor (simplified)

**File:** `CashOut/Pages/Transactions.razor`

```razor
@page "/transactions"
@using System.Globalization
@inject HttpClient Http
@inject NavigationManager Nav

<MudText Typo="Typo.h4" GutterBottom="true">Transactions</MudText>

@if (_filterAccountName != null)
{
    <MudPaper Elevation="1" Class="mb-4 pa-4">
        <MudStack Row="true" AlignItems="AlignItems.Center" Justify="Justify.SpaceBetween">
            <MudText Typo="Typo.body1">
                <MudIcon Icon="@Icons.Material.Filled.AccountBalance" Class="mr-2" />
                Showing transactions for <strong>@_filterAccountName</strong>
            </MudText>
            <MudButton Variant="Variant.Text" Color="Color.Primary" OnClick="ClearAccountFilter"
                       StartIcon="@Icons.Material.Filled.Close">
                Show all
            </MudButton>
        </MudStack>
    </MudPaper>
}

@if (_error != null)
{
    <MudAlert Severity="Severity.Error" Variant="Variant.Filled" Class="my-4">@_error</MudAlert>
}
@if (_message != null)
{
    <MudAlert Severity="Severity.Success" Variant="Variant.Filled" Class="my-4">@_message</MudAlert>
}

@* ── Year + Month tabs row ── *@
<MudPaper Elevation="1" Class="mb-4">
    <MudStack Row="true" AlignItems="AlignItems.Center" Class="px-4 pt-3 pb-0">
        <MudSelect T="int" Label="Year" Value="_filterYear" ValueChanged="OnYearChangedInternal" Dense="true"
                   Margin="Margin.Dense" Style="width:90px; flex-shrink:0" Variant="Variant.Outlined">
            @foreach (var y in _availableYears)
            {
                <MudSelectItem Value="@y">@y</MudSelectItem>
            }
        </MudSelect>

        <MudTabs Elevation="0" Rounded="false" Color="Color.Primary" ActivePanelIndex="@_activeMonthTabIndex"
                 Class="flex-grow-1" Style="min-width:0" ActivePanelIndexChanged="OnActiveMonthChanged">
            @for (int m = 1; m <= 12; m++)
            {
                var monthIndex = m;
                <MudTabPanel Text="@CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedMonthName(m)" />
            }
        </MudTabs>

        <div style="position: relative;">
            <MudTooltip Text="Filter by category">
                <MudBadge Content="@(_selectedCategories.Count > 0 ? _selectedCategories.Count.ToString() : null)"
                          Color="Color.Primary" Overlap="true" Visible="@(_selectedCategories.Count > 0)">
                    <MudIconButton Icon="@Icons.Material.Filled.FilterList"
                                   Color="@(_selectedCategories.Count > 0 ? Color.Primary : Color.Default)"
                                   OnClick="ToggleCategoryFilter" id="cat-filter-btn" />
                </MudBadge>
            </MudTooltip>

            <MudPopover Open="_categoryPopoverOpen" AnchorOrigin="Origin.BottomRight" TransformOrigin="Origin.TopRight">
                <MudPaper Elevation="4" Style="width:260px; padding:12px">
                    <MudText Typo="Typo.subtitle2" Class="mb-2">Filter by Category</MudText>

                    @if (_categories.Count == 0)
                    {
                        <MudText Typo="Typo.body2" Color="Color.Secondary">No categories available.</MudText>
                    }
                    else
                    {
                        <div style="max-height:260px; overflow-y:auto; margin-bottom:8px">
                            @foreach (var cat in _categories)
                            {
                                var isCat = cat;
                                <MudCheckBox T="bool" Value="@_pendingCategories.Contains(isCat)"
                                             ValueChanged="@(v => ToggleCategorySelection(isCat, v))" Label="@isCat" Dense="true"
                                             Color="Color.Primary" />
                            }
                        </div>
                    }

                    <MudStack Row="true" Spacing="2">
                        <MudButton Variant="Variant.Filled" Color="Color.Primary" Size="Size.Small" OnClick="ApplyCategoryFilter">
                            Apply</MudButton>
                        <MudButton Variant="Variant.Outlined" Size="Size.Small" OnClick="ClearCategoryFilter">Clear</MudButton>
                    </MudStack>
                </MudPaper>
            </MudPopover>
        </div>
    </MudStack>
</MudPaper>

@* ── Transaction list ── *@
@if (_loading)
{
    <MudProgressLinear Color="Color.Primary" Indeterminate="true" Class="my-7" />
}
else if (_transactions.Count == 0)
{
    <MudText Color="Color.Secondary" Class="mt-4">No transactions found for this month/filter.</MudText>
}
else
{
    <MudTable Items="@_transactions" Hover="true" Breakpoint="Breakpoint.Sm" Elevation="1" Dense="true" Class="mt-2">
        <HeaderContent>
            <MudTh>Date</MudTh>
            <MudTh>Name</MudTh>
            <MudTh>Account</MudTh>
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
            <MudTd DataLabel="Account">@context.AccountName</MudTd>
            <MudTd DataLabel="Category">
                @if (_editingTransactionId == context.TransactionId)
                {
                    <MudSelect T="string" Value="@context.Category" ValueChanged="@(v => SaveCategory(context.TransactionId, v))"
                               Dense="true" Margin="Margin.None" Style="min-width:150px">
                        @foreach (var cat in _allCategories)
                        {
                            <MudSelectItem Value="@cat">@cat</MudSelectItem>
                        }
                    </MudSelect>
                }
                else
                {
                    <span @onclick="() => StartEditCategory(context.TransactionId)"
                          style="cursor:pointer; @(string.IsNullOrEmpty(context.Category) ? "color:var(--mud-palette-text-secondary);font-style:italic" : "")">
                        @(string.IsNullOrEmpty(context.Category) ? "(uncategorized)" : context.Category)
                    </span>
                }
            </MudTd>
            <MudTd DataLabel="Amount" Style="text-align:right">
                <MudText Color="@(isCredit ? Color.Success : Color.Error)" Style="font-weight:bold">
                    @displayAmount.ToString("C")
                </MudText>
            </MudTd>
        </RowTemplate>
    </MudTable>

    <MudText Typo="Typo.caption" Color="Color.Secondary" Class="mt-2">
        Showing @_transactions.Count transaction(s) for @CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(_filterMonth) @_filterYear
    </MudText>
}

@code {
    private List<TransactionDto> _transactions = new();
    private List<string> _categories = new();
    private List<string> _allCategories = new();
    private List<int> _availableYears = new() { DateTime.Now.Year };
    private bool _loading = true;
    private string? _error, _message;

    private int _filterYear = DateTime.Now.Year;
    private int _filterMonth = DateTime.Now.Month;
    private int _activeMonthTabIndex;
    private string? _filterAccountId;
    private string? _filterAccountName;

    private bool _categoryPopoverOpen;
    private HashSet<string> _selectedCategories = new();
    private HashSet<string> _pendingCategories = new();

    private string? _editingTransactionId;

    protected override async Task OnInitializedAsync()
    {
        await LoadYears();
        await LoadCategories();

        var uri = new Uri(Nav.Uri);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
        if (query.TryGetValue("accountId", out var accountId))
        {
            _filterAccountId = accountId;
            await ResolveAccountName();
        }

        await LoadTransactions();
    }

    private async Task LoadYears()
    {
        try
        {
            _availableYears = await Http.GetFromJsonAsync<List<int>>("api/settings/years") ?? _availableYears;
            if (_availableYears.Count > 0 && !_availableYears.Contains(_filterYear))
                _filterYear = _availableYears[0];
        }
        catch
        {
            _availableYears = Enumerable.Range(DateTime.Now.Year - 6, 7).OrderByDescending(y => y).ToList();
        }
    }

    private async Task LoadCategories()
    {
        try
        {
            _allCategories = await Http.GetFromJsonAsync<List<string>>("api/settings/categories") ?? new();
            _categories = new List<string>(_allCategories);
        }
        catch { /* ok */ }
    }

    private async Task ResolveAccountName()
    {
        if (string.IsNullOrEmpty(_filterAccountId)) return;
        try
        {
            var linked = await Http.GetFromJsonAsync<List<LinkedAccountDto>>("api/accounts") ?? new();
            var match = linked.FirstOrDefault(a => a.AccountId == _filterAccountId || a.Id.ToString() == _filterAccountId);
            if (match != null) { _filterAccountName = match.Name; return; }

            var manual = await Http.GetFromJsonAsync<List<ManualAccountDto>>("api/manual-accounts") ?? new();
            var manualMatch = manual.FirstOrDefault(a => a.Id.ToString() == _filterAccountId);
            if (manualMatch != null) _filterAccountName = manualMatch.Name;
        }
        catch { /* fall back to showing the raw AccountId */ }
    }

    private void ClearAccountFilter()
    {
        _filterAccountId = null;
        _filterAccountName = null;
        Nav.NavigateTo("/transactions");
        _ = LoadTransactions();
    }

    private async Task OnYearChangedInternal(int year)
    {
        _filterYear = year;
        await LoadTransactions();
    }

    private async Task OnActiveMonthChanged(int index)
    {
        _activeMonthTabIndex = index;
        _filterMonth = index + 1;
        await LoadTransactions();
    }

    private async Task LoadTransactions()
    {
        _loading = true;
        _error = null;
        try
        {
            var url = $"api/transactions?year={_filterYear}&month={_filterMonth}";
            if (!string.IsNullOrEmpty(_filterAccountId))
                url += $"&accountId={Uri.EscapeDataString(_filterAccountId)}";
            if (_selectedCategories.Count > 0)
            {
                foreach (var cat in _selectedCategories)
                    url += $"&category={Uri.EscapeDataString(cat)}";
            }

            _transactions = await Http.GetFromJsonAsync<List<TransactionDto>>(url) ?? new();
        }
        catch (Exception ex) { _error = ex.Message; }
        finally { _loading = false; }
    }

    // ── Category Filter ──────────────────────────────────────────────────

    private void ToggleCategoryFilter()
    {
        _pendingCategories = new HashSet<string>(_selectedCategories);
        _categoryPopoverOpen = !_categoryPopoverOpen;
    }

    private void ToggleCategorySelection(string category, bool selected)
    {
        if (selected) _pendingCategories.Add(category);
        else _pendingCategories.Remove(category);
    }

    private async Task ApplyCategoryFilter()
    {
        _selectedCategories = new HashSet<string>(_pendingCategories);
        _categoryPopoverOpen = false;
        await LoadTransactions();
    }

    private async Task ClearCategoryFilter()
    {
        _selectedCategories.Clear();
        _pendingCategories.Clear();
        _categoryPopoverOpen = false;
        await LoadTransactions();
    }

    // ── Category Editing ─────────────────────────────────────────────────

    private void StartEditCategory(string transactionId)
    {
        _editingTransactionId = transactionId;
    }

    private async Task SaveCategory(string transactionId, string? category)
    {
        _error = null;
        try
        {
            var resp = await Http.PatchAsJsonAsync(
                $"api/transactions/{transactionId}/category",
                new { Category = category ?? "" });
            resp.EnsureSuccessStatusCode();

            var idx = _transactions.FindIndex(t => t.TransactionId == transactionId);
            if (idx >= 0)
            {
                _transactions[idx] = _transactions[idx] with { Category = category ?? "" };
            }

            if (!string.IsNullOrEmpty(category) && !_allCategories.Contains(category))
                _allCategories.Add(category);

            _editingTransactionId = null;
        }
        catch (Exception ex) { _error = ex.Message; }
    }

    // ── DTOs ─────────────────────────────────────────────────────────────

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
- Removed: Month Summary expansion panel (depended on `GetCategorySummary`)
- Removed: `NormalizedName` column from transaction table
- Removed: `_categorySummary` state and related loading logic
- Kept: Year/month tabs, category filter popover, inline category editing, transaction table

## 8.2 Verify build

```bash
dotnet build CashOut/CashOut.csproj
```

---

## Verification

1. No `GetCategorySummary` API call in the page
2. No `NormalizedName` in the transaction table
3. Category filter popover still works
4. Inline category editing via PATCH endpoint still works
5. `dotnet build` succeeds
