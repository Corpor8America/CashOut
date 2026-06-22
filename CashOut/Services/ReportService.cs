using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;

public class ReportService
{
    private readonly AppDbContext _db;
    private readonly SettingsService _settings;
    private List<string>? _excluded;

    public ReportService(AppDbContext db, SettingsService settings)
    {
        _db = db;
        _settings = settings;
    }

    // ── Shared ────────────────────────────────────────────────────────────

    private async Task<List<string>> GetExcludedCategories()
    {
        _excluded ??= await _settings.GetExcludedCategories();
        return _excluded;
    }

    /// <summary>
    /// Returns expense transactions for the year, excluding categories the user has hidden.
    /// Amount > 0 means Debit > Credit (net outflow) — consistent with the sign spec.
    /// </summary>
    private async Task<List<Transaction>> GetExpenses(int year)
    {
        var excluded = await GetExcludedCategories();
        if (excluded.Count == 0)
            return await _db.Transactions
                .Where(t => t.Date.Year == year && t.Amount > 0)
                .ToListAsync();
        return await _db.Transactions
            .Where(t => t.Date.Year == year && t.Amount > 0 && !excluded.Contains(t.Category))
            .ToListAsync();
    }

    /// <summary>
    /// Returns income transactions for the year, excluding categories the user has hidden.
    /// Amount < 0 means Credit > Debit (net inflow) in the current CashOut model.
    /// </summary>
    private async Task<List<Transaction>> GetIncomeTransactions(int year)
    {
        var excluded = await GetExcludedCategories();
        if (excluded.Count == 0)
            return await _db.Transactions
                .Include(t => t.Alias)
                .Where(t => t.Date.Year == year && t.Amount < 0)
                .ToListAsync();
        return await _db.Transactions
            .Include(t => t.Alias)
            .Where(t => t.Date.Year == year && t.Amount < 0 && !excluded.Contains(t.Category))
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

    public async Task<CategoryReportResult> GetByCategory(int? year = null)
    {
        var y = year ?? await _settings.GetOutputYear();
        var previousYear = y - 1;

        var currentExpenses = await GetExpenses(y);
        var previousExpenses = await GetExpenses(previousYear);

        // Trailing 12-month window is the full selected year
        var trailingExpenses = currentExpenses;

        var grandTotal = currentExpenses.Sum(t => t.Amount);
        var previousGrandTotal = previousExpenses.Sum(t => t.Amount);
        var transactionCount = currentExpenses.Count;

        var currentGroups = currentExpenses
            .GroupBy(t => CategoryKey(t))
            .ToDictionary(g => g.Key, g => g.ToList());

        var previousTotals = previousExpenses
            .GroupBy(t => CategoryKey(t))
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));

        var previousCounts = previousExpenses
            .GroupBy(t => CategoryKey(t))
            .ToDictionary(g => g.Key, g => g.Count());

        var trailingGroups = trailingExpenses
            .GroupBy(t => CategoryKey(t))
            .ToDictionary(g => g.Key, g => g.ToList());

