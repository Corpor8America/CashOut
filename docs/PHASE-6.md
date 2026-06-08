# Phase 6 — Blazor UI (All Pages)

## Progress Tracker

- [ ] 6.1 Update `Program.cs` — register `HttpClient` for Blazor
- [ ] 6.2 Update `Shared/MainLayout.razor` — navigation shell
- [ ] 6.3 Update `wwwroot/app.css` — minimal functional styles
- [ ] 6.4 Build `Transactions.razor`
- [ ] 6.5 Build `Reports.razor`
- [ ] 6.6 Build `Settings.razor`
- [ ] 6.7 Accounts.razor already exists from Phase 3 — review and adjust if needed
- [ ] 6.8 End-to-end browser verification

---

## Context

The API and service layers are complete. This phase builds the four Blazor pages that surface all
functionality in the browser. The `Accounts.razor` page was created in Phase 3; the remaining three
pages are new.

**Design principle:** functional over decorative. Tables, buttons, and form elements — no
component library dependency. Phase 6 ships a working app; visual polish can be layered on later.

All pages follow the same pattern:
- `_loading` bool for loading state
- `_error` string? for error display
- `StateHasChanged()` called after async operations that update state outside of event handlers

---

## Task 6.1 — Update Program.cs

Add `HttpClient` registration so Blazor Server pages can call the local API.
The base address uses the app's own listening address. For dev, this is `http://localhost:8080`.
In production (Docker), the container listens on `http://+:8080`.

Add to the services section in `Program.cs`:

```csharp
// HttpClient for Blazor pages calling local API endpoints
builder.Services.AddScoped(sp =>
{
    var nav = sp.GetRequiredService<NavigationManager>();
    return new HttpClient { BaseAddress = new Uri(nav.BaseUri) };
});
```

This requires adding `@inject NavigationManager` is not needed here — the `NavigationManager` is
resolved inside the DI factory. Blazor Server's `NavigationManager` provides the correct base URI
at runtime in both dev and production.

Also add `using Microsoft.AspNetCore.Components` at the top of `Program.cs` if not already present.

---

## Task 6.2 — MainLayout.razor

Replace `Shared/MainLayout.razor` with a navigation shell:

```razor
@inherits LayoutComponentBase

<div class="layout">
    <nav class="sidebar">
        <div class="app-title">💰 Spening</div>
        <NavLink href="/accounts">Accounts</NavLink>
        <NavLink href="/transactions">Transactions</NavLink>
        <NavLink href="/reports">Reports</NavLink>
        <NavLink href="/settings">Settings</NavLink>
    </nav>
    <main class="content">
        @Body
    </main>
</div>
```

---

## Task 6.3 — wwwroot/app.css

Replace with minimal functional styles:

```css
*, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }

body {
    font-family: system-ui, -apple-system, sans-serif;
    font-size: 14px;
    background: #f9f9f9;
    color: #1a1a1a;
}

/* Layout */
.layout { display: flex; min-height: 100vh; }

.sidebar {
    width: 180px;
    background: #1e1e2e;
    color: #cdd6f4;
    padding: 1.5rem 1rem;
    display: flex;
    flex-direction: column;
    gap: 0.5rem;
    flex-shrink: 0;
}

.app-title {
    font-size: 1.1rem;
    font-weight: 700;
    color: #cba6f7;
    margin-bottom: 1rem;
}

.sidebar a {
    color: #cdd6f4;
    text-decoration: none;
    padding: 0.4rem 0.6rem;
    border-radius: 4px;
    display: block;
}

.sidebar a:hover, .sidebar a.active { background: #313244; color: #cba6f7; }

.content { flex: 1; padding: 2rem; }

/* Typography */
h2 { font-size: 1.4rem; margin-bottom: 1.25rem; }
h3 { font-size: 1.1rem; margin-bottom: 0.75rem; }

/* Tables */
table {
    width: 100%;
    border-collapse: collapse;
    background: white;
    border-radius: 6px;
    overflow: hidden;
    box-shadow: 0 1px 3px rgba(0,0,0,0.08);
}

th, td {
    padding: 0.6rem 0.9rem;
    text-align: left;
    border-bottom: 1px solid #eee;
}

th { background: #f3f4f6; font-weight: 600; font-size: 0.8rem; text-transform: uppercase; }
tr:last-child td { border-bottom: none; }
tr:hover td { background: #fafafa; }

.text-right { text-align: right; }
.text-muted { color: #888; font-size: 0.85em; }
.total-row td { font-weight: 700; background: #f3f4f6; }

/* Buttons */
button, .btn {
    padding: 0.4rem 0.9rem;
    border: 1px solid #d1d5db;
    border-radius: 4px;
    background: white;
    cursor: pointer;
    font-size: 0.875rem;
    transition: background 0.15s;
}

button:hover { background: #f3f4f6; }
button:disabled { opacity: 0.5; cursor: not-allowed; }
.btn-primary { background: #6c63ff; color: white; border-color: #6c63ff; }
.btn-primary:hover { background: #5a52d5; }
.btn-danger { background: #ef4444; color: white; border-color: #ef4444; }
.btn-danger:hover { background: #dc2626; }
.btn-sm { padding: 0.25rem 0.6rem; font-size: 0.8rem; }

/* Forms */
select, input[type="number"], input[type="text"] {
    padding: 0.35rem 0.6rem;
    border: 1px solid #d1d5db;
    border-radius: 4px;
    font-size: 0.875rem;
}

/* Tabs */
.tabs { display: flex; gap: 0; margin-bottom: 1.25rem; border-bottom: 2px solid #e5e7eb; }
.tab {
    padding: 0.5rem 1.1rem;
    cursor: pointer;
    border: none;
    background: none;
    font-size: 0.875rem;
    color: #666;
    border-bottom: 2px solid transparent;
    margin-bottom: -2px;
}
.tab.active { color: #6c63ff; border-bottom-color: #6c63ff; font-weight: 600; }

/* Status */
.alert-error { color: #b91c1c; background: #fee2e2; padding: 0.6rem 1rem; border-radius: 4px; margin-bottom: 1rem; }
.alert-success { color: #166534; background: #dcfce7; padding: 0.6rem 1rem; border-radius: 4px; margin-bottom: 1rem; }

/* Toolbar */
.toolbar { display: flex; gap: 0.75rem; align-items: center; margin-bottom: 1.25rem; flex-wrap: wrap; }
```

---

## Task 6.4 — Transactions.razor

Create `Spening/Pages/Transactions.razor`:

```razor
@page "/transactions"
@inject HttpClient Http
@inject NavigationManager Nav

<h2>Transactions</h2>

@if (_error != null)
{
    <div class="alert-error">@_error</div>
}
@if (_message != null)
{
    <div class="alert-success">@_message</div>
}

<div class="toolbar">
    <button class="btn-primary" @onclick="Sync" disabled="@_busy">
        @(_syncing ? "Syncing..." : "Sync")
    </button>
    <button @onclick="Fetch" disabled="@_busy">
        @(_fetching ? "Fetching..." : "Fetch Full Year")
    </button>
    <span style="margin-left:auto">
        <label>Year: </label>
        <input type="number" @bind="_filterYear" style="width:80px" />
    </span>
    <select @bind="_filterCategory">
        <option value="">All categories</option>
        @foreach (var cat in _categories)
        {
            <option value="@cat">@cat</option>
        }
    </select>
    <button @onclick="ApplyFilter" disabled="@_loading">Filter</button>
    <a href="api/transactions/export?year=@_filterYear" target="_blank">
        <button type="button">Export CSV</button>
    </a>
</div>

@if (_loading)
{
    <p class="text-muted">Loading...</p>
}
else if (_transactions.Count == 0)
{
    <p class="text-muted">No transactions found. Try syncing or adjusting your filters.</p>
}
else
{
    <p class="text-muted" style="margin-bottom:0.75rem">
        Showing @_transactions.Count transaction(s)
    </p>
    <table>
        <thead>
            <tr>
                <th>Date</th>
                <th>Merchant</th>
                <th>Category</th>
                <th class="text-right">Amount</th>
            </tr>
        </thead>
        <tbody>
            @foreach (var t in _transactions)
            {
                <tr>
                    <td>@t.Date.ToString("MMM d, yyyy")</td>
                    <td>@t.Name</td>
                    <td class="text-muted">@(string.IsNullOrEmpty(t.Category) ? "—" : t.Category)</td>
                    <td class="text-right">@t.Amount.ToString("C")</td>
                </tr>
            }
        </tbody>
    </table>
}

@code {
    private List<TransactionDto> _transactions = new();
    private List<string> _categories = new();
    private int _filterYear = DateTime.Now.Year;
    private string _filterCategory = "";
    private bool _loading, _busy, _syncing, _fetching;
    private string? _error, _message;

    protected override async Task OnInitializedAsync() => await LoadTransactions();

    private async Task LoadTransactions()
    {
        _loading = true; _error = null;
        try
        {
            var url = $"api/transactions?year={_filterYear}";
            if (!string.IsNullOrEmpty(_filterCategory))
                url += $"&category={Uri.EscapeDataString(_filterCategory)}";

            _transactions = await Http.GetFromJsonAsync<List<TransactionDto>>(url) ?? new();
            _categories = _transactions
                .Select(t => t.Category)
                .Where(c => !string.IsNullOrEmpty(c))
                .Distinct()
                .OrderBy(c => c)
                .ToList();
        }
        catch (Exception ex) { _error = ex.Message; }
        finally { _loading = false; }
    }

    private async Task Sync()
    {
        _busy = _syncing = true; _message = null; _error = null;
        try
        {
            var resp = await Http.PostAsync("api/transactions/sync", null);
            resp.EnsureSuccessStatusCode();
            var result = await resp.Content.ReadFromJsonAsync<SyncResult>();
            _message = $"Sync complete — {result!.Added} added, {result.Removed} removed.";
            await LoadTransactions();
        }
        catch (Exception ex) { _error = ex.Message; }
        finally { _busy = _syncing = false; }
    }

    private async Task Fetch()
    {
        _busy = _fetching = true; _message = null; _error = null;
        try
        {
            var resp = await Http.PostAsync("api/transactions/fetch", null);
            resp.EnsureSuccessStatusCode();
            var result = await resp.Content.ReadFromJsonAsync<FetchResult>();
            _message = $"Fetch complete — {result!.Written} transactions written.";
            await LoadTransactions();
        }
        catch (Exception ex) { _error = ex.Message; }
        finally { _busy = _fetching = false; }
    }

    private async Task ApplyFilter() => await LoadTransactions();

    private record TransactionDto(string TransactionId, string AccountId, DateOnly Date,
        string Name, decimal Amount, string Category);
    private record SyncResult(int Added, int Removed);
    private record FetchResult(int Written);
}
```

