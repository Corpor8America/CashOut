# CashOut — Reports UI Scaffold Specification

**Status:** Implementation-ready scaffold spec  
**Scope:** Replace `CashOut/Pages/Reports.razor` with a new multi-report shell. Four report panels and one Executive Summary dashboard. Reports that are not yet implemented render a clearly labelled stub.

---

## 1. Goal

The existing `Reports.razor` page mixes five very different report types into a single tab strip with a shared year filter. This works for simple tables but does not scale to the richer per-report UX described in `report-features.md` (drill-down transaction lists, period comparison, trend charts, etc.).

This spec replaces the page with a left-nav sidebar layout that gives each report its own route, its own filter bar, and its own content area. The five destinations are:

| Route | Display name | Status |
|---|---|---|
| `/reports` | Executive Summary | Stub |
| `/reports/category` | Spending by Category | **Implemented** (see separate spec) |
| `/reports/merchant` | Spending by Merchant | Stub |
| `/reports/income` | Income | Stub |
| `/reports/cashflow` | Net Cash Flow | Stub |

---

## 2. Navigation Changes

### 2a. Sidebar (`CashOut/Shared/MainLayout.razor`)

Replace the single `<MudNavLink Href="/reports" ...>Reports</MudNavLink>` entry with an expandable group:

```razor
@if (_drawerOpen)
{
    <MudText Typo="Typo.subtitle2" Color="Color.Primary" Class="ml-4 mt-4 mb-1">Reports</MudText>
}
<MudNavLink Href="/reports" Match="NavLinkMatch.All"
            Icon="@Icons.Material.Filled.Dashboard">
    Executive Summary
</MudNavLink>
<MudNavLink Href="/reports/category"
            Icon="@Icons.Material.Filled.Category">
    By Category
</MudNavLink>
<MudNavLink Href="/reports/merchant"
            Icon="@Icons.Material.Filled.Store">
    By Merchant
</MudNavLink>
<MudNavLink Href="/reports/income"
            Icon="@Icons.Material.Filled.TrendingUp">
    Income
</MudNavLink>
<MudNavLink Href="/reports/cashflow"
            Icon="@Icons.Material.Filled.SwapVert">
    Cash Flow
</MudNavLink>
```

The existing `<MudNavLink Href="/reports" ...>Reports</MudNavLink>` entry under "Data" is removed entirely — the five links above replace it.

### 2b. Index redirect

`CashOut/Pages/Reports.razor` changes its route to `/reports` and becomes the Executive Summary page. The four old tab panels are deleted. See section 4.

---

## 3. Shared Report Shell Component

Create `CashOut/Shared/ReportShell.razor`. Every report page renders inside this component so that the period-picker, export button, and error/loading states are consistent everywhere.

### Props

| Parameter | Type | Description |
|---|---|---|
| `Title` | `string` | Report name shown in the `<h4>` heading |
| `ChildContent` | `RenderFragment` | The report-specific content area |
| `OnYearChanged` | `EventCallback<int>` | Fires when the user picks a different year |
| `Year` | `int` | Currently selected year, two-way bound |
| `AvailableYears` | `List<int>` | Year options for the picker |
| `Loading` | `bool` | Shows the indeterminate progress bar when true |
| `Error` | `string?` | Shows a red alert when non-null |
| `ExportHref` | `string?` | If set, shows an "Export CSV" button linking to this URL |
| `IsStub` | `bool` | Default false. When true, hides `ChildContent` and shows the stub notice |

### Markup structure

