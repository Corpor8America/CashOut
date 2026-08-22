using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CashOut.Tests;

[TestClass]
public class ReportServiceTests
{
    private AppDbContext CreateDb([System.Runtime.CompilerServices.CallerMemberName] string name = "") =>
        TestHelper.CreateInMemoryDb(name);

    private async Task<(AppDbContext db, ReportService svc)> BuildSvc(
        string dbName, IEnumerable<Transaction> transactions)
    {
        var db = CreateDb(dbName);
        db.Transactions.AddRange(transactions);
        await db.SaveChangesAsync();
        var svc = new ReportService(db, TestHelper.BuildSettings(db));
        return (db, svc);
    }

    // ── GetMonthly ────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetMonthly_GroupsByMonth_AndSumsCorrectly()
    {
        var txns = new[]
        {
            TestHelper.MakeTxn("t1", 2025, 1, 5, 100m),
            TestHelper.MakeTxn("t2", 2025, 1, 15, 50m),
            TestHelper.MakeTxn("t3", 2025, 2, 10, 200m),
        };
        var (db, svc) = await BuildSvc(nameof(GetMonthly_GroupsByMonth_AndSumsCorrectly), txns);

        var rows = await svc.GetMonthly(2025);

        Assert.AreEqual(2, rows.Count);
        Assert.AreEqual(150m, rows[0].Total);
        Assert.AreEqual(2, rows[0].Count);
        Assert.AreEqual(200m, rows[1].Total);
        Assert.AreEqual(1, rows[1].Count);
    }

    [TestMethod]
    public async Task GetMonthly_ExcludesNegativeAmounts()
    {
        var txns = new[]
        {
            TestHelper.MakeTxn("t1", 2025, 1, 1, 100m),
            TestHelper.MakeTxn("t2", 2025, 1, 2, -40m),
        };
        var (db, svc) = await BuildSvc(nameof(GetMonthly_ExcludesNegativeAmounts), txns);

        var rows = await svc.GetMonthly(2025);

        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(100m, rows[0].Total);
        Assert.AreEqual(1, rows[0].Count);
    }

    [TestMethod]
    public async Task GetMonthly_ReturnsEmpty_WhenNoTransactions()
    {
        var (db, svc) = await BuildSvc(nameof(GetMonthly_ReturnsEmpty_WhenNoTransactions), []);
        var rows = await svc.GetMonthly(2025);
        Assert.AreEqual(0, rows.Count);
    }

    [TestMethod]
    public async Task GetMonthly_OrderedChronologically()
    {
        var txns = new[]
        {
            TestHelper.MakeTxn("t1", 2025, 6, 1, 10m),
            TestHelper.MakeTxn("t2", 2025, 2, 1, 20m),
            TestHelper.MakeTxn("t3", 2025, 9, 1, 30m),
        };
        var (db, svc) = await BuildSvc(nameof(GetMonthly_OrderedChronologically), txns);

        var rows = await svc.GetMonthly(2025);

        Assert.AreEqual("2025-02", rows[0].Month);
        Assert.AreEqual("2025-06", rows[1].Month);
        Assert.AreEqual("2025-09", rows[2].Month);
    }

    [TestMethod]
    public async Task GetMonthly_FiltersByYear()
    {
        var txns = new[]
        {
            TestHelper.MakeTxn("t1", 2024, 1, 5, -100m),
            TestHelper.MakeTxn("t2", 2025, 1, 15, 200m),
        };
        var (db, svc) = await BuildSvc(nameof(GetMonthly_FiltersByYear), txns);

        var rows = await svc.GetMonthly(2025);

        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(200m, rows[0].Total);
    }

    [TestMethod]
    public async Task GetMonthly_MultipleTransactionsInMonth_SumAll()
    {
        var txns = new[]
        {
            TestHelper.MakeTxn("t1", 2025, 3, 1, 50m),
            TestHelper.MakeTxn("t2", 2025, 3, 10, 75m),
            TestHelper.MakeTxn("t3", 2025, 3, 20, 25m),
        };
        var (db, svc) = await BuildSvc(nameof(GetMonthly_MultipleTransactionsInMonth_SumAll), txns);

        var rows = await svc.GetMonthly(2025);

        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(150m, rows[0].Total);
        Assert.AreEqual(3, rows[0].Count);
    }

