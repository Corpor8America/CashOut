# Phase 5 — Report Service & Reports API

## Progress Tracker

- [ ] 5.1 Create `ReportService` with all five report methods
- [ ] 5.2 Create `ReportsController`
- [ ] 5.3 Register `ReportService` in `Program.cs`
- [ ] 5.4 Verify all five report endpoints

---

## Context

Reports are always computed from the `transactions` table at query time — no materialised views or
pre-aggregation. All five reports filter to `amount > 0` (expenses only, per Plaid's sign
convention). The year defaults to the `output_year` setting but can be overridden via query param.

All endpoints support `?format=csv` to return a downloadable CSV instead of JSON.

### The Five Reports

| Name | Groups by | Sorted by |
|---|---|---|
| Monthly totals | `date` year-month | month ascending |
| Category totals | `category` | total descending |
| Month × category pivot | month rows, category columns | month ascending, top 8 cats by spend |
| Top merchants | `name` | total descending, top N |
| Largest transactions | individual rows | amount descending, top N |

---

## Task 5.1 — ReportService

Create `Spening/Services/ReportService.cs`:

```csharp
using System.Globalization;
using System.Text;

public class ReportService
{
    private readonly AppDbContext _db;
    private readonly SettingsService _settings;

    public ReportService(AppDbContext db, SettingsService settings)
    {
        _db = db;
        _settings = settings;
    }

    // ── Shared ────────────────────────────────────────────────────────────

    private async Task<List<Transaction>> GetExpenses(int year)
    {
        return await _db.Transactions
            .Where(t => t.Date.Year == year && t.Amount > 0)
            .ToListAsync();
    }

    // ── Monthly Totals ────────────────────────────────────────────────────

    public async Task<List<MonthlyRow>> GetMonthly(int? year = null)
    {
        var y = year ?? await _settings.GetOutputYear();
        var expenses = await GetExpenses(y);

        return expenses
            .GroupBy(t => new { t.Date.Year, t.Date.Month })
            .Select(g => new MonthlyRow(
                Month: $"{g.Key.Year}-{g.Key.Month:D2}",
                Label: new DateOnly(g.Key.Year, g.Key.Month, 1).ToString("MMM yyyy"),
                Total: g.Sum(t => t.Amount),
                Count: g.Count()))
            .OrderBy(r => r.Month)
            .ToList();
    }

    // ── Category Totals ───────────────────────────────────────────────────

    public async Task<List<CategoryRow>> GetByCategory(int? year = null)
    {
        var y = year ?? await _settings.GetOutputYear();
        var expenses = await GetExpenses(y);
        var grandTotal = expenses.Sum(t => t.Amount);

        return expenses
            .GroupBy(t => string.IsNullOrWhiteSpace(t.Category)
                ? "(uncategorized)" : t.Category)
            .Select(g => new CategoryRow(
                Category: g.Key,
                Total: g.Sum(t => t.Amount),
                Count: g.Count(),
                PctOfSpend: grandTotal == 0 ? 0
                    : Math.Round(g.Sum(t => t.Amount) / grandTotal * 100, 1)))
            .OrderByDescending(r => r.Total)
            .ToList();
    }

    // ── Pivot ─────────────────────────────────────────────────────────────

    public async Task<PivotResult> GetPivot(int? year = null)
    {
        var y = year ?? await _settings.GetOutputYear();
        var expenses = await GetExpenses(y);

        var months = expenses
            .Select(t => $"{t.Date.Year}-{t.Date.Month:D2}")
            .Distinct()
            .OrderBy(m => m)
            .ToList();

        // Top 8 categories by total spend
        var topCats = expenses
            .GroupBy(t => string.IsNullOrWhiteSpace(t.Category)
                ? "(uncategorized)" : t.Category)
            .OrderByDescending(g => g.Sum(t => t.Amount))
            .Take(8)
            .Select(g => g.Key)
            .ToList();

        // Build lookup: (month, category) → total
        var lookup = expenses
            .Where(t => topCats.Contains(
                string.IsNullOrWhiteSpace(t.Category) ? "(uncategorized)" : t.Category))
            .GroupBy(t => (
                Month: $"{t.Date.Year}-{t.Date.Month:D2}",
                Cat: string.IsNullOrWhiteSpace(t.Category) ? "(uncategorized)" : t.Category))
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));

        var rows = months.Select(m =>
        {
            var cells = topCats.Select(c =>
                lookup.TryGetValue((m, c), out var v) ? v : 0m).ToList();
            return new PivotRow(
                Month: m,
                Label: ParseMonthLabel(m),
                Values: cells,
                RowTotal: cells.Sum());
        }).ToList();

        return new PivotResult(topCats, rows, rows.Select(r => r.RowTotal).Sum(),
            topCats.Select(c => rows.Sum(r =>
                r.Values[topCats.IndexOf(c)])).ToList());
    }

    private static string ParseMonthLabel(string ym)
    {
        if (DateTime.TryParseExact(ym + "-01", "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt.ToString("MMM yyyy");
        return ym;
    }

    // ── Top Merchants ─────────────────────────────────────────────────────

    public async Task<List<MerchantRow>> GetTopMerchants(int topN = 10, int? year = null)
    {
        var y = year ?? await _settings.GetOutputYear();
        var expenses = await GetExpenses(y);

        return expenses
            .GroupBy(t => t.Name)
            .Select(g => new MerchantRow(
                Name: g.Key,
                Total: g.Sum(t => t.Amount),
                Count: g.Count(),
                AvgPerVisit: g.Sum(t => t.Amount) / g.Count()))
            .OrderByDescending(r => r.Total)
            .Take(topN)
            .ToList();
    }

    // ── Largest Transactions ──────────────────────────────────────────────

    public async Task<List<Transaction>> GetLargest(int topN = 10, int? year = null)
    {
        var y = year ?? await _settings.GetOutputYear();
        var expenses = await GetExpenses(y);

        return expenses
            .OrderByDescending(t => t.Amount)
            .Take(topN)
            .ToList();
    }

    // ── CSV Helpers ───────────────────────────────────────────────────────

    public async Task<byte[]> MonthlyCsv(int? year = null)
    {
        var rows = await GetMonthly(year);
        var sb = new StringBuilder("Month,Label,Total,Transactions\n");
        foreach (var r in rows)
            sb.AppendLine($"{r.Month},{r.Label},{r.Total},{r.Count}");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public async Task<byte[]> CategoryCsv(int? year = null)
    {
        var rows = await GetByCategory(year);
        var sb = new StringBuilder("Category,Total,PctOfSpend,Transactions\n");
        foreach (var r in rows)
            sb.AppendLine($"{Esc(r.Category)},{r.Total},{r.PctOfSpend},{r.Count}");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public async Task<byte[]> MerchantsCsv(int topN = 10, int? year = null)
    {
        var rows = await GetTopMerchants(topN, year);
        var sb = new StringBuilder("Merchant,Total,Visits,AvgPerVisit\n");
        foreach (var r in rows)
            sb.AppendLine($"{Esc(r.Name)},{r.Total},{r.Count},{r.AvgPerVisit:F2}");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public async Task<byte[]> LargestCsv(int topN = 10, int? year = null)
    {
        var rows = await GetLargest(topN, year);
        var sb = new StringBuilder("Date,Merchant,Category,Amount\n");
        foreach (var t in rows)
            sb.AppendLine($"{t.Date},{Esc(t.Name)},{Esc(t.Category)},{t.Amount}");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string Esc(string s) =>
        s.Contains(',') || s.Contains('"') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
}

// ── Result Types ──────────────────────────────────────────────────────────────

public record MonthlyRow(string Month, string Label, decimal Total, int Count);

public record CategoryRow(
    string Category, decimal Total, int Count, decimal PctOfSpend);

public record MerchantRow(
    string Name, decimal Total, int Count, decimal AvgPerVisit);

public record PivotRow(
    string Month, string Label, List<decimal> Values, decimal RowTotal);

public record PivotResult(
    List<string> Categories,
    List<PivotRow> Rows,
    decimal GrandTotal,
    List<decimal> ColumnTotals);
```