        var categories = currentGroups
            .Select(g =>
            {
                var cat = g.Key;
                var txns = g.Value;
                var total = txns.Sum(t => t.Amount);
                var count = txns.Count;

                var prevTotal = previousTotals.GetValueOrDefault(cat, 0m);
                var prevCount = previousCounts.GetValueOrDefault(cat, 0);

                var trailingList = trailingGroups.GetValueOrDefault(cat, new List<Transaction>());
                var twelveMonthTotal = trailingList.Sum(t => t.Amount);
                var twelveMonthCount = trailingList.Count;
                var twelveMonthAverage = Math.Round(twelveMonthTotal / 12m, 2);

                var currentMonthlyAverage = total / 12m;
                var vsAmount = currentMonthlyAverage - twelveMonthAverage;
                var vsPercent = RollingAveragePercent(currentMonthlyAverage, twelveMonthAverage);

                var changeAmount = total - prevTotal;
                var changePercent = ChangePercent(total, prevTotal);

                var transactionRows = txns
                    .OrderByDescending(t => t.Date)
                    .ThenByDescending(t => t.Amount)
                    .Select(t => new CategoryTransactionRow(
                        t.TransactionId,
                        t.AccountId,
                        t.Date,
                        t.Name,
                        t.RawName,
                        t.NormalizedName,
                        t.Amount,
                        t.Debit,
                        t.Credit,
                        t.Category,
                        t.Source))
                    .ToList();

                return new CategoryReportRow(
                    cat, total, count,
                    Percent(total, grandTotal),
                    twelveMonthAverage, twelveMonthTotal, twelveMonthCount,
                    vsAmount, vsPercent,
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

    private static string CategoryKey(Transaction t) =>
        string.IsNullOrWhiteSpace(t.Category) ? "(uncategorized)" : t.Category;

    private static decimal Percent(decimal numerator, decimal denominator) =>
        denominator == 0 ? 0 : Math.Round(numerator / denominator * 100m, 1);

    private static decimal ChangePercent(decimal current, decimal previous) =>
        previous == 0 ? 0 : Math.Round((current - previous) / previous * 100m, 1);

    private static decimal RollingAveragePercent(decimal currentAverage, decimal rollingAverage) =>
        rollingAverage == 0 ? 0 : Math.Round((currentAverage - rollingAverage) / rollingAverage * 100m, 1);

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

    public async Task<MerchantReportResult> GetTopMerchants(int topN = 10, int? year = null)
    {
        var y = year ?? await _settings.GetOutputYear();
        var previousYear = y - 1;

        if (topN < 1) topN = 10;
        if (topN > 100) topN = 100;

        var excluded = await GetExcludedCategories();
        var currentExpenses = excluded.Count == 0
            ? await _db.Transactions
                .Where(t => t.Date.Year == y && t.Amount > 0)
                .Include(t => t.Alias)
                .ToListAsync()
            : await _db.Transactions
                .Where(t => t.Date.Year == y && t.Amount > 0 && !excluded.Contains(t.Category))
                .Include(t => t.Alias)
                .ToListAsync();

        var previousExpenses = excluded.Count == 0
            ? await _db.Transactions
                .Where(t => t.Date.Year == previousYear && t.Amount > 0)
                .Include(t => t.Alias)
                .ToListAsync()
            : await _db.Transactions
                .Where(t => t.Date.Year == previousYear && t.Amount > 0 && !excluded.Contains(t.Category))
                .Include(t => t.Alias)
                .ToListAsync();

        var grandTotal = currentExpenses.Sum(t => t.Amount);
        var transactionCount = currentExpenses.Count;

        var currentGroups = currentExpenses
            .GroupBy(t => MerchantKey(t))
            .ToDictionary(g => g.Key, g => g.ToList());

        var merchantCount = currentGroups.Count;

        var previousTotals = previousExpenses
            .GroupBy(t => MerchantKey(t))
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));

        var previousCounts = previousExpenses
            .GroupBy(t => MerchantKey(t))
            .ToDictionary(g => g.Key, g => g.Count());

        var merchants = currentGroups
            .Select(g =>
            {
                var txns = g.Value;
                var total = txns.Sum(t => t.Amount);
                var count = txns.Count;
                var avgPerVisit = count > 0 ? total / count : 0m;

                var firstWithAlias = txns.FirstOrDefault(t => t.AliasId.HasValue);
                int? aliasId = firstWithAlias?.AliasId;
                int? rawBusinessId = txns.FirstOrDefault(t => t.RawBusinessId.HasValue)?.RawBusinessId;
                var isMapped = aliasId.HasValue;

                string name;
                if (isMapped)
                {
                    var alias = firstWithAlias!.Alias;
                    name = alias?.AliasName ?? firstWithAlias.Name;
                }
                else
                {
                    name = MerchantDisplayName(txns);
                }

                var normalizedName = isMapped
                    ? (txns.First().NormalizedName ?? "")
                    : (txns.Select(t => t.NormalizedName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "");

                var primaryCat = PrimaryCategory(txns);
                var prevTotal = previousTotals.GetValueOrDefault(g.Key, 0m);
                var prevCount = previousCounts.GetValueOrDefault(g.Key, 0);
                var changeAmount = total - prevTotal;
                var changePercent = ChangePercent(total, prevTotal);

                var transactionRows = txns
                    .OrderByDescending(t => t.Date)
                    .ThenByDescending(t => t.Amount)
                    .Select(t => new MerchantTransactionRow(
                        t.TransactionId,
                        t.AccountId,
                        t.Date,
                        t.Name,
                        t.RawName,
                        t.NormalizedName,
                        t.Amount,
                        t.Debit,
                        t.Credit,
                        t.Category,
                        t.AliasId,
                        t.RawBusinessId,
                        t.Source))
                    .ToList();

                return new MerchantReportRow(
                    g.Key, aliasId, rawBusinessId,
                    name, normalizedName, isMapped,
                    primaryCat,
                    total, count, avgPerVisit,
                    Percent(total, grandTotal),
                    prevTotal, prevCount,
                    changeAmount, changePercent,
                    transactionRows);
            })
            .OrderByDescending(r => r.Total)
            .Take(topN)
            .ToList();

        return new MerchantReportResult(
            y, previousYear, topN,
            grandTotal, transactionCount, merchantCount,
            merchants);
    }

    private static string MerchantKey(Transaction t)
    {
        if (t.AliasId.HasValue) return $"alias:{t.AliasId.Value}";
        if (!string.IsNullOrWhiteSpace(t.NormalizedName)) return $"raw:{t.NormalizedName}";
        return $"name:{t.Name}";
    }

    private static string MerchantDisplayName(IEnumerable<Transaction> transactions)
    {
        return transactions
            .GroupBy(t => string.IsNullOrWhiteSpace(t.Name) ? "(unknown merchant)" : t.Name)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .First()
            .Key;
    }

    private static string SourceDisplayName(IEnumerable<Transaction> transactions)
    {
        return transactions
            .GroupBy(t => string.IsNullOrWhiteSpace(t.Name) ? "(unknown source)" : t.Name)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .First()
            .Key;
    }

    private static string PrimaryCategory(IEnumerable<Transaction> transactions)
    {
        return transactions
            .GroupBy(t => string.IsNullOrWhiteSpace(t.Category) ? "Unassigned" : t.Category)
            .OrderByDescending(g => g.Sum(t => t.Amount))
            .ThenBy(g => g.Key)
            .First()
            .Key;
    }

    // ── Income ────────────────────────────────────────────────────────────

    public async Task<IncomeReportResult> GetIncome(int? year = null)
    {
        var y = year ?? await _settings.GetOutputYear();
        var previousYear = y - 1;

        var currentIncome = await GetIncomeTransactions(y);
        var previousIncome = await GetIncomeTransactions(previousYear);

        var totalIncome = currentIncome.Sum(t => Math.Abs(t.Amount));
        var previousTotalIncome = previousIncome.Sum(t => Math.Abs(t.Amount));
        var transactionCount = currentIncome.Count;

        var currentGroups = currentIncome
            .GroupBy(t => MerchantKey(t))
            .ToDictionary(g => g.Key, g => g.ToList());

        var previousTotals = previousIncome
            .GroupBy(t => MerchantKey(t))
            .ToDictionary(g => g.Key, g => g.Sum(t => Math.Abs(t.Amount)));

        var previousCounts = previousIncome
            .GroupBy(t => MerchantKey(t))
            .ToDictionary(g => g.Key, g => g.Count());

        var sources = currentGroups
            .Select(g =>
            {
                var txns = g.Value;
                var total = txns.Sum(t => Math.Abs(t.Amount));
                var count = txns.Count;
                var avgAmount = count > 0 ? Math.Round(total / count, 2) : 0m;

                var firstWithAlias = txns.FirstOrDefault(t => t.AliasId.HasValue);
                int? aliasId = firstWithAlias?.AliasId;
                int? rawBusinessId = txns.FirstOrDefault(t => t.RawBusinessId.HasValue)?.RawBusinessId;
                var isMapped = aliasId.HasValue;

                string name;
                if (isMapped)
                {
                    var alias = firstWithAlias!.Alias;
                    name = alias?.AliasName ?? firstWithAlias.Name;
                }
                else
                {
                    name = SourceDisplayName(txns);
                }

                var normalizedName = isMapped
                    ? (txns.First().NormalizedName ?? "")
                    : (txns.Select(t => t.NormalizedName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "");

                var primaryCat = txns
                    .GroupBy(t => string.IsNullOrWhiteSpace(t.Category) ? "Unassigned" : t.Category)
                    .OrderByDescending(cg => cg.Sum(t => Math.Abs(t.Amount)))
                    .ThenBy(cg => cg.Key)
                    .First()
                    .Key;

                var prevTotal = previousTotals.GetValueOrDefault(g.Key, 0m);
                var prevCount = previousCounts.GetValueOrDefault(g.Key, 0);
                var changeAmount = total - prevTotal;
                var changePercent = ChangePercent(total, prevTotal);
                var pctOfIncome = Percent(total, totalIncome);

                var transactionRows = txns
                    .OrderByDescending(t => t.Date)
                    .ThenByDescending(t => Math.Abs(t.Amount))
                    .Select(t => new IncomeTransactionRow(
                        t.TransactionId,
                        t.AccountId,
                        t.Date,
                        t.Name,
                        t.RawName,
                        t.NormalizedName,
                        t.Amount,
                        Math.Abs(t.Amount),
                        t.Debit,
                        t.Credit,
                        t.Category,
                        t.AliasId,
                        t.RawBusinessId,
                        t.Source))
                    .ToList();

                return new IncomeReportRow(
                    g.Key, aliasId, rawBusinessId,
                    name, normalizedName, isMapped,
                    primaryCat,
                    total, count, avgAmount,
                    pctOfIncome,
                    prevTotal, prevCount,
                    changeAmount, changePercent,
                    transactionRows);
            })
            .OrderByDescending(r => r.Total)
            .ToList();

        return new IncomeReportResult(
            y, previousYear,
            totalIncome, previousTotalIncome,
            totalIncome - previousTotalIncome,
            ChangePercent(totalIncome, previousTotalIncome),
            transactionCount,
            sources.Count,
            sources);
    }

    public async Task<byte[]> IncomeCsv(int? year = null)
    {
        var result = await GetIncome(year);
        var sb = new StringBuilder("Source,IsMapped,AliasId,RawBusinessId,NormalizedName,PrimaryCategory,Total,PctOfIncome,Transactions,AvgAmount,PreviousTotal,PreviousTransactions,ChangeAmount,ChangePercent\n");
        foreach (var r in result.Sources)
        {
            sb.AppendLine(
                $"{Esc(r.Name)},{r.IsMapped},{r.AliasId?.ToString() ?? ""},{r.RawBusinessId?.ToString() ?? ""}," +
                $"{Esc(r.NormalizedName)},{Esc(r.PrimaryCategory)}," +
                $"{r.Total.ToString(CultureInfo.InvariantCulture)}," +
                $"{r.PctOfIncome.ToString(CultureInfo.InvariantCulture)}," +
                $"{r.Count}," +
                $"{r.AvgAmount.ToString(CultureInfo.InvariantCulture)}," +
                $"{r.PreviousTotal.ToString(CultureInfo.InvariantCulture)}," +
                $"{r.PreviousCount}," +
                $"{r.ChangeAmount.ToString(CultureInfo.InvariantCulture)}," +
                $"{r.ChangePercent.ToString(CultureInfo.InvariantCulture)}");
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    // ── Cash Flow ─────────────────────────────────────────────────────────

    public async Task<CashFlowReportResult> GetCashFlow(int? year = null)
    {
        var y = year ?? await _settings.GetOutputYear();
        var previousYear = y - 1;

        var excluded = await GetExcludedCategories();
        var currentTxns = excluded.Count == 0
            ? await _db.Transactions
                .Where(t => t.Date.Year == y && t.Amount != 0)
                .ToListAsync()
            : await _db.Transactions
                .Where(t => t.Date.Year == y && t.Amount != 0 && !excluded.Contains(t.Category))
                .ToListAsync();

        var previousTxns = excluded.Count == 0
            ? await _db.Transactions
                .Where(t => t.Date.Year == previousYear && t.Amount != 0)
                .ToListAsync()
            : await _db.Transactions
                .Where(t => t.Date.Year == previousYear && t.Amount != 0 && !excluded.Contains(t.Category))
                .ToListAsync();

        var currentByMonth = currentTxns
            .GroupBy(t => t.Date.Month)
            .ToDictionary(g => g.Key, g => g.ToList());

        var previousByMonth = previousTxns
            .GroupBy(t => t.Date.Month)
            .ToDictionary(g => g.Key, g => g.ToList());

        var totalIncome = 0m;
        var totalExpenses = 0m;
        var months = new List<CashFlowMonthRow>();

        for (int m = 1; m <= 12; m++)
        {
            var current = currentByMonth.GetValueOrDefault(m, new List<Transaction>());
            var previous = previousByMonth.GetValueOrDefault(m, new List<Transaction>());

            var income = current.Sum(IncomeAmount);
            var expenses = current.Sum(ExpenseAmount);
            var net = income - expenses;

            var prevIncome = previous.Sum(IncomeAmount);
            var prevExpenses = previous.Sum(ExpenseAmount);
            var prevNet = prevIncome - prevExpenses;

            var changeAmount = net - prevNet;
            var changePercent = ChangePercentFromNet(net, prevNet);

            var incomeCount = current.Count(t => t.Amount < 0);
            var expenseCount = current.Count(t => t.Amount > 0);

            var txns = current
                .OrderByDescending(t => t.Date)
                .ThenByDescending(t => t.Amount < 0 ? Math.Abs(t.Amount) : t.Amount)
                .ThenBy(t => t.Name)
                .Select(t => new CashFlowTransactionRow(
                    t.TransactionId,
                    t.AccountId,
                    t.Date,
                    t.Name,
                    t.RawName,
                    t.NormalizedName,
                    t.Amount,
                    t.Amount < 0 ? Math.Abs(t.Amount) : t.Amount,
                    t.Debit,
                    t.Credit,
                    t.Category,
                    t.AliasId,
                    t.RawBusinessId,
                    t.Source,
                    t.Amount < 0 ? "Income" : "Expense"))
                .ToList();

            totalIncome += income;
            totalExpenses += expenses;

            months.Add(new CashFlowMonthRow(
                MonthKey(y, m),
                MonthLabel(y, m),
                income, expenses, net,
                0m,
                prevNet,
                changeAmount, changePercent,
                incomeCount, expenseCount, current.Count,
                txns));
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

        var netCashFlow = totalIncome - totalExpenses;
        var prevNetCashFlow = previousTxns.Sum(IncomeAmount) - previousTxns.Sum(ExpenseAmount);
        var netChangeAmount = netCashFlow - prevNetCashFlow;
        var netChangePercent = ChangePercentFromNet(netCashFlow, prevNetCashFlow);
        var averageMonthlyNet = Math.Round(months.Sum(m => m.Net) / 12m, 2);

        var best = months.OrderByDescending(m => m.Net).First();
        var worst = months.OrderBy(m => m.Net).First();

        return new CashFlowReportResult(
            y, previousYear,
            totalIncome, totalExpenses, netCashFlow,
            prevNetCashFlow,
            netChangeAmount, netChangePercent,
            averageMonthlyNet,
            best.Net, best.Label,
            worst.Net, worst.Label,
            currentTxns.Count,
            months);
    }

    public async Task<byte[]> CashFlowCsv(int? year = null)
    {
        var result = await GetCashFlow(year);
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

    private static decimal IncomeAmount(Transaction t) =>
        t.Amount < 0 ? Math.Abs(t.Amount) : 0m;

    private static decimal ExpenseAmount(Transaction t) =>
        t.Amount > 0 ? t.Amount : 0m;

    private static decimal TotalIncome(IEnumerable<Transaction> transactions) =>
        transactions.Sum(IncomeAmount);

    private static decimal TotalExpenses(IEnumerable<Transaction> transactions) =>
        transactions.Sum(ExpenseAmount);

    private static decimal ChangePercentFromNet(decimal current, decimal previous)
    {
        if (previous == 0) return 0;
        return Math.Round((current - previous) / Math.Abs(previous) * 100m, 1);
    }

    private static string MonthKey(int year, int month) => $"{year}-{month:D2}";

    private static string MonthLabel(int year, int month) =>
        new DateOnly(year, month, 1).ToString("MMM yyyy");

    // ── Executive Summary ─────────────────────────────────────────────────

    public async Task<ExecutiveSummaryResult> GetExecutiveSummary(int? year = null)
    {
        var y = year ?? await _settings.GetOutputYear();

        var excluded = await GetExcludedCategories();
        var transactions = excluded.Count == 0
            ? await _db.Transactions
                .Where(t => t.Date.Year == y && t.Amount != 0)
                .Include(t => t.Alias)
                .ToListAsync()
            : await _db.Transactions
                .Where(t => t.Date.Year == y && t.Amount != 0 && !excluded.Contains(t.Category))
                .Include(t => t.Alias)
                .ToListAsync();

        var latestWithData = transactions
            .OrderByDescending(t => t.Date.Month)
            .Select(t => t.Date.Month)
            .FirstOrDefault();
        var dashMonth = latestWithData > 0 ? latestWithData : 12;

        var prevYear = y;
        var prevMonth = dashMonth - 1;
        if (prevMonth < 1)
        {
            prevMonth = 12;
            prevYear = y - 1;
        }

        var currentMonthTxns = transactions
            .Where(t => t.Date.Month == dashMonth)
            .ToList();

        var prevMonthTxns = excluded.Count == 0
            ? await _db.Transactions
                .Where(t => t.Date.Year == prevYear && t.Date.Month == prevMonth && t.Amount != 0)
                .ToListAsync()
            : await _db.Transactions
                .Where(t => t.Date.Year == prevYear && t.Date.Month == prevMonth && t.Amount != 0 && !excluded.Contains(t.Category))
                .ToListAsync();

        // ── Monthly Overview ───────────────────────────────────────────

        var currentIncome = TotalIncome(currentMonthTxns);
        var currentExpenses = TotalExpenses(currentMonthTxns);
        var currentNet = currentIncome - currentExpenses;
        var prevIncome = TotalIncome(prevMonthTxns);
        var prevExpenses = TotalExpenses(prevMonthTxns);
        var prevNet = prevIncome - prevExpenses;
        var incomeCount = currentMonthTxns.Count(t => t.Amount < 0);
        var expenseCount = currentMonthTxns.Count(t => t.Amount > 0);

        var overview = new ExecutiveMonthlyOverview(
            currentExpenses, prevExpenses,
            currentExpenses - prevExpenses,
            ChangePercent(currentExpenses, prevExpenses),
            currentIncome, prevIncome,
            currentIncome - prevIncome,
            ChangePercent(currentIncome, prevIncome),
            currentNet, prevNet,
            currentNet - prevNet,
            ChangePercentFromNet(currentNet, prevNet),
            incomeCount, expenseCount,
            currentMonthTxns.Count);

        // ── Top Categories (current month, expenses, top 5) ────────────

        var currentExpenseTxns = currentMonthTxns.Where(t => t.Amount > 0).ToList();
        var currentMonthTotalExpenses = currentExpenseTxns.Sum(t => t.Amount);

        var prevExpenseTxns = prevMonthTxns.Where(t => t.Amount > 0).ToList();
        var prevExpenseTotalsByCat = prevExpenseTxns
            .GroupBy(t => SummaryCategoryKey(t))
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));

        var topCategories = currentExpenseTxns
            .GroupBy(t => SummaryCategoryKey(t))
            .Select(g =>
            {
                var total = g.Sum(t => t.Amount);
                var prevTotal = prevExpenseTotalsByCat.GetValueOrDefault(g.Key, 0m);
                return new ExecutiveTopCategoryRow(
                    g.Key, total,
                    Percent(total, currentMonthTotalExpenses),
                    g.Count(),
                    prevTotal,
                    total - prevTotal,
                    ChangePercent(total, prevTotal));
            })
            .OrderByDescending(r => r.Total)
            .Take(5)
            .ToList();

        // ── Top Merchants (current month, expenses, top 5) ─────────────

        var prevMerchantTotals = prevExpenseTxns
            .GroupBy(t => MerchantKey(t))
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));

        var topMerchants = currentExpenseTxns
            .GroupBy(t => MerchantKey(t))
            .Select(g =>
            {
                var txns = g.ToList();
                var total = txns.Sum(t => t.Amount);
                var firstWithAlias = txns.FirstOrDefault(t => t.AliasId.HasValue);
                int? aliasId = firstWithAlias?.AliasId;
                var isMapped = aliasId.HasValue;

                string name;
                if (isMapped)
                {
                    var alias = firstWithAlias!.Alias;
                    name = alias?.AliasName ?? firstWithAlias.Name;
                }
                else
                {
                    name = MerchantDisplayName(txns);
                }

                var normalizedName = isMapped
                    ? (txns.First().NormalizedName ?? "")
                    : (txns.Select(t => t.NormalizedName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "");

                var primaryCat = txns
                    .GroupBy(t => string.IsNullOrWhiteSpace(t.Category) ? "Unassigned" : t.Category)
                    .OrderByDescending(cg => cg.Sum(t => t.Amount))
                    .ThenBy(cg => cg.Key)
                    .First()
                    .Key;

                var prevTotal = prevMerchantTotals.GetValueOrDefault(g.Key, 0m);
                return new ExecutiveTopMerchantRow(
                    g.Key, aliasId, name, normalizedName, isMapped, primaryCat,
                    total, Percent(total, currentMonthTotalExpenses), txns.Count,
                    prevTotal, total - prevTotal, ChangePercent(total, prevTotal));
            })
            .OrderByDescending(r => r.Total)
            .Take(5)
            .ToList();

        // ── Recurring Charges (selected year, expenses) ────────────────

        var yearExpenseTxns = transactions.Where(t => t.Amount > 0).ToList();

        var recurringCandidates = yearExpenseTxns
            .GroupBy(t => MerchantKey(t))
            .Where(g =>
            {
                var list = g.ToList();
                var distinctMonths = list.Select(t => t.Date.Month).Distinct().Count();
                var avg = list.Sum(ExpenseAmount) / list.Count;
                return list.Count >= 3 && distinctMonths >= 3 && avg > 0;
            })
            .Select(g =>
            {
                var list = g.OrderBy(t => t.Date).ToList();
                var latest = list.Last();
                var avg = list.Sum(ExpenseAmount) / (decimal)list.Count;
                var distinctMonths = list.Select(t => t.Date.Month).Distinct().Count();

                var firstWithAlias = list.FirstOrDefault(t => t.AliasId.HasValue);
                string name;
                if (firstWithAlias?.AliasId != null)
                {
                    name = firstWithAlias.Alias?.AliasName ?? firstWithAlias.Name;
                }
                else
                {
                    name = MerchantDisplayName(list);
                }

                var category = list
                    .GroupBy(t => string.IsNullOrWhiteSpace(t.Category) ? "Unassigned" : t.Category)
                    .OrderByDescending(cg => cg.Sum(t => t.Amount))
                    .ThenBy(cg => cg.Key)
                    .First()
                    .Key;

                var cadence = distinctMonths >= list.Count - 1 ? "Monthly" : "Recurring";
                var amountChange = latest.Amount - avg;
                var isChanged = Math.Abs(amountChange) >= Math.Max(5m, avg * 0.1m);

                return new ExecutiveRecurringChargeRow(
                    g.Key, name, category,
                    latest.Amount, Math.Round(avg, 2), Math.Round(amountChange, 2),
                    list.Count, latest.Date, cadence, isChanged);
            })
            .OrderByDescending(r => r.IsAmountChanged)
            .ThenByDescending(r => r.LatestAmount)
            .ThenBy(r => r.Name)
            .Take(5)
            .ToList();

        // ── Alerts ─────────────────────────────────────────────────────

        var unmatchedCount = transactions.Count(t => t.AliasId == null && t.RawBusinessId != null);
        var uncategorizedCount = transactions.Count(t =>
            string.IsNullOrWhiteSpace(t.Category) ||
            t.Category == "Unassigned" ||
            t.Category == "(uncategorized)");

        var possibleDupes = transactions
            .GroupBy(t => new { t.Date, t.Amount, Name = t.NormalizedName ?? t.Name, t.AccountId })
            .Where(g => g.Count() > 1)
            .Sum(g => g.Count() - 1);

        var alertItems = new List<ExecutiveAlertRow>();
        if (unmatchedCount > 0)
            alertItems.Add(new ExecutiveAlertRow(
                "Warning", "UnmatchedMerchants", "Unmatched merchants",
                $"{unmatchedCount} transactions are not mapped to a merchant alias.", unmatchedCount));
        if (uncategorizedCount > 0)
            alertItems.Add(new ExecutiveAlertRow(
                "Warning", "UncategorizedTransactions", "Uncategorized transactions",
                $"{uncategorizedCount} transactions need a category.", uncategorizedCount));
        if (possibleDupes > 0)
            alertItems.Add(new ExecutiveAlertRow(
                "Info", "PossibleDuplicates", "Possible duplicates",
                $"{possibleDupes} possible duplicate transactions found.", possibleDupes));

        // ── Account Summary ────────────────────────────────────────────

        var linkedAccounts = await _db.LinkedAccounts.ToListAsync();
        var manualAccounts = await _db.ManualAccounts.ToListAsync();

        var accountSummary = transactions
            .GroupBy(t => t.AccountId)
            .Select(g =>
            {
                var income = g.Where(t => t.Amount < 0).Sum(t => Math.Abs(t.Amount));
                var expenses = g.Where(t => t.Amount > 0).Sum(t => t.Amount);
                var linked = linkedAccounts.FirstOrDefault(a => a.AccountId == g.Key);
                var manual = manualAccounts.FirstOrDefault(a => a.Id.ToString() == g.Key);
                var name = linked?.Name ?? manual?.Name ?? g.Key;
                var type = linked?.Subtype ?? "";
                return new ExecutiveAccountSummaryRow(
                    g.Key, name, type,
                    income, expenses, income - expenses,
                    g.Count());
            })
            .OrderByDescending(r => r.Income + r.Expenses)
            .ToList();

        return new ExecutiveSummaryResult(
            y, dashMonth,
            MonthKey(y, dashMonth),
            MonthLabel(y, dashMonth),
            overview, topCategories, topMerchants,
            recurringCandidates,
            new ExecutiveAlertSummary(unmatchedCount, uncategorizedCount, possibleDupes, 0, alertItems),
            accountSummary);
    }

    public async Task<byte[]> ExecutiveSummaryCsv(int? year = null)
    {
        var result = await GetExecutiveSummary(year);
        var sb = new StringBuilder();

        sb.AppendLine("Section,Metric,Value");
        sb.AppendLine($"Overview,Month,{result.MonthLabel}");
        sb.AppendLine($"Overview,TotalSpending,{result.MonthlyOverview.TotalSpending.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"Overview,TotalIncome,{result.MonthlyOverview.TotalIncome.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"Overview,NetCashFlow,{result.MonthlyOverview.NetCashFlow.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine();

        sb.AppendLine("Top Categories");
        sb.AppendLine("Category,Total,PctOfSpend,Transactions,PreviousMonthTotal,ChangeAmount,ChangePercent");
        foreach (var r in result.TopCategories)
            sb.AppendLine($"{Esc(r.Category)},{r.Total.ToString(CultureInfo.InvariantCulture)},{r.PctOfSpend.ToString(CultureInfo.InvariantCulture)},{r.Count},{r.PreviousMonthTotal.ToString(CultureInfo.InvariantCulture)},{r.ChangeAmount.ToString(CultureInfo.InvariantCulture)},{r.ChangePercent.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine();

        sb.AppendLine("Top Merchants");
        sb.AppendLine("Merchant,IsMapped,Category,Total,PctOfSpend,Transactions,PreviousMonthTotal,ChangeAmount,ChangePercent");
        foreach (var r in result.TopMerchants)
            sb.AppendLine($"{Esc(r.Name)},{r.IsMapped},{Esc(r.PrimaryCategory)},{r.Total.ToString(CultureInfo.InvariantCulture)},{r.PctOfSpend.ToString(CultureInfo.InvariantCulture)},{r.Count},{r.PreviousMonthTotal.ToString(CultureInfo.InvariantCulture)},{r.ChangeAmount.ToString(CultureInfo.InvariantCulture)},{r.ChangePercent.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine();

        sb.AppendLine("Recurring Charges");
        sb.AppendLine("Merchant,Category,LatestAmount,AverageAmount,AmountChange,OccurrenceCount,LatestDate,Cadence,IsAmountChanged");
        foreach (var r in result.RecurringCharges)
            sb.AppendLine($"{Esc(r.Name)},{Esc(r.Category)},{r.LatestAmount.ToString(CultureInfo.InvariantCulture)},{r.AverageAmount.ToString(CultureInfo.InvariantCulture)},{r.AmountChange.ToString(CultureInfo.InvariantCulture)},{r.OccurrenceCount},{r.LatestDate},{r.Cadence},{r.IsAmountChanged}");
        sb.AppendLine();

        sb.AppendLine("Alerts");
        sb.AppendLine("Severity,Type,Title,Detail,Count");
        foreach (var r in result.Alerts.Items)
            sb.AppendLine($"{r.Severity},{r.Type},{Esc(r.Title)},{Esc(r.Detail)},{r.Count}");
        sb.AppendLine();

        sb.AppendLine("Accounts");
        sb.AppendLine("AccountId,AccountName,AccountType,Income,Expenses,NetCashFlow,Transactions");
        foreach (var r in result.Accounts)
            sb.AppendLine($"{Esc(r.AccountId)},{Esc(r.AccountName)},{Esc(r.AccountType)},{r.Income.ToString(CultureInfo.InvariantCulture)},{r.Expenses.ToString(CultureInfo.InvariantCulture)},{r.NetCashFlow.ToString(CultureInfo.InvariantCulture)},{r.TransactionCount}");

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string SummaryCategoryKey(Transaction t) =>
        string.IsNullOrWhiteSpace(t.Category) ? "Unassigned" : t.Category;

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

    public async Task<List<CategorySummaryRow>> GetCategorySummary(int year, int month)
    {
        var targetDate = new DateOnly(year, month, 1);
        var startDate = targetDate.AddMonths(-11);
        var endDate = targetDate.AddMonths(1).AddDays(-1);

        var excluded = await GetExcludedCategories();

        // Perform aggregation in the database for the 12-month period
        var stats = await _db.Transactions
            .Where(t => t.Date >= startDate && t.Date <= endDate && !excluded.Contains(t.Category))
            .GroupBy(t => string.IsNullOrEmpty(t.Category) ? "(uncategorized)" : t.Category)
            .Select(g => new
            {
                Category = g.Key,
                TwelveMonthDebit = g.Sum(t => t.Debit ?? 0),
                TwelveMonthCredit = g.Sum(t => t.Credit ?? 0),
                MonthDebit = g.Where(t => t.Date.Year == year && t.Date.Month == month).Sum(t => t.Debit ?? 0),
                MonthCredit = g.Where(t => t.Date.Year == year && t.Date.Month == month).Sum(t => t.Credit ?? 0)
            })
            .ToListAsync();

        return stats
            .Select(s => new CategorySummaryRow(
                s.Category,
                s.MonthDebit,
                s.MonthCredit,
                s.MonthCredit - s.MonthDebit,
                (s.TwelveMonthCredit - s.TwelveMonthDebit) / 12m,
                s.TwelveMonthDebit / 12m,
                s.TwelveMonthCredit / 12m))
            .OrderByDescending(r => Math.Abs(r.MonthNet))
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
        var result = await GetByCategory(year);
        var sb = new StringBuilder("Category,Total,PctOfSpend,Transactions,TwelveMonthAverage,TwelveMonthTotal,TwelveMonthTransactions,VsTwelveMonthAverageAmount,VsTwelveMonthAveragePercent,PreviousTotal,PreviousTransactions,ChangeAmount,ChangePercent\n");
        foreach (var r in result.Categories)
            sb.AppendLine($"{Esc(r.Category)},{r.Total},{r.PctOfSpend},{r.Count},{r.TwelveMonthAverage},{r.TwelveMonthTotal},{r.TwelveMonthCount},{r.VsTwelveMonthAverageAmount},{r.VsTwelveMonthAveragePercent},{r.PreviousTotal},{r.PreviousCount},{r.ChangeAmount},{r.ChangePercent}");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public async Task<byte[]> MerchantsCsv(int topN = 10, int? year = null)
    {
        var result = await GetTopMerchants(topN, year);
        var sb = new StringBuilder("Merchant,IsMapped,AliasId,RawBusinessId,NormalizedName,PrimaryCategory,Total,PctOfSpend,Transactions,AvgPerVisit,PreviousTotal,PreviousTransactions,ChangeAmount,ChangePercent\n");
        foreach (var r in result.Merchants)
            sb.AppendLine($"{Esc(r.Name)},{r.IsMapped},{r.AliasId},{r.RawBusinessId},{Esc(r.NormalizedName)},{Esc(r.PrimaryCategory)},{r.Total},{r.PctOfSpend},{r.Count},{r.AvgPerVisit:F2},{r.PreviousTotal},{r.PreviousCount},{r.ChangeAmount},{r.ChangePercent}");
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
        s.Contains(',') || s.Contains('"') || s.Contains('\n')
            ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
}

// ── Result Types ──────────────────────────────────────────────────────────────

public record MonthlyRow(string Month, string Label, decimal Total, int Count);

public record CategoryReportResult(
    int Year,
    int PreviousYear,
    decimal TotalSpend,
    decimal PreviousTotalSpend,
    decimal TotalChangeAmount,
    decimal TotalChangePercent,
    int TransactionCount,
    List<CategoryReportRow> Categories);

public record CategoryReportRow(
    string Category,
    decimal Total,
    int Count,
    decimal PctOfSpend,
    decimal TwelveMonthAverage,
    decimal TwelveMonthTotal,
    int TwelveMonthCount,
    decimal VsTwelveMonthAverageAmount,
    decimal VsTwelveMonthAveragePercent,
    decimal PreviousTotal,
    int PreviousCount,
    decimal ChangeAmount,
    decimal ChangePercent,
    List<CategoryTransactionRow> Transactions);

public record CategoryTransactionRow(
    string TransactionId,
    string AccountId,
    DateOnly Date,
    string Name,
    string RawName,
    string NormalizedName,
    decimal Amount,
    decimal? Debit,
    decimal? Credit,
    string Category,
    TransactionSource Source);

public record MerchantReportResult(
    int Year,
    int PreviousYear,
    int TopN,
    decimal TotalSpend,
    int TransactionCount,
    int MerchantCount,
    List<MerchantReportRow> Merchants);

public record MerchantReportRow(
    string MerchantKey,
    int? AliasId,
    int? RawBusinessId,
    string Name,
    string NormalizedName,
    bool IsMapped,
    string PrimaryCategory,
    decimal Total,
    int Count,
    decimal AvgPerVisit,
    decimal PctOfSpend,
    decimal PreviousTotal,
    int PreviousCount,
    decimal ChangeAmount,
    decimal ChangePercent,
    List<MerchantTransactionRow> Transactions);

public record MerchantTransactionRow(
    string TransactionId,
    string AccountId,
    DateOnly Date,
    string Name,
    string RawName,
    string NormalizedName,
    decimal Amount,
    decimal? Debit,
    decimal? Credit,
    string Category,
    int? AliasId,
    int? RawBusinessId,
    TransactionSource Source);

public record PivotRow(
    string Month, string Label, List<decimal> Values, decimal RowTotal);

public record PivotResult(
    List<string> Categories,
    List<PivotRow> Rows,
    decimal GrandTotal,
    List<decimal> ColumnTotals);

public record CategorySummaryRow(
    string Category, 
    decimal MonthDebit, 
    decimal MonthCredit, 
    decimal MonthNet, 
    decimal AvgNet,
    decimal AvgDebit,
    decimal AvgCredit);

public record IncomeReportResult(
    int Year,
    int PreviousYear,
    decimal TotalIncome,
    decimal PreviousTotalIncome,
    decimal TotalChangeAmount,
    decimal TotalChangePercent,
    int TransactionCount,
    int SourceCount,
    List<IncomeReportRow> Sources);

public record IncomeReportRow(
    string SourceKey,
    int? AliasId,
    int? RawBusinessId,
    string Name,
    string NormalizedName,
    bool IsMapped,
    string PrimaryCategory,
    decimal Total,
    int Count,
    decimal AvgAmount,
    decimal PctOfIncome,
    decimal PreviousTotal,
    int PreviousCount,
    decimal ChangeAmount,
    decimal ChangePercent,
    List<IncomeTransactionRow> Transactions);

public record IncomeTransactionRow(
    string TransactionId,
    string AccountId,
    DateOnly Date,
    string Name,
    string RawName,
    string NormalizedName,
    decimal Amount,
    decimal DisplayAmount,
    decimal? Debit,
    decimal? Credit,
    string Category,
    int? AliasId,
    int? RawBusinessId,
    TransactionSource Source);

public record CashFlowReportResult(
    int Year,
    int PreviousYear,
    decimal TotalIncome,
    decimal TotalExpenses,
    decimal NetCashFlow,
    decimal PreviousNetCashFlow,
    decimal NetChangeAmount,
    decimal NetChangePercent,
    decimal AverageMonthlyNet,
    decimal BestMonthNet,
    string? BestMonth,
    decimal WorstMonthNet,
    string? WorstMonth,
    int TransactionCount,
    List<CashFlowMonthRow> Months);

public record CashFlowMonthRow(
    string Month,
    string Label,
    decimal Income,
    decimal Expenses,
    decimal Net,
    decimal RollingAverageNet,
    decimal PreviousYearNet,
    decimal ChangeAmount,
    decimal ChangePercent,
    int IncomeCount,
    int ExpenseCount,
    int TransactionCount,
    List<CashFlowTransactionRow> Transactions);

public record CashFlowTransactionRow(
    string TransactionId,
    string AccountId,
    DateOnly Date,
    string Name,
    string RawName,
    string NormalizedName,
    decimal Amount,
    decimal DisplayAmount,
    decimal? Debit,
    decimal? Credit,
    string Category,
    int? AliasId,
    int? RawBusinessId,
    TransactionSource Source,
    string Direction);

public record ExecutiveSummaryResult(
    int Year,
    int Month,
    string MonthKey,
    string MonthLabel,
    ExecutiveMonthlyOverview MonthlyOverview,
    List<ExecutiveTopCategoryRow> TopCategories,
    List<ExecutiveTopMerchantRow> TopMerchants,
    List<ExecutiveRecurringChargeRow> RecurringCharges,
    ExecutiveAlertSummary Alerts,
    List<ExecutiveAccountSummaryRow> Accounts);

public record ExecutiveMonthlyOverview(
    decimal TotalSpending,
    decimal PreviousMonthSpending,
    decimal SpendingChangeAmount,
    decimal SpendingChangePercent,
    decimal TotalIncome,
    decimal PreviousMonthIncome,
    decimal IncomeChangeAmount,
    decimal IncomeChangePercent,
    decimal NetCashFlow,
    decimal PreviousMonthNetCashFlow,
    decimal NetCashFlowChangeAmount,
    decimal NetCashFlowChangePercent,
    int IncomeTransactionCount,
    int ExpenseTransactionCount,
    int TransactionCount);

public record ExecutiveTopCategoryRow(
    string Category,
    decimal Total,
    decimal PctOfSpend,
    int Count,
    decimal PreviousMonthTotal,
    decimal ChangeAmount,
    decimal ChangePercent);

public record ExecutiveTopMerchantRow(
    string MerchantKey,
    int? AliasId,
    string Name,
    string NormalizedName,
    bool IsMapped,
    string PrimaryCategory,
    decimal Total,
    decimal PctOfSpend,
    int Count,
    decimal PreviousMonthTotal,
    decimal ChangeAmount,
    decimal ChangePercent);

public record ExecutiveRecurringChargeRow(
    string MerchantKey,
    string Name,
    string Category,
    decimal LatestAmount,
    decimal AverageAmount,
    decimal AmountChange,
    int OccurrenceCount,
    DateOnly LatestDate,
    string Cadence,
    bool IsAmountChanged);

public record ExecutiveAlertSummary(
    int UnmatchedMerchantCount,
    int UncategorizedTransactionCount,
    int PossibleDuplicateCount,
    int RuleConflictCount,
    List<ExecutiveAlertRow> Items);

public record ExecutiveAlertRow(
    string Severity,
    string Type,
    string Title,
    string Detail,
    int Count);

public record ExecutiveAccountSummaryRow(
    string AccountId,
    string AccountName,
    string AccountType,
    decimal Income,
    decimal Expenses,
    decimal NetCashFlow,
    int TransactionCount);