    // ── GetByCategory ─────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetByCategory_GroupsByCategory_AndSortsDescending()
    {
        var txns = new[]
        {
            TestHelper.MakeTxn("t1", 2025, 3, 1, 50m, category: "Food"),
            TestHelper.MakeTxn("t2", 2025, 3, 5, 30m, category: "Food"),
            TestHelper.MakeTxn("t3", 2025, 3, 10, 100m, category: "Transport"),
        };
        var (db, svc) = await BuildSvc(nameof(GetByCategory_GroupsByCategory_AndSortsDescending), txns);

        var result = await svc.GetByCategory(2025);

        Assert.AreEqual(2, result.Categories.Count);
        Assert.AreEqual("Transport", result.Categories[0].Category);
        Assert.AreEqual(100m, result.Categories[0].Total);
        Assert.AreEqual("Food", result.Categories[1].Category);
        Assert.AreEqual(80m, result.Categories[1].Total);
    }

    [TestMethod]
    public async Task GetByCategory_PctOfSpend_SumsToHundred()
    {
        var txns = new[]
        {
            TestHelper.MakeTxn("t1", 2025, 1, 1, 300m, category: "Food"),
            TestHelper.MakeTxn("t2", 2025, 1, 2, 100m, category: "Travel"),
            TestHelper.MakeTxn("t3", 2025, 1, 3, 100m, category: "Shopping"),
        };
        var (db, svc) = await BuildSvc(nameof(GetByCategory_PctOfSpend_SumsToHundred), txns);

        var result = await svc.GetByCategory(2025);
        var totalPct = result.Categories.Sum(r => r.PctOfSpend);

        Assert.AreEqual(100m, totalPct);
    }

    [TestMethod]
    public async Task GetByCategory_EmptyCategory_MarkedUncategorized()
    {
        var txns = new[]
        {
            TestHelper.MakeTxn("t1", 2025, 1, 1, 50m, category: ""),
        };
        var (db, svc) = await BuildSvc(nameof(GetByCategory_EmptyCategory_MarkedUncategorized), txns);

        var result = await svc.GetByCategory(2025);

        Assert.AreEqual(1, result.Categories.Count);
        Assert.AreEqual("(uncategorized)", result.Categories[0].Category);
    }

    [TestMethod]
    public async Task GetByCategory_IncludesPreviousYearComparison()
    {
        var txns = new[]
        {
            TestHelper.MakeTxn("t1", 2025, 1, 1, 100m, category: "Food"),
            TestHelper.MakeTxn("t2", 2025, 1, 2, 100m, category: "Food"),
            TestHelper.MakeTxn("t3", 2024, 6, 1, 100m, category: "Food"),
        };
        var (db, svc) = await BuildSvc(nameof(GetByCategory_IncludesPreviousYearComparison), txns);

        var result = await svc.GetByCategory(2025);

        var food = result.Categories.Single(c => c.Category == "Food");
        Assert.AreEqual(100m, food.PreviousTotal);
        Assert.AreEqual(100m, food.ChangeAmount);
        Assert.AreEqual(100m, food.ChangePercent);
    }

    [TestMethod]
    public async Task GetByCategory_PreviousZero_ReturnsZeroChangePercent()
    {
        var txns = new[]
        {
            TestHelper.MakeTxn("t1", 2025, 1, 1, 50m, category: "Food"),
        };
        var (db, svc) = await BuildSvc(nameof(GetByCategory_PreviousZero_ReturnsZeroChangePercent), txns);

        var result = await svc.GetByCategory(2025);

        var food = result.Categories.Single(c => c.Category == "Food");
        Assert.AreEqual(0m, food.PreviousTotal);
        Assert.AreEqual(50m, food.ChangeAmount);
        Assert.AreEqual(0m, food.ChangePercent);
    }

