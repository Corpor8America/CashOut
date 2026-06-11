using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Spening.Tests;

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

    private static IConfiguration BuildConfig(Dictionary<string, string>? initialData = null)
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

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("TRAVEL", result[0].Category);
        Assert.AreEqual(200m, result[0].Total);
        Assert.AreEqual("FOOD_AND_DRINK", result[1].Category);
        Assert.AreEqual(125m, result[1].Total);
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
        var totalPct = result.Sum(r => r.PctOfSpend);

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

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("(uncategorized)", result[0].Category);
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

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("Airline", result[0].Name);
        Assert.AreEqual("Grocery", result[1].Name);
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

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(45m, result[0].AvgPerVisit);
        Assert.AreEqual(2, result[0].Count);
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