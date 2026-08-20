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

    private async Task<List<Transaction>> GetExpenses(int year)
    {
        return await _db.Transactions
            .Where(t => t.Date.Year == year && t.Debit != null)
            .ToListAsync();
    }

    private async Task<List<Transaction>> GetExpenses(int year, int month)
    {
        return await _db.Transactions
            .Where(t => t.Date.Year == year && t.Date.Month == month && t.Debit != null)
            .ToListAsync();
    }

    public async Task<List<MonthlyRow>> GetMonthly(int? year = null)
    {
        var y = year ?? await _settings.GetOutputYear();
        var expenses = await GetExpenses(y);

        return expenses
            .GroupBy(t => new { t.Date.Year, t.Date.Month })
            .Select(g => new MonthlyRow(
                Month: $"{g.Key.Year}-{g.Key.Month:D2}",
                Label: new DateOnly(g.Key.Year, g.Key.Month, 1).ToString("MMM yyyy"),
                Total: g.Sum(t => Math.Abs(t.Amount)),
                Count: g.Count()))
            .OrderBy(r => r.Month)
            .ToList();
    }

    public async Task<CategoryReportResult> GetByCategory(int? year = null, int? month = null)
    {
        var y = year ?? await _settings.GetOutputYear();
        var previousYear = y - 1;

        var currentExpenses = month.HasValue
            ? await GetExpenses(y, month.Value)
            : await GetExpenses(y);
        var previousExpenses = await GetExpenses(previousYear);

        var grandTotal = currentExpenses.Sum(t => Math.Abs(t.Amount));
        var previousGrandTotal = previousExpenses.Sum(t => Math.Abs(t.Amount));
        var transactionCount = currentExpenses.Count;

        var currentGroups = currentExpenses
            .GroupBy(t => CategoryKey(t))
            .ToDictionary(g => g.Key, g => g.ToList());

        var previousTotals = previousExpenses
            .GroupBy(t => CategoryKey(t))
            .ToDictionary(g => g.Key, g => g.Sum(t => Math.Abs(t.Amount)));

        var previousCounts = previousExpenses
            .GroupBy(t => CategoryKey(t))
            .ToDictionary(g => g.Key, g => g.Count());

        var categories = currentGroups
            .Select(g =>
            {
                var cat = g.Key;
                var txns = g.Value;
                var total = txns.Sum(t => Math.Abs(t.Amount));
                var count = txns.Count;

                var prevTotal = previousTotals.GetValueOrDefault(cat, 0m);
                var prevCount = previousCounts.GetValueOrDefault(cat, 0);

                var changeAmount = total - prevTotal;
                var changePercent = ChangePercent(total, prevTotal);

                var transactionRows = txns
                    .OrderByDescending(t => t.Date)
                    .ThenByDescending(t => Math.Abs(t.Amount))
                    .Select(t => new CategoryTransactionRow(
                        t.TransactionId,
                        t.AccountId,
                        t.Date,
                        t.Name,
                        t.RawName,
                        t.Amount,
                        t.Debit,
                        t.Credit,
                        t.Category,
                        t.Source))
                    .ToList();

                return new CategoryReportRow(
                    cat, total, count,
                    Percent(total, grandTotal),
                    prevTotal, prevCount,
                    changeAmount, changePercent,
                    transactionRows);
            })
            .OrderByDescending(r => r.Total)
            .ToList();

        return new CategoryReportResult(
            y, previousYear,
            grandTotal, previousGrandTotal,
            grandTotal - previousGrandTotal,
            ChangePercent(grandTotal, previousGrandTotal),
            transactionCount,
            categories);
    }

    public async Task<CategoryDetailReportResult> GetCategoryDetail(
        int? fromYear = null, int? fromMonth = null,
        int? toYear = null, int? toMonth = null,
        string? accountId = null)
    {
        var y = await _settings.GetOutputYear();
        var fy = fromYear ?? y;
        var fm = fromMonth ?? 1;
        var ty = toYear ?? y;
        var tm = toMonth ?? 12;

        var minYear = Math.Min(fy, ty);
        var maxYear = Math.Max(fy, ty);

        var currentQuery = _db.Transactions
            .Where(t => t.Date.Year >= minYear && t.Date.Year <= maxYear
                && t.Date.Month >= (t.Date.Year == fy ? fm : 1)
                && t.Date.Month <= (t.Date.Year == ty ? tm : 12)
                && (t.Credit != null || t.Debit != null));

        if (!string.IsNullOrEmpty(accountId))
        {
            currentQuery = currentQuery.Where(t => t.AccountId == accountId);
        }

        var currentTxns = await currentQuery.ToListAsync();

        var accountNames = await _db.LinkedAccounts
            .ToDictionaryAsync(a => a.AccountId, a => a.Name);
        var manualNames = await _db.ManualAccounts
            .ToDictionaryAsync(a => a.Id.ToString(), a => a.Name);

        string ResolveAccountName(string id) =>
            accountNames.TryGetValue(id, out var n) ? n
            : manualNames.TryGetValue(id, out var m) ? m
            : $"Account {id[..Math.Min(8, id.Length)]}";

        var monthsInRange = 0;
        {
            var (cY, cM) = (fy, fm);
            while (cY < ty || (cY == ty && cM <= tm))
            {
                monthsInRange++;
                cM++;
                if (cM > 12) { cM = 1; cY++; }
            }
        }
        if (monthsInRange == 0) monthsInRange = 1;

        var totalIncome = currentTxns.Sum(IncomeAmount);
        var totalExpenses = currentTxns.Sum(ExpenseAmount);
        var netCashFlow = totalIncome - totalExpenses;

        var currentGroups = currentTxns
            .GroupBy(t => CategoryKey(t))
            .ToDictionary(g => g.Key, g => g.ToList());

        var categories = currentGroups
            .Select(g =>
            {
                var cat = g.Key;
                var txns = g.Value;
                var total = txns.Sum(t => t.Amount);
                var count = txns.Count;
                var avgPerMonth = Math.Round(total / monthsInRange, 2);

                var transactionRows = txns
                    .OrderByDescending(t => t.Date)
                    .ThenByDescending(t => Math.Abs(t.Amount))
                    .Select(t => new CategoryDetailTransactionRow(
                        t.TransactionId,
                        t.AccountId,
                        ResolveAccountName(t.AccountId),
                        t.Date,
                        t.Name,
                        t.RawName,
                        t.Amount,
                        t.Debit,
                        t.Credit,
                        t.Category,
                        t.Source))
                    .ToList();

                var isIncome = total >= 0;
                return new CategoryDetailRow(
                    cat, total, avgPerMonth, count,
                    isIncome ? Percent(total, totalIncome) : 0,
                    isIncome ? 0 : Percent(Math.Abs(total), totalExpenses),
                    transactionRows);
            })
            .OrderByDescending(r => r.Total)
            .ToList();

        return new CategoryDetailReportResult(
            fy, fm, ty, tm,
            totalIncome, totalExpenses, netCashFlow,
            Math.Round(totalIncome / monthsInRange, 2),
            Math.Round(totalExpenses / monthsInRange, 2),
            Math.Round(netCashFlow / monthsInRange, 2),
            currentTxns.Count,
            categories);
    }

    public async Task<CashFlowReportResult> GetCashFlow(
        int? year = null, string? accountId = null,
        int? fromYear = null, int? fromMonth = null,
        int? toYear = null, int? toMonth = null)
    {
        var y = year ?? await _settings.GetOutputYear();
        var fy = fromYear ?? y;
        var fm = fromMonth ?? 1;
        var ty = toYear ?? y;
        var tm = toMonth ?? 12;

        var minYear = Math.Min(fy, ty);
        var maxYear = Math.Max(fy, ty);
        var prevYear = minYear - 1;

        var currentQuery = _db.Transactions
            .Where(t => t.Date.Year >= minYear && t.Date.Year <= maxYear && (t.Credit != null || t.Debit != null));

        var previousQuery = _db.Transactions
            .Where(t => t.Date.Year >= prevYear && t.Date.Year < minYear && (t.Credit != null || t.Debit != null));

        if (!string.IsNullOrEmpty(accountId))
        {
            currentQuery = currentQuery.Where(t => t.AccountId == accountId);
            previousQuery = previousQuery.Where(t => t.AccountId == accountId);
        }

        var currentTxns = await currentQuery.ToListAsync();
        var previousTxns = await previousQuery.ToListAsync();

        var accountNames = await _db.LinkedAccounts
            .ToDictionaryAsync(a => a.AccountId, a => a.Name);
        var manualNames = await _db.ManualAccounts
            .ToDictionaryAsync(a => a.Id.ToString(), a => a.Name);

        string ResolveAccountName(string id) =>
            accountNames.TryGetValue(id, out var n) ? n
            : manualNames.TryGetValue(id, out var m) ? m
            : $"Account {id[..Math.Min(8, id.Length)]}";

        var currentByKey = currentTxns
            .GroupBy(t => $"{t.Date.Year}-{t.Date.Month:D2}")
            .ToDictionary(g => g.Key, g => g.ToList());

        var previousByKey = previousTxns
            .GroupBy(t => $"{t.Date.Year}-{t.Date.Month:D2}")
            .ToDictionary(g => g.Key, g => g.ToList());

        var months = new List<CashFlowMonthRow>();
        var (curY, curM) = (fy, fm);

        while (curY < ty || (curY == ty && curM <= tm))
        {
            var key = $"{curY}-{curM:D2}";
            var prevKey = $"{curY - 1}-{curM:D2}";

            var current = currentByKey.GetValueOrDefault(key, new List<Transaction>());
            var previous = previousByKey.GetValueOrDefault(prevKey, new List<Transaction>());

            var income = current.Sum(IncomeAmount);
            var expenses = current.Sum(ExpenseAmount);
            var net = income - expenses;

            var prevIncome = previous.Sum(IncomeAmount);
            var prevExpenses = previous.Sum(ExpenseAmount);
            var prevNet = prevIncome - prevExpenses;

            var changeAmount = net - prevNet;
            var changePercent = ChangePercentFromNet(net, prevNet);

            var incomeCount = current.Count(t => t.Amount > 0);
            var expenseCount = current.Count(t => t.Amount < 0);

            var txns = current
                .OrderByDescending(t => t.Date)
                .ThenByDescending(t => t.Amount > 0 ? t.Amount : Math.Abs(t.Amount))
                .ThenBy(t => t.Name)
                .Select(t => new CashFlowTransactionRow(
                    t.TransactionId,
                    t.AccountId,
                    ResolveAccountName(t.AccountId),
                    t.Date,
                    t.Name,
                    t.RawName,
                    t.Amount,
                    t.Debit,
                    t.Credit,
                    t.Category,
                    t.Source))
                .ToList();

            months.Add(new CashFlowMonthRow(
                key,
                MonthLabel(curY, curM),
                income, expenses, net,
                0m,
                prevNet,
                changeAmount, changePercent,
                incomeCount, expenseCount, current.Count,
                txns));

            curM++;
            if (curM > 12) { curM = 1; curY++; }
        }

        for (int i = 0; i < months.Count; i++)
        {
            var start = Math.Max(0, i - 2);
            var count = i - start + 1;
            var sum = 0m;
            for (int j = start; j <= i; j++)
                sum += months[j].Net;
            months[i] = months[i] with { RollingAverageNet = Math.Round(sum / count, 2) };
        }

        var totalIncome = months.Sum(m => m.Income);
        var totalExpenses = months.Sum(m => m.Expenses);
        var filteredCount = months.Sum(m => m.TransactionCount);
        var netCashFlow = totalIncome - totalExpenses;
        var prevNetCashFlow = months.Sum(m => m.PreviousYearNet);
        var netChangeAmount = netCashFlow - prevNetCashFlow;
        var netChangePercent = ChangePercentFromNet(netCashFlow, prevNetCashFlow);
        var avgDivisor = (decimal)months.Count;
        var averageMonthlyNet = avgDivisor > 0 ? Math.Round(months.Sum(m => m.Net) / avgDivisor, 2) : 0m;

        var best = months.OrderByDescending(m => m.Net).First();
        var worst = months.OrderBy(m => m.Net).First();

        return new CashFlowReportResult(
            fy, prevYear,
            totalIncome, totalExpenses, netCashFlow,
            prevNetCashFlow,
            netChangeAmount, netChangePercent,
            averageMonthlyNet,
            best.Net, best.Label,
            worst.Net, worst.Label,
            filteredCount,
            months);
    }

    private static string CategoryKey(Transaction t) =>
        string.IsNullOrWhiteSpace(t.Category) ? "(uncategorized)" : t.Category;

    private static decimal Percent(decimal numerator, decimal denominator) =>
        denominator == 0 ? 0 : Math.Round(numerator / denominator * 100m, 1);

    private static decimal ChangePercent(decimal current, decimal previous) =>
        previous == 0 ? 0 : Math.Round((current - previous) / previous * 100m, 1);

    private static decimal ChangePercentFromNet(decimal current, decimal previous)
    {
        if (previous == 0) return 0;
        return Math.Round((current - previous) / Math.Abs(previous) * 100m, 1);
    }

    private static decimal IncomeAmount(Transaction t) =>
        t.Amount > 0 ? t.Amount : 0m;

    private static decimal ExpenseAmount(Transaction t) =>
        t.Amount < 0 ? Math.Abs(t.Amount) : 0m;

    private static string MonthKey(int year, int month) => $"{year}-{month:D2}";

    private static string MonthLabel(int year, int month) =>
        new DateOnly(year, month, 1).ToString("MMM yyyy");

    public async Task<byte[]> MonthlyCsv(int? year = null)
    {
        var rows = await GetMonthly(year);
        var sb = new StringBuilder("Month,Label,Total,Transactions\n");
        foreach (var r in rows)
            sb.AppendLine($"{r.Month},{r.Label},{r.Total},{r.Count}");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public async Task<byte[]> CategoryCsv(int? year = null, int? month = null)
    {
        var result = await GetByCategory(year, month);
        var sb = new StringBuilder("Category,Total,PctOfSpend,Transactions,PreviousTotal,PreviousTransactions,ChangeAmount,ChangePercent\n");
        foreach (var r in result.Categories)
            sb.AppendLine($"{Esc(r.Category)},{r.Total},{r.PctOfSpend},{r.Count},{r.PreviousTotal},{r.PreviousCount},{r.ChangeAmount},{r.ChangePercent}");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public async Task<byte[]> CategoryDetailCsv(
        int? fromYear = null, int? fromMonth = null,
        int? toYear = null, int? toMonth = null,
        string? accountId = null)
    {
        var result = await GetCategoryDetail(fromYear, fromMonth, toYear, toMonth, accountId);
        var sb = new StringBuilder("Category,Total,AvgPerMonth,PctOfIncome,PctOfExpenses,Transactions\n");
        foreach (var r in result.Categories)
            sb.AppendLine($"{Esc(r.Category)},{r.Total},{r.AvgPerMonth},{r.PctOfIncome},{r.PctOfExpenses},{r.Count}");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public async Task<byte[]> CashFlowCsv(
        int? year = null, string? accountId = null,
        int? fromYear = null, int? fromMonth = null,
        int? toYear = null, int? toMonth = null)
    {
        var result = await GetCashFlow(year, accountId, fromYear, fromMonth, toYear, toMonth);
        var sb = new StringBuilder("Month,Label,Income,Expenses,Net,RollingAverageNet,PreviousYearNet,ChangeAmount,ChangePercent,IncomeTransactions,ExpenseTransactions,Transactions\n");
        foreach (var r in result.Months)
        {
            sb.AppendLine(
                $"{r.Month},{r.Label}," +
                $"{r.Income.ToString(CultureInfo.InvariantCulture)}," +
                $"{r.Expenses.ToString(CultureInfo.InvariantCulture)}," +
                $"{r.Net.ToString(CultureInfo.InvariantCulture)}," +
                $"{r.RollingAverageNet.ToString(CultureInfo.InvariantCulture)}," +
                $"{r.PreviousYearNet.ToString(CultureInfo.InvariantCulture)}," +
                $"{r.ChangeAmount.ToString(CultureInfo.InvariantCulture)}," +
                $"{r.ChangePercent.ToString(CultureInfo.InvariantCulture)}," +
                $"{r.IncomeCount},{r.ExpenseCount},{r.TransactionCount}");
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string Esc(string s) =>
        s.Contains(',') || s.Contains('"') || s.Contains('\n')
            ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
}