    [TestMethod]
    public async Task GetByCategory_IncludesTransactionsForEachCategory()
    {
        var txns = new[]
        {
            TestHelper.MakeTxn("t1", 2025, 1, 1, 100m, category: "Food"),
            TestHelper.MakeTxn("t2", 2025, 1, 2, 50m, category: "Food"),
            TestHelper.MakeTxn("t3", 2025, 1, 3, 200m, category: "Travel"),
        };
        var (db, svc) = await BuildSvc(
            nameof(GetByCategory_IncludesTransactionsForEachCategory), txns);

        var result = await svc.GetByCategory(2025);

        var food = result.Categories.Single(c => c.Category == "Food");
        Assert.AreEqual(2, food.Transactions.Count);
        Assert.IsTrue(food.Transactions.All(t => t.Category == "Food"));
    }

    [TestMethod]
    public async Task GetByCategory_DoesNotIncludePreviousOnlyCategories()
    {
        var txns = new[]
        {
            TestHelper.MakeTxn("t1", 2024, 6, 1, 100m, category: "Travel"),
            TestHelper.MakeTxn("t2", 2025, 1, 1, 50m, category: "Food"),
        };
        var (db, svc) = await BuildSvc(
            nameof(GetByCategory_DoesNotIncludePreviousOnlyCategories), txns);

        var result = await svc.GetByCategory(2025);

        Assert.AreEqual(1, result.Categories.Count);
        Assert.AreEqual("Food", result.Categories[0].Category);
    }

    [TestMethod]
    public async Task GetByCategory_MonthFilter_ReturnsOnlyThatMonthsTransactions()
    {
        var txns = new[]
        {
            TestHelper.MakeTxn("t1", 2025, 1, 15, 100m, category: "Food"),
            TestHelper.MakeTxn("t2", 2025, 2, 10, 50m, category: "Food"),
        };
        var (db, svc) = await BuildSvc(
            nameof(GetByCategory_MonthFilter_ReturnsOnlyThatMonthsTransactions), txns);

        var result = await svc.GetByCategory(2025, 1);

        Assert.AreEqual(100m, result.GrandTotal);
        var food = result.Categories.Single(c => c.Category == "Food");
        Assert.AreEqual(1, food.Transactions.Count);
        Assert.AreEqual("t1", food.Transactions[0].TransactionId);
    }

    [TestMethod]
    public async Task GetByCategory_MonthFilter_PreviousYearTotalStillUsesFullYear()
    {
        var txns = new[]
        {
            TestHelper.MakeTxn("t1", 2025, 1, 15, 100m, category: "Food"),
            TestHelper.MakeTxn("t2", 2025, 1, 20, 50m, category: "Food"),
            TestHelper.MakeTxn("t3", 2024, 7, 1, 200m, category: "Food"),
        };
        var (db, svc) = await BuildSvc(
            nameof(GetByCategory_MonthFilter_PreviousYearTotalStillUsesFullYear), txns);

        var result = await svc.GetByCategory(2025, 1);

        var food = result.Categories.Single(c => c.Category == "Food");
        Assert.AreEqual(200m, food.PreviousTotal);
    }

    [TestMethod]
    public async Task GetByCategory_IncludesYearOverYearTotals()
    {
        var txns = new[]
        {
            TestHelper.MakeTxn("t1", 2025, 1, 1, 100m, category: "Food"),
            TestHelper.MakeTxn("t2", 2024, 6, 1, 50m, category: "Food"),
        };
        var (db, svc) = await BuildSvc(
            nameof(GetByCategory_IncludesYearOverYearTotals), txns);

        var result = await svc.GetByCategory(2025);

        Assert.AreEqual(100m, result.GrandTotal);
        Assert.AreEqual(50m, result.PreviousGrandTotal);
        Assert.AreEqual(50m, result.ChangeAmount);
        Assert.AreEqual(100m, result.ChangePercent);
    }

    // ── GetCategoryDetail ────────────────────────────────────────────────

