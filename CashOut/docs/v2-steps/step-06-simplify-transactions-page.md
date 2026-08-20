# Step 6: Simplify Transactions Page

## Goal

Remove the Month Summary expansion panel from the Transactions page and make it a pure transaction browser. Also remove inline category editing.

## File: `CashOut/Pages/Transactions.razor`

### Remove the Month Summary Panel (lines 97-187)

Delete the entire `@* ── Month summary ── *@` section:

```razor
@* ── Month summary ── *@
@if (_summaryLoading)
{
    <MudProgressLinear Color="Color.Secondary" Indeterminate="true" Class="mt-2" />
}
else if (_categorySummary.Count > 0)
{
    <MudExpansionPanels Elevation="1" Class="mb-6">
        <MudExpansionPanel Text="Month Summary" Expanded="true">
            @* ... entire expansion panel content ... *@
        </MudExpansionPanel>
    </MudExpansionPanels>
}

<style>
    .footer-row-secondary .mud-table-cell { ... }
    .footer-row-main .mud-table-cell { ... }
</style>
```

### Remove Inline Category Editing from the Transaction Table (lines 216-236)

Replace the Category column cell template. Currently it has an edit-on-dblclick pattern:

```razor
<MudTd DataLabel="Category">
    @if (_editingId == context.TransactionId)
    {
        <div style="display:flex;gap:0.35rem;align-items:center">
            <MudTextField T="string" Value="@_editValue" ValueChanged="@(v => _editValue = v)"
                          OnKeyDown="@(e => OnEditKey(e, context.TransactionId))" Margin="Margin.Dense"
                          Variant="Variant.Outlined" />
            <MudIconButton Icon="@Icons.Material.Filled.Check" Color="Color.Success" Size="Size.Small"
                           OnClick="() => CommitEdit(context.TransactionId)" />
            <MudIconButton Icon="@Icons.Material.Filled.Close" Color="Color.Error" Size="Size.Small"
                           OnClick="CancelEdit" />
        </div>
    }
    else
    {
        <span @ondblclick="() => StartEdit(context.TransactionId, context.Category)" title="Double-click to edit"
              style="cursor:pointer;display:inline-block;min-width:60px;
            border-bottom:1px dashed #ccc;padding-bottom:1px">
            @(string.IsNullOrEmpty(context.Category) ? "—" : context.Category)
        </span>
    }
</MudTd>
```

**Replace with read-only display:**

```razor
<MudTd DataLabel="Category">
    @(string.IsNullOrEmpty(context.Category) ? "—" : context.Category)
</MudTd>
```

### Remove the `LoadSummary()` Method and Its Call

**Delete the `LoadSummary()` method (lines 393-405):**

```csharp
private async Task LoadSummary()
{
    _summaryLoading = true;
    try
    {
        var url = $"api/reports/category-summary?year={_filterYear}&month={_filterMonth}";
        if (!string.IsNullOrEmpty(_filterAccountId))
            url += $"&accountId={Uri.EscapeDataString(_filterAccountId)}";
        _categorySummary = await Http.GetFromJsonAsync<List<CategorySummaryDto>>(url) ?? new();
    }
    catch { /* ignored */ }
    finally { _summaryLoading = false; }
}
```

**Remove `await LoadSummary()` calls from:**
- `OnInitializedAsync()` (line 282): remove `await LoadSummary();`
- `OnYearChangedInternal()` (line 309): remove `await LoadSummary();`
- `OnActiveMonthChanged()` (line 323): remove `await LoadSummary();`
- `ClearAccountFilter()` (line 364): remove `await LoadSummary();`

### Remove Inline Category Edit Methods

**Delete these methods (lines 427-475):**

```csharp
private void StartEdit(string transactionId, string currentCategory) { ... }
private void CancelEdit() { ... }
private async Task CommitEdit(string transactionId) { ... }
private async Task OnEditKey(KeyboardEventArgs e, string transactionId) { ... }
```

### Remove Unused Fields and DTOs

**Remove these fields:**

```csharp
private List<CategorySummaryDto> _categorySummary = new();  // line 251
private bool _summaryLoading;                                 // line 262
private string? _message;                                     // line 263
private string? _editingId;                                   // line 270
private string _editValue = "";                               // line 271
```

**Remove the `CategorySummaryDto` record (lines 491-498):**

```csharp
private record CategorySummaryDto(
    string Category,
    decimal MonthDebit,
    decimal MonthCredit,
    decimal MonthNet,
    decimal AvgNet,
    decimal AvgDebit,
    decimal AvgCredit);
```

**Remove the `FormatCurrency` helper method (lines 500-506):**

```csharp
private string FormatCurrency(decimal value)
{
    if (value < 0)
        return $"-{Math.Abs(value).ToString("C")}";
    return value.ToString("C");
}
```

## What the Page Looks Like After

The page retains:
- Year dropdown + 12 month tabs
- Category filter popover (unchanged)
- Transaction table with: Date, Merchant (raw Name), Account, Category (read-only), Amount
- Account filter banner (if navigated from account detail)

Removed:
- Month Summary expansion panel (category breakdown + 12-month averages)
- Inline category editing (double-click to edit)
- All category-summary API calls

## Verification

```bash
dotnet build CashOut/CashOut.csproj
```

Navigate to `/transactions` — should show only the year/month tabs, category filter, and transaction table with no summary panel and no editable category cells.
