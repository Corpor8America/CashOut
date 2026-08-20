# Step 04 — ReportService (Stripped)

**Goal:** Create a stripped ReportService with only two reports: Inflow vs Outflow (monthly net cash flow) and Spending by Category. Remove all merchant normalization, alias includes, executive summary, and income report logic.

**Prerequisites:** Steps 01–03 complete.

---

## 4.1 ReportService

**File:** `CashOut/Services/ReportService.cs`

```csharp
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
    /// Expense transactions for the year. Amount > 0 means net outflow.
    /// </summary>
    private async Task<List<Transaction>> GetExpenses(int year)
    {
        return await _db.Transactions
            .Where(t => t.Date.Year == year && t.Amount > 0)
            .ToListAsync();
    }

    private async Task<List<Transaction>> GetExpenses(int year, int month)
    {
        return await _db.Transactions
            .Where(t => t.Date.Year == year && t.Date.Month == month && t.Amount > 0)
            .ToListAsync();
    }

    /// <summary>
    /// Income transactions for the year. Amount < 0 means net inflow.
    /// </summary>
    private async Task<List<Transaction>> GetIncomeTransactions(int year, int? month = null)
    {
        var query = _db.Transactions
            .Where(t => t.Date.Year == year && t.Amount < 0);
        if (month.HasValue)
            query = query.Where(t => t.Date.Month == month.Value);
        return await query.ToListAsync();
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

    public async Task<CategoryReportResult> GetByCategory(int? year = null, int? month = null)
    {
        var y = year ?? await _settings.GetOutputYear();
        var previousYear = y - 1;

        var currentExpenses = month.HasValue
            ? await GetExpenses(y, month.Value)
            : await GetExpenses(y);
        var previousExpenses = await GetExpenses(previousYear);

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

        var categories = currentGroups
            .Select(g =>
            {
                var cat = g.Key;
                var txns = g.Value;
                var total = txns.Sum(t => t.Amount);
                var count = txns.Count;

                var prevTotal = previousTotals.GetValueOrDefault(cat, 0m);
                var prevCount = previousCounts.GetValueOrDefault(cat, 0);

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

    // ── Cash Flow (Inflow vs Outflow) ─────────────────────────────────────

    public async Task<CashFlowReportResult> GetCashFlow(int? year = null)
    {
        var y = year ?? await _settings.GetOutputYear();
        var previousYear = y - 1;

        var currentTxns = await _db.Transactions
            .Where(t => t.Date.Year == y && t.Amount != 0)
            .ToListAsync();

        var previousTxns = await _db.Transactions
            .Where(t => t.Date.Year == previousYear && t.Amount != 0)
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
                    t.Amount,
                    t.Debit,
                    t.Credit,
                    t.Category,
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

        // Rolling average
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

    // ── Helpers ───────────────────────────────────────────────────────────

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
        t.Amount < 0 ? Math.Abs(t.Amount) : 0m;

    private static decimal ExpenseAmount(Transaction t) =>
        t.Amount > 0 ? t.Amount : 0m;

    private static string MonthKey(int year, int month) => $"{year}-{month:D2}";

    private static string MonthLabel(int year, int month) =>
        new DateOnly(year, month, 1).ToString("MMM yyyy");

    // ── CSV Exports ───────────────────────────────────────────────────────

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

    private static string Esc(string s) =>
        s.Contains(',') || s.Contains('"') || s.Contains('\n')
            ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
}
```

## 4.2 Updated DTOs

The report DTOs are simplified — remove all alias/merchant/normalizedName fields.

**File:** `CashOut/Models/ReportDtos.cs`

```csharp
// ── Monthly ───────────────────────────────────────────────────────────────

public record MonthlyRow(string Month, string Label, decimal Total, int Count);

// ── Category Report ───────────────────────────────────────────────────────

public record CategoryReportResult(
    int Year, int PreviousYear,
    decimal GrandTotal, decimal PreviousGrandTotal,
    decimal ChangeAmount, decimal ChangePercent,
    int TransactionCount,
    List<CategoryReportRow> Categories);

public record CategoryReportRow(
    string Category,
    decimal Total,
    int Count,
    decimal PctOfSpend,
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
    decimal Amount,
    decimal? Debit,
    decimal? Credit,
    string Category,
    TransactionSource Source);

// ── Cash Flow (Inflow vs Outflow) ────────────────────────────────────────

public record CashFlowReportResult(
    int Year, int PreviousYear,
    decimal TotalIncome, decimal TotalExpenses,
    decimal NetCashFlow,
    decimal PreviousYearNet,
    decimal NetChangeAmount, decimal NetChangePercent,
    decimal AverageMonthlyNet,
    decimal BestMonthNet, string BestMonthLabel,
    decimal WorstMonthNet, string WorstMonthLabel,
    int TransactionCount,
    List<CashFlowMonthRow> Months);

public record CashFlowMonthRow(
    string Month, string Label,
    decimal Income, decimal Expenses, decimal Net,
    decimal RollingAverageNet,
    decimal PreviousYearNet,
    decimal ChangeAmount, decimal ChangePercent,
    int IncomeCount, int ExpenseCount, int TransactionCount,
    List<CashFlowTransactionRow> Transactions);

public record CashFlowTransactionRow(
    string TransactionId,
    string AccountId,
    DateOnly Date,
    string Name,
    string RawName,
    decimal Amount,
    decimal? Debit,
    decimal? Credit,
    string Category,
    TransactionSource Source,
    string Type);
```

## 4.3 Register service

In `CashOut/Program.cs`, add:

```csharp
builder.Services.AddScoped<ReportService>();
```

## 4.4 Verify build

```bash
dotnet build CashOut/CashOut.csproj
```

---

## Verification

1. ReportService has no `Include(t => t.Alias)` calls
2. No references to `NormalizedName`, `AliasId`, `RawBusinessId`
3. Only `GetByCategory`, `GetCashFlow`, `GetMonthly` remain (no `GetTopMerchants`, `GetIncome`, `GetExecutiveSummary`)
4. `CategoryTransactionRow` has no `NormalizedName` field
5. `CashFlowTransactionRow` has no `AliasId`, `RawBusinessId`, `NormalizedName` fields
6. `dotnet build` succeeds