---

## Task 5.2 — ReportsController

Create `Spening/Controllers/ReportsController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly ReportService _reports;
    private readonly SettingsService _settings;

    public ReportsController(ReportService reports, SettingsService settings)
    {
        _reports = reports;
        _settings = settings;
    }

    [HttpGet("monthly")]
    public async Task<IActionResult> Monthly(
        [FromQuery] int? year, [FromQuery] string? format)
    {
        if (format == "csv")
            return File(await _reports.MonthlyCsv(year), "text/csv", "monthly.csv");
        return Ok(await _reports.GetMonthly(year));
    }

    [HttpGet("category")]
    public async Task<IActionResult> Category(
        [FromQuery] int? year, [FromQuery] string? format)
    {
        if (format == "csv")
            return File(await _reports.CategoryCsv(year), "text/csv", "category.csv");
        return Ok(await _reports.GetByCategory(year));
    }

    [HttpGet("pivot")]
    public async Task<IActionResult> Pivot(
        [FromQuery] int? year, [FromQuery] string? format)
    {
        // Pivot CSV is complex — skip for now, return JSON only
        if (format == "csv")
            return BadRequest(new { error = "Pivot CSV export not supported" });
        return Ok(await _reports.GetPivot(year));
    }

    [HttpGet("merchants")]
    public async Task<IActionResult> Merchants(
        [FromQuery] int topN = 10, [FromQuery] int? year = null,
        [FromQuery] string? format = null)
    {
        if (format == "csv")
            return File(await _reports.MerchantsCsv(topN, year), "text/csv", "merchants.csv");
        return Ok(await _reports.GetTopMerchants(topN, year));
    }

    [HttpGet("largest")]
    public async Task<IActionResult> Largest(
        [FromQuery] int topN = 10, [FromQuery] int? year = null,
        [FromQuery] string? format = null)
    {
        if (format == "csv")
            return File(await _reports.LargestCsv(topN, year), "text/csv", "largest.csv");
        return Ok(await _reports.GetLargest(topN, year));
    }
}
```

---

## Task 5.3 — Register ReportService in Program.cs

Add to services section:

```csharp
builder.Services.AddScoped<ReportService>();
```

---

## Task 5.4 — Verification

With transactions in the DB from Phase 4:

```bash
# Monthly totals (JSON)
curl http://localhost:8080/api/reports/monthly
# Expected: array of {month, label, total, count}

# Monthly totals (CSV download)
curl "http://localhost:8080/api/reports/monthly?format=csv" -o monthly.csv

# Category totals
curl http://localhost:8080/api/reports/category

# Pivot
curl http://localhost:8080/api/reports/pivot
# Expected: {categories:[...], rows:[...], grandTotal:..., columnTotals:[...]}

# Top 5 merchants
curl "http://localhost:8080/api/reports/merchants?topN=5"

# Top 10 largest transactions
curl http://localhost:8080/api/reports/largest

# Filter by year
curl "http://localhost:8080/api/reports/monthly?year=2024"
```

All endpoints should return data (not empty arrays) when transactions exist in the DB.

---

## Proceed to Phase 6

Continue with [PHASE-6.md](./PHASE-6.md).