```razor
@* CashOut/Shared/ReportShell.razor *@
<MudText Typo="Typo.h4" GutterBottom="true">@Title</MudText>

@if (Error != null)
{
    <MudAlert Severity="Severity.Error" Variant="Variant.Filled" Class="my-4">@Error</MudAlert>
}

<MudPaper Class="pa-4 mb-4" Elevation="1">
    <MudStack Row="true" AlignItems="AlignItems.Center" Spacing="4">
        <MudSelect T="int" Label="Year" Value="Year" ValueChanged="OnYearChanged"
                   Dense="true" Margin="Margin.Dense" Style="width:100px">
            @foreach (var y in AvailableYears)
            {
                <MudSelectItem Value="@y">@y</MudSelectItem>
            }
        </MudSelect>
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

@if (Loading)
{
    <MudProgressLinear Color="Color.Primary" Indeterminate="true" Class="my-4" />
}

@if (IsStub)
{
    <MudPaper Class="pa-8" Elevation="0" Style="text-align:center; border: 2px dashed var(--mud-palette-divider)">
        <MudIcon Icon="@Icons.Material.Filled.Construction"
                 Style="font-size:3rem; color:var(--mud-palette-text-secondary); margin-bottom:12px" />
        <MudText Typo="Typo.h6" Color="Color.Secondary">Coming Soon</MudText>
        <MudText Typo="Typo.body2" Color="Color.Secondary" Class="mt-2">
            This report is not yet implemented.
        </MudText>
    </MudPaper>
}
else
{
    @ChildContent
}
```

```csharp
@code {
    [Parameter] public string Title { get; set; } = "";
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public EventCallback<int> OnYearChanged { get; set; }
    [Parameter] public int Year { get; set; } = DateTime.Now.Year;
    [Parameter] public List<int> AvailableYears { get; set; } = new();
    [Parameter] public bool Loading { get; set; }
    [Parameter] public string? Error { get; set; }
    [Parameter] public string? ExportHref { get; set; }
    [Parameter] public bool IsStub { get; set; }
}
```

---

## 4. Page Files

### 4a. Executive Summary — `/reports`

**File:** `CashOut/Pages/Reports.razor` (rewritten)

This page is a stub for now. Render `<ReportShell>` with `IsStub="true"`.

```razor
@page "/reports"
@inject HttpClient Http

<ReportShell Title="Executive Summary"
             Year="_year"
             AvailableYears="_availableYears"
             OnYearChanged="y => _year = y"
             IsStub="true" />

@code {
    private int _year = DateTime.Now.Year;
    private List<int> _availableYears = new() { DateTime.Now.Year };

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _availableYears = await Http.GetFromJsonAsync<List<int>>("api/settings/years")
                              ?? _availableYears;
            if (_availableYears.Count > 0) _year = _availableYears[0];
        }
        catch { /* use defaults */ }
    }
}
```

### 4b. Spending by Category — `/reports/category`

**File:** `CashOut/Pages/ReportCategory.razor`

This is the first report to be fully implemented. Its full spec is in `docs/report-category-spec.md`. The scaffold creates the file with a stub that will be replaced:

```razor
@page "/reports/category"
@inject HttpClient Http

<ReportShell Title="Spending by Category"
             Year="_year"
             AvailableYears="_availableYears"
             OnYearChanged="OnYearChanged"
             Loading="_loading"
             Error="_error"
             ExportHref="@($"api/reports/category?year={_year}&format=csv")"
             IsStub="true" />

@code {
    private int _year = DateTime.Now.Year;
    private List<int> _availableYears = new() { DateTime.Now.Year };
    private bool _loading;
    private string? _error;

    protected override async Task OnInitializedAsync() => await LoadYears();

    private async Task LoadYears()
    {
        try
        {
            _availableYears = await Http.GetFromJsonAsync<List<int>>("api/settings/years")
                              ?? _availableYears;
            if (_availableYears.Count > 0) _year = _availableYears[0];
        }
        catch { }
    }

    private async Task OnYearChanged(int year)
    {
        _year = year;
        await Task.CompletedTask; // replaced in full implementation
    }
}
```

Remove `IsStub="true"` and add real content once the feature is implemented per `docs/report-category-spec.md`.

### 4c. Spending by Merchant — `/reports/merchant`

**File:** `CashOut/Pages/ReportMerchant.razor`

```razor
@page "/reports/merchant"
@inject HttpClient Http

<ReportShell Title="Spending by Merchant"
             Year="_year"
             AvailableYears="_availableYears"
             OnYearChanged="y => _year = y"
             IsStub="true" />

@code {
    private int _year = DateTime.Now.Year;
    private List<int> _availableYears = new() { DateTime.Now.Year };

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _availableYears = await Http.GetFromJsonAsync<List<int>>("api/settings/years")
                              ?? _availableYears;
            if (_availableYears.Count > 0) _year = _availableYears[0];
        }
        catch { }
    }
}
```