    [TestMethod]
    public async Task GetCategoryDetail_GroupsByCategory_AndSortsDescending()
    {
        var txns = new[]
        {
            TestHelper.MakeTxn("t1", 2025, 3, 1, 50m, category: "Food"),
            TestHelper.MakeTxn("t2", 2025, 3, 5, 30m, category: "Food"),
            TestHelper.MakeTxn("t3", 2025, 3, 10, 100m, category: "Transport"),
        };
        var (db, svc) = await BuildSvc(nameof(GetCategoryDetail_GroupsByCategory_AndSortsDescending), txns);

        var result = await svc.GetCategoryDetail(fromYear: 2025, fromMonth: 1, toYear: 2025, toMonth: 12);

        Assert.AreEqual(2, result.Categories.Count);
        // Signed amounts: Food=-80, Transport=-100; descending: -80 > -100
        Assert.AreEqual("Food", result.Categories[0].Category);
        Assert.AreEqual(-80m, result.Categories[0].Total);
        Assert.AreEqual("Transport", result.Categories[1].Category);
        Assert.AreEqual(-100m, result.Categories[1].Total);
    }

    [TestMethod]
    public async Task GetCategoryDetail_ComputesAvgPerMonth()
    {
        var txns = new[]
        {
            TestHelper.MakeTxn("t1", 2025, 1, 1, 300m, category: "Food"),
            TestHelper.MakeTxn("t2", 2025, 2, 1, 300m, category: "Food"),
            TestHelper.MakeTxn("t3", 2025, 3, 1, 300m, category: "Food"),
        };
        var (db, svc) = await BuildSvc(nameof(GetCategoryDetail_ComputesAvgPerMonth), txns);

        var result = await svc.GetCategoryDetail(fromYear: 2025, fromMonth: 1, toYear: 2025, toMonth: 3);

        var food = result.Categories.Single(c => c.Category == "Food");
        Assert.AreEqual(-900m, food.Total);
        Assert.AreEqual(-300m, food.AvgPerMonth);
        Assert.AreEqual(300m, result.AvgExpensesPerMonth);
    }

    [TestMethod]
    public async Task GetCategoryDetail_FiltersByAccountId()
    {
        var txns = new[]
        {
            TestHelper.MakeTxn("t1", 2025, 1, 1, 100m, category: "Food", accountId: "acct-A"),
            TestHelper.MakeTxn("t2", 2025, 1, 2, 50m, category: "Food", accountId: "acct-B"),
            TestHelper.MakeTxn("t3", 2025, 1, 3, 200m, category: "Travel", accountId: "acct-A"),
        };
        var (db, svc) = await BuildSvc(nameof(GetCategoryDetail_FiltersByAccountId), txns);

        var result = await svc.GetCategoryDetail(fromYear: 2025, fromMonth: 1, toYear: 2025, toMonth: 12, accountId: "acct-A");

        Assert.AreEqual(2, result.Categories.Count);
        Assert.AreEqual(300m, result.TotalExpenses);
        Assert.AreEqual(2, result.TransactionCount);
    }

    [TestMethod]
    public async Task GetCategoryDetail_IncludesAccountNames()
    {
        var acctAId = Guid.NewGuid();
        var acctBId = Guid.NewGuid();
        var db = CreateDb(nameof(GetCategoryDetail_IncludesAccountNames));
        db.Transactions.AddRange(
            TestHelper.MakeTxn("t1", 2025, 1, 1, 100m, category: "Food", accountId: acctAId.ToString()),
            TestHelper.MakeTxn("t2", 2025, 1, 2, 50m, category: "Food", accountId: acctBId.ToString())
        );
        db.Accounts.Add(new Account { Id = acctAId, Name = "Chase Checking" });
        db.Accounts.Add(new Account { Id = acctBId, Name = "Cash Wallet" });
        await db.SaveChangesAsync();
        var svc = new ReportService(db, TestHelper.BuildSettings(db));

        var result = await svc.GetCategoryDetail(fromYear: 2025, fromMonth: 1, toYear: 2025, toMonth: 12);

        var food = result.Categories.Single(c => c.Category == "Food");
        Assert.AreEqual("Chase Checking", food.Transactions.Single(t => t.AccountId == acctAId.ToString()).AccountName);
        Assert.AreEqual("Cash Wallet", food.Transactions.Single(t => t.AccountId == acctBId.ToString()).AccountName);
    }

