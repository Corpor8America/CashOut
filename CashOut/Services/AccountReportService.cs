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
