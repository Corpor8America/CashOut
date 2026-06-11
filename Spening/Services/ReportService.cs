using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;

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

    /// <summary>
    /// Returns expense transactions for the year.
    /// Amount > 0 means Debit > Credit (net outflow) — consistent with the sign spec.
    /// </summary>
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

        var topCats = expenses
            .GroupBy(t => string.IsNullOrWhiteSpace(t.Category)
                ? "(uncategorized)" : t.Category)
            .OrderByDescending(g => g.Sum(t => t.Amount))
            .Take(8)
            .Select(g => g.Key)
            .ToList();

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
            topCats.Select(c => rows.Sum(r => r.Values[topCats.IndexOf(c)])).ToList());
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
        var sb = new StringBuilder("Date,Merchant,Category,Debit,Credit,Amount\n");
        foreach (var t in rows)
            sb.AppendLine(
                $"{t.Date},{Esc(t.Name)},{Esc(t.Category)}," +
                $"{t.Debit?.ToString(CultureInfo.InvariantCulture) ?? ""}," +
                $"{t.Credit?.ToString(CultureInfo.InvariantCulture) ?? ""}," +
                $"{t.Amount.ToString(CultureInfo.InvariantCulture)}");
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