    [TestMethod]
    public async Task GetCategoryDetail_MonthRange_FiltersCorrectly()
    {
        var txns = new[]
        {
            TestHelper.MakeTxn("t1", 2025, 1, 1, 100m, category: "Food"),
            TestHelper.MakeTxn("t2", 2025, 2, 1, 200m, category: "Food"),
            TestHelper.MakeTxn("t3", 2025, 3, 1, 300m, category: "Food"),
            TestHelper.MakeTxn("t4", 2025, 6, 1, 400m, category: "Food"),
        };
        var (db, svc) = await BuildSvc(nameof(GetCategoryDetail_MonthRange_FiltersCorrectly), txns);

        var result = await svc.GetCategoryDetail(fromYear: 2025, fromMonth: 1, toYear: 2025, toMonth: 3);

        Assert.AreEqual(600m, result.TotalExpenses);
        Assert.AreEqual(3, result.TransactionCount);
        var food = result.Categories.Single(c => c.Category == "Food");
        Assert.AreEqual(3, food.Transactions.Count);
    }

    [TestMethod]
    public async Task GetCategoryDetail_IncludesTransactionsPerCategory()
    {
        var txns = new[]
        {
            TestHelper.MakeTxn("t1", 2025, 1, 1, 100m, category: "Food"),
            TestHelper.MakeTxn("t2", 2025, 1, 2, 50m, category: "Food"),
            TestHelper.MakeTxn("t3", 2025, 1, 3, 200m, category: "Travel"),
        };
        var (db, svc) = await BuildSvc(
            nameof(GetCategoryDetail_IncludesTransactionsPerCategory), txns);

        var result = await svc.GetCategoryDetail(fromYear: 2025, fromMonth: 1, toYear: 2025, toMonth: 12);

        var food = result.Categories.Single(c => c.Category == "Food");
        Assert.AreEqual(2, food.Transactions.Count);
        Assert.IsTrue(food.Transactions.All(t => t.Category == "Food"));
        Assert.IsTrue(food.Transactions.All(t => !string.IsNullOrEmpty(t.AccountName)));
    }

