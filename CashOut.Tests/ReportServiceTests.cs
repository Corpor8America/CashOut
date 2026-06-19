using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CashOut.Tests;

[TestClass]
public class ReportServiceTests
{
    // ── Helpers ───────────────────────────────────────────────────────────

    private static AppDbContext BuildDb(string dbName)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(opts);
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?>? initialData = null)
    {
        IEnumerable<KeyValuePair<string, string?>> data =
        initialData ?? new Dictionary<string, string?>() { { "PLAID_ENV", "production" } };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(data)
            .Build();
    }

    private static SettingsService BuildSettings(AppDbContext db) => new(db, BuildConfig());

    private static Transaction MakeTxn(
        string id, int year, int month, int day,
        decimal amount, string name = "Merchant", string category = "FOOD_AND_DRINK") =>
        new()
        {
            TransactionId = id,
            AccountId = "acct-1",
            Date = new DateOnly(year, month, day),
            Name = name,
            Amount = amount,
            Category = category,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

    private static async Task<(AppDbContext db, ReportService svc)> BuildAsync(
        string dbName, IEnumerable<Transaction> transactions)
    {
        var db = BuildDb(dbName);
        db.Transactions.AddRange(transactions);
        await db.SaveChangesAsync();

        var svc = new ReportService(db, BuildSettings(db));
        return (db, svc);
    }

    // ── Monthly ───────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetMonthly_GroupsByMonth_AndSumsCorrectly()
    {
        var txns = new[]
        {
            MakeTxn("t1", 2025, 1, 5,  100m),
            MakeTxn("t2", 2025, 1, 20, 50m),
            MakeTxn("t3", 2025, 3, 1,  200m),
        };
        var (_, svc) = await BuildAsync(nameof(GetMonthly_GroupsByMonth_AndSumsCorrectly), txns);

        var result = await svc.GetMonthly(2025);

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(150m, result.First(r => r.Month == "2025-01").Total);
        Assert.AreEqual(200m, result.First(r => r.Month == "2025-03").Total);
    }

    [TestMethod]
    public async Task GetMonthly_ExcludesNegativeAmounts()
    {
        var txns = new[]
        {
            MakeTxn("t1", 2025, 1, 1, 100m),
            MakeTxn("t2", 2025, 1, 2, -40m),   // refund
        };
        var (_, svc) = await BuildAsync(nameof(GetMonthly_ExcludesNegativeAmounts), txns);

        var result = await svc.GetMonthly(2025);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(100m, result[0].Total);
        Assert.AreEqual(1, result[0].Count);
    }

    [TestMethod]
    public async Task GetMonthly_ReturnsEmpty_WhenNoTransactions()
    {
        var (_, svc) = await BuildAsync(nameof(GetMonthly_ReturnsEmpty_WhenNoTransactions), []);
        var result = await svc.GetMonthly(2025);
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public async Task GetMonthly_OrderedChronologically()
    {
        var txns = new[]
        {
            MakeTxn("t1", 2025, 6, 1, 10m),
            MakeTxn("t2", 2025, 2, 1, 20m),
            MakeTxn("t3", 2025, 9, 1, 30m),
        };
        var (_, svc) = await BuildAsync(nameof(GetMonthly_OrderedChronologically), txns);

        var result = await svc.GetMonthly(2025);

        Assert.AreEqual("2025-02", result[0].Month);
        Assert.AreEqual("2025-06", result[1].Month);
        Assert.AreEqual("2025-09", result[2].Month);
    }

    // ── Category ──────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetByCategory_GroupsAndSums()
    {
        var txns = new[]
        {
            MakeTxn("t1", 2025, 1, 1, 50m,  category: "FOOD_AND_DRINK"),
            MakeTxn("t2", 2025, 1, 2, 75m,  category: "FOOD_AND_DRINK"),
            MakeTxn("t3", 2025, 1, 3, 200m, category: "TRAVEL"),
        };
        var (_, svc) = await BuildAsync(nameof(GetByCategory_GroupsAndSums), txns);

        var result = await svc.GetByCategory(2025);

        Assert.AreEqual(2, result.Categories.Count);
        Assert.AreEqual("TRAVEL", result.Categories[0].Category);
        Assert.AreEqual(200m, result.Categories[0].Total);
        Assert.AreEqual("FOOD_AND_DRINK", result.Categories[1].Category);
        Assert.AreEqual(125m, result.Categories[1].Total);
    }

    [TestMethod]
    public async Task GetByCategory_PctOfSpend_SumsToHundred()
    {
        var txns = new[]
        {
            MakeTxn("t1", 2025, 1, 1, 300m, category: "FOOD_AND_DRINK"),
            MakeTxn("t2", 2025, 1, 2, 100m, category: "TRAVEL"),
            MakeTxn("t3", 2025, 1, 3, 100m, category: "SHOPPING"),
        };
        var (_, svc) = await BuildAsync(nameof(GetByCategory_PctOfSpend_SumsToHundred), txns);

        var result = await svc.GetByCategory(2025);
        var totalPct = result.Categories.Sum(r => r.PctOfSpend);

        Assert.AreEqual(100m, totalPct);
    }

    [TestMethod]
    public async Task GetByCategory_EmptyCategory_MarkedUncategorized()
    {
        var txns = new[]
        {
            MakeTxn("t1", 2025, 1, 1, 50m, category: ""),
        };
        var (_, svc) = await BuildAsync(nameof(GetByCategory_EmptyCategory_MarkedUncategorized), txns);

        var result = await svc.GetByCategory(2025);

        Assert.AreEqual(1, result.Categories.Count);
        Assert.AreEqual("(uncategorized)", result.Categories[0].Category);
    }

    [TestMethod]
    public async Task GetByCategory_IncludesPreviousYearComparison()
    {
        var txns = new[]
        {
            MakeTxn("t1", 2025, 1, 1, 100m, category: "FOOD"),
            MakeTxn("t2", 2025, 1, 2, 100m, category: "FOOD"),
            MakeTxn("t3", 2024, 6, 1, 100m, category: "FOOD"),
        };
        var (_, svc) = await BuildAsync(nameof(GetByCategory_IncludesPreviousYearComparison), txns);

        var result = await svc.GetByCategory(2025);

        var food = result.Categories.Single(c => c.Category == "FOOD");
        Assert.AreEqual(100m, food.PreviousTotal);
        Assert.AreEqual(100m, food.ChangeAmount);
        Assert.AreEqual(100m, food.ChangePercent);
    }

    [TestMethod]
    public async Task GetByCategory_PreviousZero_ReturnsZeroChangePercent()
    {
        var txns = new[]
        {
            MakeTxn("t1", 2025, 1, 1, 50m, category: "FOOD"),
        };
        var (_, svc) = await BuildAsync(nameof(GetByCategory_PreviousZero_ReturnsZeroChangePercent), txns);

        var result = await svc.GetByCategory(2025);

        var food = result.Categories.Single(c => c.Category == "FOOD");
        Assert.AreEqual(0m, food.PreviousTotal);
        Assert.AreEqual(50m, food.ChangeAmount);
        Assert.AreEqual(0m, food.ChangePercent);
    }

    [TestMethod]
    public async Task GetByCategory_IncludesCurrentYearTransactionsForEachCategory()
    {
        var txns = new[]
        {
            MakeTxn("t1", 2025, 1, 1, 100m, category: "FOOD"),
            MakeTxn("t2", 2025, 1, 2, 50m,  category: "FOOD"),
            MakeTxn("t3", 2025, 1, 3, 200m, category: "TRAVEL"),
        };
        var (_, svc) = await BuildAsync(
            nameof(GetByCategory_IncludesCurrentYearTransactionsForEachCategory), txns);

        var result = await svc.GetByCategory(2025);

        var food = result.Categories.Single(c => c.Category == "FOOD");
        Assert.AreEqual(2, food.Transactions.Count);
        Assert.IsTrue(food.Transactions.All(t => t.Category == "FOOD"));
    }

    [TestMethod]
    public async Task GetByCategory_DoesNotIncludePreviousOnlyCategories()
    {
        var txns = new[]
        {
            MakeTxn("t1", 2024, 6, 1, 100m, category: "TRAVEL"),
            MakeTxn("t2", 2025, 1, 1, 50m,  category: "FOOD"),
        };
        var (_, svc) = await BuildAsync(
            nameof(GetByCategory_DoesNotIncludePreviousOnlyCategories), txns);

        var result = await svc.GetByCategory(2025);

        Assert.AreEqual(1, result.Categories.Count);
        Assert.AreEqual("FOOD", result.Categories[0].Category);
    }

    [TestMethod]
    public async Task GetByCategory_TotalsIncludeOnlyExpenses()
    {
        var txns = new[]
        {
            MakeTxn("t1", 2025, 1, 1, 100m,  category: "FOOD"),
            MakeTxn("t2", 2025, 1, 2, -25m,  category: "REFUND"),
        };
        var (_, svc) = await BuildAsync(
            nameof(GetByCategory_TotalsIncludeOnlyExpenses), txns);

        var result = await svc.GetByCategory(2025);

        Assert.AreEqual(1, result.Categories.Count);
        Assert.AreEqual(100m, result.TotalSpend);
    }

    [TestMethod]
    public async Task GetByCategory_IncludesTwelveMonthRollingAverage()
    {
        var txns = Enumerable.Range(1, 12).Select(i =>
            MakeTxn($"t{i}", 2025, i, 1, 100m, category: "FOOD")).ToArray();
        var (_, svc) = await BuildAsync(
            nameof(GetByCategory_IncludesTwelveMonthRollingAverage), txns);

        var result = await svc.GetByCategory(2025);

        var food = result.Categories.Single(c => c.Category == "FOOD");
        Assert.AreEqual(1200m, food.TwelveMonthTotal);
        Assert.AreEqual(100m, food.TwelveMonthAverage);
        Assert.AreEqual(12, food.TwelveMonthCount);
    }

    [TestMethod]
    public async Task GetByCategory_ComputesVarianceFromTwelveMonthAverage()
    {
        var txns = Enumerable.Range(1, 12).Select(i =>
            MakeTxn($"t{i}", 2025, i, 1, 200m, category: "FOOD")).ToArray();
        var (_, svc) = await BuildAsync(
            nameof(GetByCategory_ComputesVarianceFromTwelveMonthAverage), txns);

        var result = await svc.GetByCategory(2025);

        var food = result.Categories.Single(c => c.Category == "FOOD");
        // Current monthly average = 2400/12 = 200
        // TwelveMonthAverage = 2400/12 = 200
        // vsAmount = 200 - 200 = 0, vsPercent = 0
        Assert.AreEqual(0m, food.VsTwelveMonthAverageAmount);
        Assert.AreEqual(0m, food.VsTwelveMonthAveragePercent);
    }

    // ── Top Merchants ─────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetTopMerchants_OrderedByTotalDesc_AndRespectsTopN()
    {
        var txns = new[]
        {
            MakeTxn("t1", 2025, 1, 1, 10m,  name: "Coffee Shop"),
            MakeTxn("t2", 2025, 1, 2, 500m, name: "Airline"),
            MakeTxn("t3", 2025, 1, 3, 80m,  name: "Grocery"),
            MakeTxn("t4", 2025, 1, 4, 25m,  name: "Pharmacy"),
        };
        var (_, svc) = await BuildAsync(nameof(GetTopMerchants_OrderedByTotalDesc_AndRespectsTopN), txns);

        var result = await svc.GetTopMerchants(topN: 2, year: 2025);

        Assert.AreEqual(2, result.Merchants.Count);
        Assert.AreEqual("Airline", result.Merchants[0].Name);
        Assert.AreEqual("Grocery", result.Merchants[1].Name);
    }

    [TestMethod]
    public async Task GetTopMerchants_AvgPerVisit_IsCorrect()
    {
        var txns = new[]
        {
            MakeTxn("t1", 2025, 1, 1, 30m, name: "Coffee"),
            MakeTxn("t2", 2025, 1, 2, 60m, name: "Coffee"),
        };
        var (_, svc) = await BuildAsync(nameof(GetTopMerchants_AvgPerVisit_IsCorrect), txns);

        var result = await svc.GetTopMerchants(year: 2025);

        Assert.AreEqual(1, result.Merchants.Count);
        Assert.AreEqual(45m, result.Merchants[0].AvgPerVisit);
        Assert.AreEqual(2, result.Merchants[0].Count);
    }

    [TestMethod]
    public async Task GetTopMerchants_GroupsAliasedTransactionsByAlias()
    {
        var dbName = nameof(GetTopMerchants_GroupsAliasedTransactionsByAlias);
        var db = BuildDb(dbName);
        var alias = new BusinessAlias { AliasName = "Amazon" };
        db.BusinessAliases.Add(alias);
        await db.SaveChangesAsync();

        var txns = new[]
        {
            MakeTxn("t1", 2025, 1, 1, 100m, name: "Amazon Purchase"),
            MakeTxn("t2", 2025, 1, 2, 50m,  name: "Amazon Renewal"),
        };
        foreach (var t in txns) t.AliasId = alias.Id;
        db.Transactions.AddRange(txns);
        await db.SaveChangesAsync();

        var svc = new ReportService(db, BuildSettings(db));

        var result = await svc.GetTopMerchants(year: 2025);

        Assert.AreEqual(1, result.Merchants.Count);
        Assert.AreEqual("Amazon", result.Merchants[0].Name);
        Assert.IsTrue(result.Merchants[0].IsMapped);
        Assert.AreEqual(150m, result.Merchants[0].Total);
        Assert.AreEqual(2, result.Merchants[0].Count);
    }

    [TestMethod]
    public async Task GetTopMerchants_GroupsUnmappedTransactionsByNormalizedName()
    {
        var txns = new[]
        {
            MakeTxn("t1", 2025, 1, 1, 100m, name: "Whole Foods"),
            MakeTxn("t2", 2025, 1, 2, 50m,  name: "WF Market"),
        };
        foreach (var t in txns) t.NormalizedName = "whole foods";
        var (_, svc) = await BuildAsync(
            nameof(GetTopMerchants_GroupsUnmappedTransactionsByNormalizedName), txns);

        var result = await svc.GetTopMerchants(year: 2025);

        Assert.AreEqual(1, result.Merchants.Count);
        Assert.IsFalse(result.Merchants[0].IsMapped);
        Assert.AreEqual(2, result.Merchants[0].Count);
    }

    [TestMethod]
    public async Task GetTopMerchants_IncludesPrimaryCategory()
    {
        var txns = new[]
        {
            MakeTxn("t1", 2025, 1, 1, 300m, name: "Costco", category: "GROCERIES"),
            MakeTxn("t2", 2025, 1, 2, 50m,  name: "Costco", category: "SHOPPING"),
        };
        var (_, svc) = await BuildAsync(
            nameof(GetTopMerchants_IncludesPrimaryCategory), txns);

        var result = await svc.GetTopMerchants(year: 2025);

        Assert.AreEqual("GROCERIES", result.Merchants[0].PrimaryCategory);
    }

    [TestMethod]
    public async Task GetTopMerchants_IncludesPreviousYearComparison()
    {
        var txns = new[]
        {
            MakeTxn("t1", 2025, 1, 1, 100m, name: "Grocery"),
            MakeTxn("t2", 2025, 1, 2, 100m, name: "Grocery"),
            MakeTxn("t3", 2024, 6, 1, 100m, name: "Grocery"),
        };
        var (_, svc) = await BuildAsync(
            nameof(GetTopMerchants_IncludesPreviousYearComparison), txns);

        var result = await svc.GetTopMerchants(year: 2025);

        var g = result.Merchants.Single(m => m.MerchantKey == "name:Grocery");
        Assert.AreEqual(100m, g.PreviousTotal);
        Assert.AreEqual(100m, g.ChangeAmount);
        Assert.AreEqual(100m, g.ChangePercent);
    }

    [TestMethod]
    public async Task GetTopMerchants_PreviousZero_ReturnsZeroChangePercent()
    {
        var txns = new[]
        {
            MakeTxn("t1", 2025, 1, 1, 50m, name: "Grocery"),
        };
        var (_, svc) = await BuildAsync(
            nameof(GetTopMerchants_PreviousZero_ReturnsZeroChangePercent), txns);

        var result = await svc.GetTopMerchants(year: 2025);

        var g = result.Merchants.Single(m => m.MerchantKey == "name:Grocery");
        Assert.AreEqual(0m, g.PreviousTotal);
        Assert.AreEqual(50m, g.ChangeAmount);
        Assert.AreEqual(0m, g.ChangePercent);
    }

    [TestMethod]
    public async Task GetTopMerchants_IncludesCurrentYearTransactionsForEachMerchant()
    {
        var txns = new[]
        {
            MakeTxn("t1", 2025, 1, 1, 100m, name: "Costco"),
            MakeTxn("t2", 2025, 1, 2, 50m,  name: "Costco"),
            MakeTxn("t3", 2025, 1, 3, 200m, name: "Walmart"),
        };
        var (_, svc) = await BuildAsync(
            nameof(GetTopMerchants_IncludesCurrentYearTransactionsForEachMerchant), txns);

        var result = await svc.GetTopMerchants(year: 2025);

        var costco = result.Merchants.Single(m => m.MerchantKey == "name:Costco");
        Assert.AreEqual(2, costco.Transactions.Count);
    }

    [TestMethod]
    public async Task GetTopMerchants_TotalsIncludeOnlyExpenses()
    {
        var txns = new[]
        {
            MakeTxn("t1", 2025, 1, 1, 100m, name: "Store"),
            MakeTxn("t2", 2025, 1, 2, -25m, name: "Refund"),
        };
        var (_, svc) = await BuildAsync(
            nameof(GetTopMerchants_TotalsIncludeOnlyExpenses), txns);

        var result = await svc.GetTopMerchants(year: 2025);

        Assert.AreEqual(1, result.Merchants.Count);
        Assert.AreEqual(100m, result.TotalSpend);
    }

    [TestMethod]
    public async Task GetTopMerchants_ClampsTopN()
    {
        var txns = Enumerable.Range(0, 50).Select(i =>
            MakeTxn($"t{i}", 2025, (i % 12) + 1, (i % 28) + 1, 10m, name: $"Merchant_{i:D2}")).ToArray();
        var (_, svc) = await BuildAsync(nameof(GetTopMerchants_ClampsTopN), txns);

        var below = await svc.GetTopMerchants(topN: 0, year: 2025);
        Assert.AreEqual(10, below.TopN);

        var above = await svc.GetTopMerchants(topN: 500, year: 2025);
        Assert.AreEqual(100, above.TopN);
    }

    // ── Largest ───────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetLargest_ReturnsTopNByAmountDesc()
    {
        var txns = new[]
        {
            MakeTxn("t1", 2025, 1, 1, 10m),
            MakeTxn("t2", 2025, 1, 2, 999m),
            MakeTxn("t3", 2025, 1, 3, 50m),
            MakeTxn("t4", 2025, 1, 4, 200m),
        };
        var (_, svc) = await BuildAsync(nameof(GetLargest_ReturnsTopNByAmountDesc), txns);

        var result = await svc.GetLargest(topN: 2, year: 2025);

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(999m, result[0].Amount);
        Assert.AreEqual(200m, result[1].Amount);
    }

    // ── Pivot ─────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetPivot_CategoryColumns_DoNotExceedEight()
    {
        var txns = Enumerable.Range(1, 10).Select(i =>
            MakeTxn($"t{i}", 2025, 1, i, i * 10m, category: $"CAT_{i:D2}")).ToArray();
        var (_, svc) = await BuildAsync(nameof(GetPivot_CategoryColumns_DoNotExceedEight), txns);

        var result = await svc.GetPivot(2025);

        Assert.AreEqual(8, result.Categories.Count);
        Assert.IsTrue(result.Rows.All(row => row.Values.Count == 8));
    }

    // ── Income ────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetIncome_GroupsByIncomeSource()
    {
        var txns = new[]
        {
            MakeTxn("t1", 2025, 1, 1, -100m, name: "Employer Payroll"),
            MakeTxn("t2", 2025, 1, 15, -200m, name: "Employer Payroll"),
            MakeTxn("t3", 2025, 2, 1, -50m, name: "Freelance Gig"),
        };
        foreach (var t in txns) t.NormalizedName = t.Name;
        var (_, svc) = await BuildAsync(nameof(GetIncome_GroupsByIncomeSource), txns);

        var result = await svc.GetIncome(2025);

        Assert.AreEqual(2, result.Sources.Count);
        Assert.AreEqual(300m, result.Sources[0].Total);
        Assert.AreEqual(2, result.Sources[0].Count);
        Assert.AreEqual(50m, result.Sources[1].Total);
        Assert.AreEqual(1, result.Sources[1].Count);
    }

    [TestMethod]
    public async Task GetIncome_UsesPositiveDisplayTotals()
    {
        var txns = new[]
        {
            MakeTxn("t1", 2025, 1, 1, -100m, name: "Employer"),
        };
        var (_, svc) = await BuildAsync(nameof(GetIncome_UsesPositiveDisplayTotals), txns);

        var result = await svc.GetIncome(2025);

        Assert.AreEqual(100m, result.TotalIncome);
        Assert.AreEqual(100m, result.Sources[0].Total);
        Assert.AreEqual(-100m, result.Sources[0].Transactions[0].Amount);
        Assert.AreEqual(100m, result.Sources[0].Transactions[0].DisplayAmount);
    }

    [TestMethod]
    public async Task GetIncome_ExcludesExpenses()
    {
        var txns = new[]
        {
            MakeTxn("t1", 2025, 1, 1, -100m, name: "Employer"),
            MakeTxn("t2", 2025, 1, 2, 50m, name: "Store"),
        };
        var (_, svc) = await BuildAsync(nameof(GetIncome_ExcludesExpenses), txns);

        var result = await svc.GetIncome(2025);

        Assert.AreEqual(100m, result.TotalIncome);
        Assert.AreEqual(1, result.TransactionCount);
    }

    [TestMethod]
    public async Task GetIncome_IncludesPreviousYearComparison()
    {
        var txns = new[]
        {
            MakeTxn("t1", 2025, 1, 1, -150m, name: "Employer"),
            MakeTxn("t2", 2025, 1, 15, -50m, name: "Employer"),
            MakeTxn("t3", 2024, 6, 1, -100m, name: "Employer"),
        };
        foreach (var t in txns) t.NormalizedName = "employer";
        var (_, svc) = await BuildAsync(nameof(GetIncome_IncludesPreviousYearComparison), txns);

        var result = await svc.GetIncome(2025);

        var source = result.Sources.Single(s => s.SourceKey == "raw:employer");
        Assert.AreEqual(100m, source.PreviousTotal);
        Assert.AreEqual(100m, source.ChangeAmount);
        Assert.AreEqual(100m, source.ChangePercent);
    }

    [TestMethod]
    public async Task GetIncome_PreviousZero_ReturnsZeroChangePercent()
    {
        var txns = new[]
        {
            MakeTxn("t1", 2025, 1, 1, -50m, name: "Employer"),
        };
        foreach (var t in txns) t.NormalizedName = "employer";
        var (_, svc) = await BuildAsync(nameof(GetIncome_PreviousZero_ReturnsZeroChangePercent), txns);

        var result = await svc.GetIncome(2025);

        var source = result.Sources.Single(s => s.SourceKey == "raw:employer");
        Assert.AreEqual(0m, source.PreviousTotal);
        Assert.AreEqual(50m, source.ChangeAmount);
        Assert.AreEqual(0m, source.ChangePercent);
    }

    [TestMethod]
    public async Task GetIncome_IncludesPrimaryCategory()
    {
        var txns = new[]
        {
            MakeTxn("t1", 2025, 1, 1, -300m, name: "Employer", category: "PAYROLL"),
            MakeTxn("t2", 2025, 1, 2, -50m, name: "Employer", category: "REFUND"),
        };
        foreach (var t in txns) t.NormalizedName = "employer";
        var (_, svc) = await BuildAsync(nameof(GetIncome_IncludesPrimaryCategory), txns);

        var result = await svc.GetIncome(2025);

        var source = result.Sources.Single(s => s.SourceKey == "raw:employer");
        Assert.AreEqual("PAYROLL", source.PrimaryCategory);
    }

    [TestMethod]
    public async Task GetIncome_GroupsAliasedTransactionsByAlias()
    {
        var dbName = nameof(GetIncome_GroupsAliasedTransactionsByAlias);
        var db = BuildDb(dbName);
        var alias = new BusinessAlias { AliasName = "Employer Corp" };
        db.BusinessAliases.Add(alias);
        await db.SaveChangesAsync();

        var txns = new[]
        {
            MakeTxn("t1", 2025, 1, 1, -200m, name: "Employer Payroll"),
            MakeTxn("t2", 2025, 1, 15, -300m, name: "Employer Bonus"),
        };
        foreach (var t in txns)
        {
            t.AliasId = alias.Id;
            t.NormalizedName = "employer corp";
        }
        db.Transactions.AddRange(txns);
        await db.SaveChangesAsync();

        var svc = new ReportService(db, BuildSettings(db));

        var result = await svc.GetIncome(2025);

        Assert.AreEqual(1, result.Sources.Count);
        Assert.AreEqual("Employer Corp", result.Sources[0].Name);
        Assert.IsTrue(result.Sources[0].IsMapped);
        Assert.AreEqual(500m, result.Sources[0].Total);
        Assert.AreEqual(2, result.Sources[0].Count);
    }

    [TestMethod]
    public async Task IncomeCsv_IncludesExpectedHeaders()
    {
        var (_, svc) = await BuildAsync(nameof(IncomeCsv_IncludesExpectedHeaders), []);

        var csv = await svc.IncomeCsv(2025);
        var header = System.Text.Encoding.UTF8.GetString(csv).Split('\n')[0];

        Assert.IsTrue(header.StartsWith("Source,IsMapped,AliasId"));
    }

    // ── Cash Flow ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetCashFlow_ReturnsTwelveMonths()
    {
        var txns = new[]
        {
            MakeTxn("t1", 2025, 6, 1, 100m),
        };
        var (_, svc) = await BuildAsync(nameof(GetCashFlow_ReturnsTwelveMonths), txns);

        var result = await svc.GetCashFlow(2025);

        Assert.AreEqual(12, result.Months.Count);
        Assert.AreEqual("2025-01", result.Months[0].Month);
        Assert.AreEqual("2025-12", result.Months[11].Month);
    }

    [TestMethod]
    public async Task GetCashFlow_ComputesIncomeExpensesAndNet()
    {
        var txns = new[]
        {
            MakeTxn("t1", 2025, 1, 1, -1000m, name: "Payroll"),
            MakeTxn("t2", 2025, 1, 2, 300m, name: "Store"),
        };
        var (_, svc) = await BuildAsync(nameof(GetCashFlow_ComputesIncomeExpensesAndNet), txns);

        var result = await svc.GetCashFlow(2025);
        var jan = result.Months[0];

        Assert.AreEqual(1000m, jan.Income);
        Assert.AreEqual(300m, jan.Expenses);
        Assert.AreEqual(700m, jan.Net);
    }

    [TestMethod]
    public async Task GetCashFlow_YearTotalsMatchMonthlySums()
    {
        var txns = new[]
        {
            MakeTxn("t1", 2025, 1, 1, -1000m, name: "Payroll"),
            MakeTxn("t2", 2025, 1, 2, 300m, name: "Store"),
            MakeTxn("t3", 2025, 2, 1, -500m, name: "Freelance"),
            MakeTxn("t4", 2025, 2, 2, 200m, name: "Market"),
        };
        var (_, svc) = await BuildAsync(nameof(GetCashFlow_YearTotalsMatchMonthlySums), txns);

        var result = await svc.GetCashFlow(2025);

        Assert.AreEqual(result.Months.Sum(m => m.Income), result.TotalIncome);
        Assert.AreEqual(result.Months.Sum(m => m.Expenses), result.TotalExpenses);
        Assert.AreEqual(result.Months.Sum(m => m.Net), result.NetCashFlow);
    }

    [TestMethod]
    public async Task GetCashFlow_IncludesPreviousYearComparison()
    {
        var txns = new[]
        {
            MakeTxn("t1", 2025, 1, 1, -1000m, name: "Payroll"),
            MakeTxn("t2", 2025, 1, 2, 300m, name: "Store"),
            MakeTxn("t3", 2024, 1, 1, -700m, name: "Payroll"),
            MakeTxn("t4", 2024, 1, 2, 200m, name: "Store"),
        };
        var (_, svc) = await BuildAsync(nameof(GetCashFlow_IncludesPreviousYearComparison), txns);

        var result = await svc.GetCashFlow(2025);
        var jan = result.Months[0];

        Assert.AreEqual(500m, jan.PreviousYearNet);
        Assert.AreEqual(200m, jan.ChangeAmount);
        Assert.AreEqual(40m, jan.ChangePercent);
    }

    [TestMethod]
    public async Task GetCashFlow_PreviousNegativeNet_UsesAbsoluteDenominatorForPercent()
    {
        var txns = new[]
        {
            MakeTxn("t1", 2025, 1, 1, -200m, name: "Payroll"),
            MakeTxn("t2", 2025, 1, 2, 100m, name: "Store"),
            MakeTxn("t3", 2024, 1, 1, -100m, name: "Payroll"),
            MakeTxn("t4", 2024, 1, 2, 200m, name: "Store"),
        };
        var (_, svc) = await BuildAsync(
            nameof(GetCashFlow_PreviousNegativeNet_UsesAbsoluteDenominatorForPercent), txns);

        var result = await svc.GetCashFlow(2025);
        var jan = result.Months[0];

        // 2025 net = 200 - 100 = 100
        // 2024 net = 100 - 200 = -100
        // changeAmount = 100 - (-100) = 200
        // changePercent = 200 / abs(-100) * 100 = 200
        Assert.AreEqual(-100m, jan.PreviousYearNet);
        Assert.AreEqual(200m, jan.ChangeAmount);
        Assert.AreEqual(200m, jan.ChangePercent);
    }

    [TestMethod]
    public async Task GetCashFlow_ComputesTrailingThreeMonthRollingAverage()
    {
        var txns = new[]
        {
            MakeTxn("t1", 2025, 1, 1, 100m, name: "Store"),   // net -100 (only expense)
            MakeTxn("t2", 2025, 2, 1, 200m, name: "Store"),   // net -200
            MakeTxn("t3", 2025, 3, 1, -600m, name: "Payroll"), // net +600 income-only month
            MakeTxn("t4", 2025, 4, 1, 600m, name: "Store"),    // expense only (need income too for net)
            MakeTxn("t5", 2025, 4, 2, -1200m, name: "Payroll"),// income
        };
        // Jan: expense 100 → net -100
        // Feb: expense 200 → net -200
        // Mar: income 600 → net 600
        // Apr: expense 600, income 1200 → net 600
        var (_, svc) = await BuildAsync(
            nameof(GetCashFlow_ComputesTrailingThreeMonthRollingAverage), txns);

        var result = await svc.GetCashFlow(2025);

        // Rolling avg: Jan = -100/1 = -100
        // Feb = (-100 + -200)/2 = -150
        // Mar = (-100 + -200 + 600)/3 = 100
        // Apr = (-200 + 600 + 600)/3 = 333.33
        Assert.AreEqual(-100m, result.Months[0].RollingAverageNet);
        Assert.AreEqual(-150m, result.Months[1].RollingAverageNet);
        Assert.AreEqual(100m, result.Months[2].RollingAverageNet);
        Assert.AreEqual(333.33m, result.Months[3].RollingAverageNet);
    }

    [TestMethod]
    public async Task GetCashFlow_IncludesMonthTransactionsWithDirection()
    {
        var txns = new[]
        {
            MakeTxn("t1", 2025, 1, 1, -1000m, name: "Payroll"),
            MakeTxn("t2", 2025, 1, 2, 300m, name: "Store"),
        };
        var (_, svc) = await BuildAsync(
            nameof(GetCashFlow_IncludesMonthTransactionsWithDirection), txns);

        var result = await svc.GetCashFlow(2025);
        var jan = result.Months[0];

        Assert.AreEqual(2, jan.Transactions.Count);

        var income = jan.Transactions.Single(t => t.Direction == "Income");
        Assert.AreEqual(1000m, income.DisplayAmount);

        var expense = jan.Transactions.Single(t => t.Direction == "Expense");
        Assert.AreEqual(300m, expense.DisplayAmount);
    }

    [TestMethod]
    public async Task CashFlowCsv_IncludesExpectedHeaders()
    {
        var (_, svc) = await BuildAsync(nameof(CashFlowCsv_IncludesExpectedHeaders), []);

        var csv = await svc.CashFlowCsv(2025);
        var header = System.Text.Encoding.UTF8.GetString(csv).Split('\n')[0];

        Assert.IsTrue(header.StartsWith("Month,Label,Income,Expenses,Net"));
    }

    // ── Executive Summary ─────────────────────────────────────────────────

    [TestMethod]
    public async Task GetExecutiveSummary_UsesLatestTransactionMonth()
    {
        var txns = new[]
        {
            MakeTxn("t1", 2025, 1, 15, 100m),
            MakeTxn("t2", 2025, 3, 1, 50m),
        };
        var (_, svc) = await BuildAsync(nameof(GetExecutiveSummary_UsesLatestTransactionMonth), txns);

        var result = await svc.GetExecutiveSummary(2025);

        Assert.AreEqual(3, result.Month);
        Assert.AreEqual("2025-03", result.MonthKey);
    }

    [TestMethod]
    public async Task GetExecutiveSummary_EmptyYear_UsesDecemberWithZeroTotals()
    {
        var (_, svc) = await BuildAsync(nameof(GetExecutiveSummary_EmptyYear_UsesDecemberWithZeroTotals), []);

        var result = await svc.GetExecutiveSummary(2025);

        Assert.AreEqual(12, result.Month);
        Assert.AreEqual(0m, result.MonthlyOverview.TotalSpending);
        Assert.AreEqual(0m, result.MonthlyOverview.TotalIncome);
        Assert.AreEqual(0m, result.MonthlyOverview.NetCashFlow);
    }

    [TestMethod]
    public async Task GetExecutiveSummary_ComputesMonthlyOverview()
    {
        var txns = new[]
        {
            MakeTxn("t1", 2025, 3, 1, -1000m, name: "Payroll"),
            MakeTxn("t2", 2025, 3, 2, 300m, name: "Store"),
        };
        var (_, svc) = await BuildAsync(nameof(GetExecutiveSummary_ComputesMonthlyOverview), txns);

        var result = await svc.GetExecutiveSummary(2025);

        Assert.AreEqual(300m, result.MonthlyOverview.TotalSpending);
        Assert.AreEqual(1000m, result.MonthlyOverview.TotalIncome);
        Assert.AreEqual(700m, result.MonthlyOverview.NetCashFlow);
    }

    [TestMethod]
    public async Task GetExecutiveSummary_ComputesPreviousMonthComparison()
    {
        var txns = new[]
        {
            MakeTxn("t1", 2025, 3, 1, -1200m, name: "Payroll"),
            MakeTxn("t2", 2025, 3, 2, 500m, name: "Store"),
            MakeTxn("t3", 2025, 2, 1, -900m, name: "Payroll"),
            MakeTxn("t4", 2025, 2, 2, 400m, name: "Store"),
        };
        var (_, svc) = await BuildAsync(nameof(GetExecutiveSummary_ComputesPreviousMonthComparison), txns);

        var result = await svc.GetExecutiveSummary(2025);

        // Mar net = 1200 - 500 = 700, Feb net = 900 - 400 = 500
        Assert.AreEqual(200m, result.MonthlyOverview.NetCashFlowChangeAmount);
        Assert.AreEqual(40m, result.MonthlyOverview.NetCashFlowChangePercent);
    }

    [TestMethod]
    public async Task GetExecutiveSummary_TopCategories_ReturnsTopFive()
    {
        var txns = Enumerable.Range(1, 6).Select(i =>
            MakeTxn($"t{i}", 2025, 3, i, i * 100m, category: $"CAT_{i}")).ToArray();
        var (_, svc) = await BuildAsync(nameof(GetExecutiveSummary_TopCategories_ReturnsTopFive), txns);

        var result = await svc.GetExecutiveSummary(2025);

        Assert.AreEqual(5, result.TopCategories.Count);
        Assert.AreEqual("CAT_6", result.TopCategories[0].Category);
        Assert.AreEqual(600m, result.TopCategories[0].Total);
        Assert.AreEqual("CAT_5", result.TopCategories[1].Category);
    }

    [TestMethod]
    public async Task GetExecutiveSummary_TopMerchants_GroupsByAliasOrNormalizedName()
    {
        var dbName = nameof(GetExecutiveSummary_TopMerchants_GroupsByAliasOrNormalizedName);
        var db = BuildDb(dbName);
        var alias = new BusinessAlias { AliasName = "Amazon" };
        db.BusinessAliases.Add(alias);
        await db.SaveChangesAsync();

        var txns = new[]
        {
            MakeTxn("t1", 2025, 3, 1, 100m, name: "Amazon Purchase"),
            MakeTxn("t2", 2025, 3, 2, 50m, name: "Amazon Renewal"),
        };
        foreach (var t in txns)
        {
            t.AliasId = alias.Id;
            t.NormalizedName = "amazon";
        }
        db.Transactions.AddRange(txns);
        await db.SaveChangesAsync();

        var svc = new ReportService(db, BuildSettings(db));

        var result = await svc.GetExecutiveSummary(2025);

        Assert.AreEqual(1, result.TopMerchants.Count);
        Assert.AreEqual("Amazon", result.TopMerchants[0].Name);
        Assert.IsTrue(result.TopMerchants[0].IsMapped);
        Assert.AreEqual(150m, result.TopMerchants[0].Total);
    }

    [TestMethod]
    public async Task GetExecutiveSummary_RecurringCharges_DetectsThreeMonthMerchant()
    {
        var txns = new[]
        {
            MakeTxn("t1", 2025, 1, 1, 100m, name: "Netflix"),
            MakeTxn("t2", 2025, 2, 1, 100m, name: "Netflix"),
            MakeTxn("t3", 2025, 3, 1, 100m, name: "Netflix"),
        };
        var (_, svc) = await BuildAsync(
            nameof(GetExecutiveSummary_RecurringCharges_DetectsThreeMonthMerchant), txns);

        var result = await svc.GetExecutiveSummary(2025);

        Assert.AreEqual(1, result.RecurringCharges.Count);
        Assert.AreEqual("Netflix", result.RecurringCharges[0].Name);
        Assert.AreEqual(3, result.RecurringCharges[0].OccurrenceCount);
    }

    [TestMethod]
    public async Task GetExecutiveSummary_Alerts_CountsUncategorizedTransactions()
    {
        var txns = new[]
        {
            MakeTxn("t1", 2025, 1, 1, 100m, category: "Unassigned"),
            MakeTxn("t2", 2025, 1, 2, -50m, category: ""),
        };
        var (_, svc) = await BuildAsync(
            nameof(GetExecutiveSummary_Alerts_CountsUncategorizedTransactions), txns);

        var result = await svc.GetExecutiveSummary(2025);

        var uncat = result.Alerts.Items.FirstOrDefault(a => a.Type == "UncategorizedTransactions");
        Assert.IsNotNull(uncat);
        Assert.AreEqual(2, uncat.Count);
    }

    [TestMethod]
    public async Task GetExecutiveSummary_AccountSummary_GroupsByAccount()
    {
        var txns = new[]
        {
            MakeTxn("t1", 2025, 1, 1, -1000m, name: "Payroll"),
            MakeTxn("t2", 2025, 1, 2, 300m, name: "Store"),
            MakeTxn("t3", 2025, 1, 3, -500m, name: "Freelance"),
        };
        foreach (var t in txns) t.AccountId = "acct-1";
        var txns2 = MakeTxn("t4", 2025, 1, 4, 200m, name: "Other");
        txns2.AccountId = "acct-2";
        var allTxns = txns.Concat(new[] { txns2 }).ToArray();

        var (_, svc) = await BuildAsync(
            nameof(GetExecutiveSummary_AccountSummary_GroupsByAccount), allTxns);

        var result = await svc.GetExecutiveSummary(2025);

        Assert.AreEqual(2, result.Accounts.Count);
        var acct1 = result.Accounts.Single(a => a.AccountId == "acct-1");
        Assert.AreEqual(1500m, acct1.Income);
        Assert.AreEqual(300m, acct1.Expenses);
        Assert.AreEqual(1200m, acct1.NetCashFlow);
        Assert.AreEqual(3, acct1.TransactionCount);
    }

    [TestMethod]
    public async Task ExecutiveSummaryCsv_IncludesExpectedSections()
    {
        var (_, svc) = await BuildAsync(nameof(ExecutiveSummaryCsv_IncludesExpectedSections), []);

        var csv = await svc.ExecutiveSummaryCsv(2025);
        var text = System.Text.Encoding.UTF8.GetString(csv);

        Assert.IsTrue(text.Contains("Overview"));
        Assert.IsTrue(text.Contains("Top Categories"));
        Assert.IsTrue(text.Contains("Top Merchants"));
        Assert.IsTrue(text.Contains("Alerts"));
        Assert.IsTrue(text.Contains("Accounts"));
    }

    [TestMethod]
    public async Task GetPivot_GrandTotal_MatchesSumOfRowTotals()
    {
        var txns = new[]
        {
            MakeTxn("t1", 2025, 1, 1, 100m, category: "FOOD"),
            MakeTxn("t2", 2025, 2, 1, 200m, category: "TRAVEL"),
        };
        var (_, svc) = await BuildAsync(nameof(GetPivot_GrandTotal_MatchesSumOfRowTotals), txns);

        var result = await svc.GetPivot(2025);

        Assert.AreEqual(result.Rows.Sum(r => r.RowTotal), result.GrandTotal);
    }
}