---

## Task 6.5 — Reports.razor

Create `Spening/Pages/Reports.razor`:

```razor
@page "/reports"
@inject HttpClient Http

<h2>Reports</h2>

@if (_error != null)
{
    <div class="alert-error">@_error</div>
}

<div class="toolbar">
    <label>Year: </label>
    <input type="number" @bind="_year" style="width:80px" />
    <button @onclick="Refresh" disabled="@_loading">Refresh</button>
</div>

<div class="tabs">
    @foreach (var tab in _tabs)
    {
        <button class="tab @(tab == _activeTab ? "active" : "")"
                @onclick="() => SetTab(tab)">@tab</button>
    }
</div>

@if (_loading)
{
    <p class="text-muted">Loading...</p>
}
else
{
    @if (_activeTab == "Monthly")
    {
        <div class="toolbar">
            <a href="api/reports/monthly?year=@_year&format=csv" target="_blank">
                <button type="button" class="btn-sm">Export CSV</button>
            </a>
        </div>
        <table>
            <thead>
                <tr><th>Month</th><th class="text-right">Total</th><th class="text-right">Transactions</th></tr>
            </thead>
            <tbody>
                @foreach (var r in _monthly)
                {
                    <tr>
                        <td>@r.Label</td>
                        <td class="text-right">@r.Total.ToString("C")</td>
                        <td class="text-right">@r.Count</td>
                    </tr>
                }
                <tr class="total-row">
                    <td>Total</td>
                    <td class="text-right">@_monthly.Sum(r => r.Total).ToString("C")</td>
                    <td class="text-right">@_monthly.Sum(r => r.Count)</td>
                </tr>
            </tbody>
        </table>
    }

    @if (_activeTab == "By Category")
    {
        <div class="toolbar">
            <a href="api/reports/category?year=@_year&format=csv" target="_blank">
                <button type="button" class="btn-sm">Export CSV</button>
            </a>
        </div>
        <table>
            <thead>
                <tr><th>Category</th><th class="text-right">Total</th><th class="text-right">% of Spend</th><th class="text-right">Transactions</th></tr>
            </thead>
            <tbody>
                @foreach (var r in _categories)
                {
                    <tr>
                        <td>@r.Category</td>
                        <td class="text-right">@r.Total.ToString("C")</td>
                        <td class="text-right">@r.PctOfSpend%</td>
                        <td class="text-right">@r.Count</td>
                    </tr>
                }
                <tr class="total-row">
                    <td>Total</td>
                    <td class="text-right">@_categories.Sum(r => r.Total).ToString("C")</td>
                    <td class="text-right">100%</td>
                    <td class="text-right">@_categories.Sum(r => r.Count)</td>
                </tr>
            </tbody>
        </table>
    }

    @if (_activeTab == "Pivot")
    {
        @if (_pivot != null)
        {
            <table>
                <thead>
                    <tr>
                        <th>Month</th>
                        @foreach (var cat in _pivot.Categories)
                        {
                            <th class="text-right" title="@cat">
                                @(cat.Length > 12 ? cat[..11] + "…" : cat)
                            </th>
                        }
                        <th class="text-right">Total</th>
                    </tr>
                </thead>
                <tbody>
                    @foreach (var row in _pivot.Rows)
                    {
                        <tr>
                            <td>@row.Label</td>
                            @for (int i = 0; i < row.Values.Count; i++)
                            {
                                <td class="text-right">
                                    @(row.Values[i] == 0 ? "—" : row.Values[i].ToString("C"))
                                </td>
                            }
                            <td class="text-right">@row.RowTotal.ToString("C")</td>
                        </tr>
                    }
                    <tr class="total-row">
                        <td>Total</td>
                        @foreach (var ct in _pivot.ColumnTotals)
                        {
                            <td class="text-right">@ct.ToString("C")</td>
                        }
                        <td class="text-right">@_pivot.GrandTotal.ToString("C")</td>
                    </tr>
                </tbody>
            </table>
            <p class="text-muted" style="margin-top:0.5rem">Showing top 8 categories by spend.</p>
        }
    }

    @if (_activeTab == "Top Merchants")
    {
        <div class="toolbar">
            <label>Top: </label>
            <input type="number" @bind="_topN" style="width:60px" min="1" max="100" />
            <button @onclick="Refresh" class="btn-sm">Apply</button>
            <a href="api/reports/merchants?year=@_year&topN=@_topN&format=csv" target="_blank">
                <button type="button" class="btn-sm">Export CSV</button>
            </a>
        </div>
        <table>
            <thead>
                <tr><th>Merchant</th><th class="text-right">Total</th><th class="text-right">Visits</th><th class="text-right">Avg/Visit</th></tr>
            </thead>
            <tbody>
                @foreach (var r in _merchants)
                {
                    <tr>
                        <td>@r.Name</td>
                        <td class="text-right">@r.Total.ToString("C")</td>
                        <td class="text-right">@r.Count</td>
                        <td class="text-right">@r.AvgPerVisit.ToString("C")</td>
                    </tr>
                }
            </tbody>
        </table>
    }

    @if (_activeTab == "Largest")
    {
        <div class="toolbar">
            <label>Top: </label>
            <input type="number" @bind="_topN" style="width:60px" min="1" max="100" />
            <button @onclick="Refresh" class="btn-sm">Apply</button>
            <a href="api/reports/largest?year=@_year&topN=@_topN&format=csv" target="_blank">
                <button type="button" class="btn-sm">Export CSV</button>
            </a>
        </div>
        <table>
            <thead>
                <tr><th>Date</th><th>Merchant</th><th>Category</th><th class="text-right">Amount</th></tr>
            </thead>
            <tbody>
                @foreach (var t in _largest)
                {
                    <tr>
                        <td>@t.Date.ToString("MMM d, yyyy")</td>
                        <td>@t.Name</td>
                        <td class="text-muted">@(string.IsNullOrEmpty(t.Category) ? "—" : t.Category)</td>
                        <td class="text-right">@t.Amount.ToString("C")</td>
                    </tr>
                }
            </tbody>
        </table>
    }
}

@code {
    private readonly string[] _tabs = { "Monthly", "By Category", "Pivot", "Top Merchants", "Largest" };
    private string _activeTab = "Monthly";
    private int _year = DateTime.Now.Year;
    private int _topN = 10;
    private bool _loading;
    private string? _error;

    private List<MonthlyRow> _monthly = new();
    private List<CategoryRow> _categories = new();
    private PivotResult? _pivot;
    private List<MerchantRow> _merchants = new();
    private List<TransactionDto> _largest = new();

    protected override async Task OnInitializedAsync() => await Refresh();

    private async Task SetTab(string tab)
    {
        _activeTab = tab;
        await Refresh();
    }

    private async Task Refresh()
    {
        _loading = true; _error = null;
        try
        {
            if (_activeTab == "Monthly")
                _monthly = await Http.GetFromJsonAsync<List<MonthlyRow>>(
                    $"api/reports/monthly?year={_year}") ?? new();

            else if (_activeTab == "By Category")
                _categories = await Http.GetFromJsonAsync<List<CategoryRow>>(
                    $"api/reports/category?year={_year}") ?? new();

            else if (_activeTab == "Pivot")
                _pivot = await Http.GetFromJsonAsync<PivotResult>(
                    $"api/reports/pivot?year={_year}");

            else if (_activeTab == "Top Merchants")
                _merchants = await Http.GetFromJsonAsync<List<MerchantRow>>(
                    $"api/reports/merchants?year={_year}&topN={_topN}") ?? new();

            else if (_activeTab == "Largest")
                _largest = await Http.GetFromJsonAsync<List<TransactionDto>>(
                    $"api/reports/largest?year={_year}&topN={_topN}") ?? new();
        }
        catch (Exception ex) { _error = ex.Message; }
        finally { _loading = false; }
    }

    // Mirror the record types from ReportService so JSON deserialization works
    private record MonthlyRow(string Month, string Label, decimal Total, int Count);
    private record CategoryRow(string Category, decimal Total, int Count, decimal PctOfSpend);
    private record MerchantRow(string Name, decimal Total, int Count, decimal AvgPerVisit);
    private record PivotRow(string Month, string Label, List<decimal> Values, decimal RowTotal);
    private record PivotResult(List<string> Categories, List<PivotRow> Rows,
        decimal GrandTotal, List<decimal> ColumnTotals);
    private record TransactionDto(string TransactionId, string AccountId, DateOnly Date,
        string Name, decimal Amount, string Category);
}
```