    // ── GetCashFlow ───────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetCashFlow_ReturnsTwelveMonths()
    {
        var txns = new[]
        {
            TestHelper.MakeTxn("t1", 2025, 6, 1, 100m),
        };
        var (db, svc) = await BuildSvc(nameof(GetCashFlow_ReturnsTwelveMonths), txns);

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
            TestHelper.MakeTxn("t1", 2025, 1, 1, -1000m, name: "Payroll"),
            TestHelper.MakeTxn("t2", 2025, 1, 2, 300m, name: "Store"),
        };
        var (db, svc) = await BuildSvc(nameof(GetCashFlow_ComputesIncomeExpensesAndNet), txns);

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
            TestHelper.MakeTxn("t1", 2025, 1, 1, -1000m, name: "Payroll"),
            TestHelper.MakeTxn("t2", 2025, 1, 2, 300m, name: "Store"),
            TestHelper.MakeTxn("t3", 2025, 2, 1, -500m, name: "Freelance"),
            TestHelper.MakeTxn("t4", 2025, 2, 2, 200m, name: "Market"),
        };
        var (db, svc) = await BuildSvc(nameof(GetCashFlow_YearTotalsMatchMonthlySums), txns);

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
            TestHelper.MakeTxn("t1", 2025, 1, 1, -1000m, name: "Payroll"),
            TestHelper.MakeTxn("t2", 2025, 1, 2, 300m, name: "Store"),
            TestHelper.MakeTxn("t3", 2024, 1, 1, -700m, name: "Payroll"),
            TestHelper.MakeTxn("t4", 2024, 1, 2, 200m, name: "Store"),
        };
        var (db, svc) = await BuildSvc(nameof(GetCashFlow_IncludesPreviousYearComparison), txns);

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
            TestHelper.MakeTxn("t1", 2025, 1, 1, -200m, name: "Payroll"),
            TestHelper.MakeTxn("t2", 2025, 1, 2, 100m, name: "Store"),
            TestHelper.MakeTxn("t3", 2024, 1, 1, -100m, name: "Payroll"),
            TestHelper.MakeTxn("t4", 2024, 1, 2, 200m, name: "Store"),
        };
        var (db, svc) = await BuildSvc(
            nameof(GetCashFlow_PreviousNegativeNet_UsesAbsoluteDenominatorForPercent), txns);

        var result = await svc.GetCashFlow(2025);
        var jan = result.Months[0];

        Assert.AreEqual(-100m, jan.PreviousYearNet);
        Assert.AreEqual(200m, jan.ChangeAmount);
        Assert.AreEqual(200m, jan.ChangePercent);
    }

    [TestMethod]
    public async Task GetCashFlow_ComputesTrailingThreeMonthRollingAverage()
    {
        var txns = new[]
        {
            TestHelper.MakeTxn("t1", 2025, 1, 1, 100m, name: "Store"),
            TestHelper.MakeTxn("t2", 2025, 2, 1, 200m, name: "Store"),
            TestHelper.MakeTxn("t3", 2025, 3, 1, -600m, name: "Payroll"),
            TestHelper.MakeTxn("t4", 2025, 4, 1, 600m, name: "Store"),
            TestHelper.MakeTxn("t5", 2025, 4, 2, -1200m, name: "Payroll"),
        };
        var (db, svc) = await BuildSvc(
            nameof(GetCashFlow_ComputesTrailingThreeMonthRollingAverage), txns);

        var result = await svc.GetCashFlow(2025);

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
            TestHelper.MakeTxn("t1", 2025, 1, 1, -1000m, name: "Payroll"),
            TestHelper.MakeTxn("t2", 2025, 1, 2, 300m, name: "Store"),
        };
        var (db, svc) = await BuildSvc(
            nameof(GetCashFlow_IncludesMonthTransactionsWithDirection), txns);

        var result = await svc.GetCashFlow(2025);
        var jan = result.Months[0];

        Assert.AreEqual(2, jan.Transactions.Count);

        var income = jan.Transactions.Single(t => t.Amount > 0);
        Assert.AreEqual(1000m, income.Amount);

        var expense = jan.Transactions.Single(t => t.Amount < 0);
        Assert.AreEqual(-300m, expense.Amount);
    }

    [TestMethod]
    public async Task GetCashFlow_BestAndWorstMonthsAreCorrect()
    {
        var txns = new[]
        {
            TestHelper.MakeTxn("t1", 2025, 1, 1, -500m, name: "Payroll"),
            TestHelper.MakeTxn("t2", 2025, 1, 2, 100m, name: "Store"),
            TestHelper.MakeTxn("t3", 2025, 2, 1, -200m, name: "Payroll"),
            TestHelper.MakeTxn("t4", 2025, 2, 2, 300m, name: "Store"),
        };
        var (db, svc) = await BuildSvc(
            nameof(GetCashFlow_BestAndWorstMonthsAreCorrect), txns);

        var result = await svc.GetCashFlow(2025);

        // Jan net = 500-100 = 400, Feb net = 200-300 = -100
        Assert.AreEqual(400m, result.BestMonthNet);
        Assert.AreEqual("Jan 2025", result.BestMonthLabel);
        Assert.AreEqual(-100m, result.WorstMonthNet);
        Assert.AreEqual("Feb 2025", result.WorstMonthLabel);
    }

    // ── CSV exports ───────────────────────────────────────────────────────

    [TestMethod]
    public async Task MonthlyCsv_IncludesExpectedHeaders()
    {
        var (db, svc) = await BuildSvc(nameof(MonthlyCsv_IncludesExpectedHeaders), []);

        var csv = await svc.MonthlyCsv(2025);
        var header = System.Text.Encoding.UTF8.GetString(csv).Split('\n')[0];

        Assert.IsTrue(header.StartsWith("Month,Label,Total,Transactions"));
    }

    [TestMethod]
    public async Task CategoryCsv_IncludesExpectedHeaders()
    {
        var (db, svc) = await BuildSvc(nameof(CategoryCsv_IncludesExpectedHeaders), []);

        var csv = await svc.CategoryCsv(2025);
        var header = System.Text.Encoding.UTF8.GetString(csv).Split('\n')[0];

        Assert.IsTrue(header.StartsWith("Category,Total,PctOfSpend,Transactions"));
    }

    [TestMethod]
    public async Task CashFlowCsv_IncludesExpectedHeaders()
    {
        var (db, svc) = await BuildSvc(nameof(CashFlowCsv_IncludesExpectedHeaders), []);

        var csv = await svc.CashFlowCsv(2025);
        var header = System.Text.Encoding.UTF8.GetString(csv).Split('\n')[0];

        Assert.IsTrue(header.StartsWith("Month,Label,Income,Expenses,Net"));
    }

    // ── GetCashFlow filtering ─────────────────────────────────────────────

    [TestMethod]
    public async Task GetCashFlow_FiltersByAccountId()
    {
        var txns = new[]
        {
            TestHelper.MakeTxn("t1", 2025, 1, 1, -1000m, name: "Payroll", accountId: "acct-A"),
            TestHelper.MakeTxn("t2", 2025, 1, 2, 300m, name: "Store", accountId: "acct-A"),
            TestHelper.MakeTxn("t3", 2025, 1, 3, -500m, name: "Freelance", accountId: "acct-B"),
        };
        var (db, svc) = await BuildSvc(nameof(GetCashFlow_FiltersByAccountId), txns);

        var result = await svc.GetCashFlow(2025, accountId: "acct-A");
        var jan = result.Months[0];

        Assert.AreEqual(1000m, jan.Income);
        Assert.AreEqual(300m, jan.Expenses);
        Assert.AreEqual(700m, jan.Net);
        Assert.AreEqual(2, result.TransactionCount);
    }

    [TestMethod]
    public async Task GetCashFlow_MonthRange_FiltersMonths()
    {
        var txns = new[]
        {
            TestHelper.MakeTxn("t1", 2025, 1, 1, -1000m, name: "Payroll"),
            TestHelper.MakeTxn("t2", 2025, 2, 1, -500m, name: "Freelance"),
            TestHelper.MakeTxn("t3", 2025, 3, 1, 200m, name: "Store"),
            TestHelper.MakeTxn("t4", 2025, 6, 1, -800m, name: "Payroll"),
        };
        var (db, svc) = await BuildSvc(nameof(GetCashFlow_MonthRange_FiltersMonths), txns);

        var result = await svc.GetCashFlow(2025, fromMonth: 1, toMonth: 3);

        Assert.AreEqual(3, result.Months.Count);
        Assert.AreEqual("2025-01", result.Months[0].Month);
        Assert.AreEqual("2025-03", result.Months[2].Month);
        Assert.AreEqual(1500m, result.TotalIncome);
        Assert.AreEqual(200m, result.TotalExpenses);
    }

    [TestMethod]
    public async Task GetCashFlow_CombinedAccountAndMonthFilter()
    {
        var txns = new[]
        {
            TestHelper.MakeTxn("t1", 2025, 1, 1, -1000m, name: "Payroll", accountId: "acct-A"),
            TestHelper.MakeTxn("t2", 2025, 2, 1, 300m, name: "Store", accountId: "acct-A"),
            TestHelper.MakeTxn("t3", 2025, 3, 1, -500m, name: "Freelance", accountId: "acct-B"),
            TestHelper.MakeTxn("t4", 2025, 1, 1, -200m, name: "Side", accountId: "acct-B"),
        };
        var (db, svc) = await BuildSvc(nameof(GetCashFlow_CombinedAccountAndMonthFilter), txns);

        var result = await svc.GetCashFlow(2025, accountId: "acct-A", fromMonth: 1, toMonth: 2);

        Assert.AreEqual(2, result.Months.Count);
        Assert.AreEqual(1000m, result.TotalIncome);
        Assert.AreEqual(300m, result.TotalExpenses);
        Assert.AreEqual(2, result.TransactionCount);
    }
}