### 4d. Income — `/reports/income`

**File:** `CashOut/Pages/ReportIncome.razor`

```razor
@page "/reports/income"
@inject HttpClient Http

<ReportShell Title="Income"
             Year="_year"
             AvailableYears="_availableYears"
             OnYearChanged="y => _year = y"
             IsStub="true" />

@code {
    private int _year = DateTime.Now.Year;
    private List<int> _availableYears = new() { DateTime.Now.Year };

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _availableYears = await Http.GetFromJsonAsync<List<int>>("api/settings/years")
                              ?? _availableYears;
            if (_availableYears.Count > 0) _year = _availableYears[0];
        }
        catch { }
    }
}
```

### 4e. Net Cash Flow — `/reports/cashflow`

**File:** `CashOut/Pages/ReportCashFlow.razor`

```razor
@page "/reports/cashflow"
@inject HttpClient Http

<ReportShell Title="Net Cash Flow"
             Year="_year"
             AvailableYears="_availableYears"
             OnYearChanged="y => _year = y"
             IsStub="true" />

@code {
    private int _year = DateTime.Now.Year;
    private List<int> _availableYears = new() { DateTime.Now.Year };

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _availableYears = await Http.GetFromJsonAsync<List<int>>("api/settings/years")
                              ?? _availableYears;
            if (_availableYears.Count > 0) _year = _availableYears[0];
        }
        catch { }
    }
}
```

---

## 5. Existing Backend — What to Keep

The existing `ReportsController` and `ReportService` already expose:

| Endpoint | Used by |
|---|---|
| `GET /api/reports/category?year=` | Spending by Category (keep, extend) |
| `GET /api/reports/category?year=&format=csv` | CSV export (keep) |
| `GET /api/reports/monthly?year=` | Cash Flow (will reuse) |
| `GET /api/reports/merchants?year=&topN=` | Spending by Merchant (keep) |
| `GET /api/reports/largest?year=&topN=` | Possibly Executive Summary |
| `GET /api/reports/pivot?year=` | No longer used by any page — can be removed later |
| `GET /api/reports/category-summary?year=&month=` | Used by Transactions page — keep |
| `GET /api/settings/years` | All report pages |

Do not delete or modify any existing endpoints in this scaffold phase. The Spending by Category spec (`report-category-spec.md`) calls for a new endpoint; everything else reuses what exists.

---

## 6. `_Imports.razor` — Shared Namespace

`CashOut/Shared/ReportShell.razor` lives in the `CashOut.Shared` namespace. Confirm that `CashOut/Pages/_Imports.razor` already contains:

```razor
@using CashOut.Shared
```

It does (visible in the existing file). No change needed.

---

## 7. Verification Checklist

After implementing the scaffold:

- `dotnet build` produces zero errors.
- Navigating to `/reports` shows "Executive Summary" heading with a "Coming Soon" stub panel.
- Navigating to `/reports/merchant`, `/reports/income`, `/reports/cashflow` each show their respective "Coming Soon" stubs.
- Navigating to `/reports/category` shows the "Spending by Category" heading and a stub (until the full implementation from `report-category-spec.md` is applied).
- The sidebar shows all five report links and the old single "Reports" link is gone.
- The `<MudNavLink>` active highlight correctly highlights the current report page.
- The year picker renders on all five pages using the same data source (`api/settings/years`).
- No existing functionality on `/transactions`, `/accounts`, `/settings`, or `/merchants` is affected.

---

## 8. Files Created / Modified Summary

| Action | File |
|---|---|
| Modify | `CashOut/Shared/MainLayout.razor` — replace single Reports nav link with five links |
| Rewrite | `CashOut/Pages/Reports.razor` — Executive Summary stub |
| Create | `CashOut/Shared/ReportShell.razor` — shared shell component |
| Create | `CashOut/Pages/ReportCategory.razor` — stub, to be filled by category spec |
| Create | `CashOut/Pages/ReportMerchant.razor` — stub |
| Create | `CashOut/Pages/ReportIncome.razor` — stub |
| Create | `CashOut/Pages/ReportCashFlow.razor` — stub |

No database migrations. No new API endpoints. No changes to DI registration.