---

## Task 6.6 — Settings.razor

Create `Spening/Pages/Settings.razor`:

```razor
@page "/settings"
@inject HttpClient Http

<h2>Settings</h2>

@if (_error != null) { <div class="alert-error">@_error</div> }
@if (_saved) { <div class="alert-success">Settings saved.</div> }

@if (_loading)
{
    <p class="text-muted">Loading...</p>
}
else
{
    <table style="max-width:480px">
        <tbody>
            <tr>
                <td><strong>Year</strong></td>
                <td>
                    <input type="number" @bind="_year" style="width:100px" />
                </td>
            </tr>
            <tr>
                <td><strong>Plaid Environment</strong></td>
                <td>
                    <select @bind="_environment">
                        <option value="sandbox">sandbox</option>
                        <option value="development">development</option>
                        <option value="production">production</option>
                    </select>
                </td>
            </tr>
        </tbody>
    </table>

    @if (_environment == "production")
    {
        <div class="alert-error" style="margin-top:1rem">
            ⚠ Production mode uses real financial data. Ensure
            <code>PLAID_PRODUCTION_SECRET</code> is set correctly.
        </div>
    }

    <br />
    <button class="btn-primary" @onclick="Save" disabled="@_saving">
        @(_saving ? "Saving..." : "Save Settings")
    </button>
}

@code {
    private int _year = DateTime.Now.Year;
    private string _environment = "sandbox";
    private bool _loading = true, _saving, _saved;
    private string? _error;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var settings = await Http.GetFromJsonAsync<Dictionary<string, string>>("api/settings")
                           ?? new();
            if (settings.TryGetValue("output_year", out var y) && int.TryParse(y, out var yi))
                _year = yi;
            if (settings.TryGetValue("plaid_environment", out var e))
                _environment = e;
        }
        catch (Exception ex) { _error = ex.Message; }
        finally { _loading = false; }
    }

    private async Task Save()
    {
        _saving = true; _saved = false; _error = null;
        try
        {
            var payload = new Dictionary<string, string>
            {
                ["output_year"] = _year.ToString(),
                ["plaid_environment"] = _environment
            };
            var resp = await Http.PutAsJsonAsync("api/settings", payload);
            resp.EnsureSuccessStatusCode();
            _saved = true;
        }
        catch (Exception ex) { _error = ex.Message; }
        finally { _saving = false; }
    }
}
```

---

## Task 6.7 — Review Accounts.razor

`Accounts.razor` was created in Phase 3. Confirm it compiles and the styles from `app.css` apply
correctly (buttons use `.btn-primary`, table uses base table styles). No functional changes needed
unless issues are found.

---

## Task 6.8 — End-to-End Browser Verification

1. `dotnet run` from `Spening/`
2. Navigate to `http://localhost:8080`
3. Verify the sidebar navigation links work for all four pages
4. **Accounts** — linked accounts appear; "Add Account" opens Plaid Link
5. **Transactions** — Sync button runs and shows result; table populates; Export CSV downloads file
6. **Reports** — all five tabs load data; year filter changes results; CSV export links work
7. **Settings** — current values load; save persists changes (verify in DB or via `/api/settings`)

---

## Proceed to Phase 7

Continue with [PHASE-7.md](./PHASE-7.md).
