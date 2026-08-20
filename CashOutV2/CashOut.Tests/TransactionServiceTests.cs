using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CashOut.Tests;

[TestClass]
public class TransactionServiceTests
{
    private AppDbContext CreateDb([System.Runtime.CompilerServices.CallerMemberName] string name = "") =>
        TestHelper.CreateInMemoryDb(name);

    // ── Query ─────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Query_FiltersByYear()
    {
        await using var db = CreateDb();
        db.Transactions.AddRange(
            TestHelper.MakeTxn("t1", 2024, 1, 1, 100m),
            TestHelper.MakeTxn("t2", 2025, 1, 1, 200m),
            TestHelper.MakeTxn("t3", 2025, 6, 1, 300m));
        await db.SaveChangesAsync();

        var svc = new TransactionService(db, null!, TestHelper.BuildSettings(db));
        var results = await svc.Query(year: 2025);

        Assert.AreEqual(2, results.Count);
        Assert.IsTrue(results.All(t => t.Date.Year == 2025));
    }

    [TestMethod]
    public async Task Query_FiltersByMonth()
    {
        await using var db = CreateDb();
        db.Transactions.AddRange(
            TestHelper.MakeTxn("t1", 2025, 1, 5, 100m),
            TestHelper.MakeTxn("t2", 2025, 2, 10, 200m));
        await db.SaveChangesAsync();

        var svc = new TransactionService(db, null!, TestHelper.BuildSettings(db));
        var results = await svc.Query(year: 2025, month: 1);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual(1, results[0].Date.Month);
    }

    [TestMethod]
    public async Task Query_FiltersByAccountId()
    {
        await using var db = CreateDb();
        db.Transactions.AddRange(
            TestHelper.MakeTxn("t1", 2025, 1, 1, 100m, accountId: "acct-A"),
            TestHelper.MakeTxn("t2", 2025, 1, 1, 200m, accountId: "acct-B"));
        await db.SaveChangesAsync();

        var svc = new TransactionService(db, null!, TestHelper.BuildSettings(db));
        var results = await svc.Query(accountId: "acct-A");

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("acct-A", results[0].AccountId);
    }

    [TestMethod]
    public async Task Query_FiltersByCategory()
    {
        await using var db = CreateDb();
        db.Transactions.AddRange(
            TestHelper.MakeTxn("t1", 2025, 1, 1, 100m, category: "Food"),
            TestHelper.MakeTxn("t2", 2025, 1, 1, 200m, category: "Travel"));
        await db.SaveChangesAsync();

        var svc = new TransactionService(db, null!, TestHelper.BuildSettings(db));
        var results = await svc.Query(categories: new List<string> { "Food" });

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("Food", results[0].Category);
    }

    [TestMethod]
    public async Task Query_FiltersBySource()
    {
        await using var db = CreateDb();
        db.Transactions.AddRange(
            TestHelper.MakeTxn("t1", 2025, 1, 1, 100m, source: TransactionSource.Plaid),
            TestHelper.MakeTxn("t2", 2025, 1, 1, 200m, source: TransactionSource.CSV));
        await db.SaveChangesAsync();

        var svc = new TransactionService(db, null!, TestHelper.BuildSettings(db));
        var results = await svc.Query(source: TransactionSource.CSV);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual(TransactionSource.CSV, results[0].Source);
    }

    [TestMethod]
    public async Task Query_CombinesFilters()
    {
        await using var db = CreateDb();
        db.Transactions.AddRange(
            TestHelper.MakeTxn("t1", 2025, 1, 1, 100m, accountId: "acct-A", category: "Food"),
            TestHelper.MakeTxn("t2", 2025, 1, 1, 200m, accountId: "acct-A", category: "Travel"),
            TestHelper.MakeTxn("t3", 2025, 1, 1, 300m, accountId: "acct-B", category: "Food"));
        await db.SaveChangesAsync();

        var svc = new TransactionService(db, null!, TestHelper.BuildSettings(db));
        var results = await svc.Query(
            year: 2025, accountId: "acct-A",
            categories: new List<string> { "Food" });

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("t1", results[0].TransactionId);
    }

    [TestMethod]
    public async Task Query_OrderedByDateDescending()
    {
        await using var db = CreateDb();
        db.Transactions.AddRange(
            TestHelper.MakeTxn("t1", 2025, 1, 1, 100m),
            TestHelper.MakeTxn("t2", 2025, 3, 1, 300m),
            TestHelper.MakeTxn("t3", 2025, 2, 1, 200m));
        await db.SaveChangesAsync();

        var svc = new TransactionService(db, null!, TestHelper.BuildSettings(db));
        var results = await svc.Query(year: 2025);

        Assert.AreEqual("2025-03-01", results[0].Date.ToString("yyyy-MM-dd"));
        Assert.AreEqual("2025-02-01", results[1].Date.ToString("yyyy-MM-dd"));
        Assert.AreEqual("2025-01-01", results[2].Date.ToString("yyyy-MM-dd"));
    }

    // ── UpdateCategory ────────────────────────────────────────────────────

    [TestMethod]
    public async Task UpdateCategory_UpdatesAndReturnsTransaction()
    {
        await using var db = CreateDb();
        var txn = TestHelper.MakeTxn("t1", 2025, 1, 1, 100m, category: "");
        db.Transactions.Add(txn);
        await db.SaveChangesAsync();

        var svc = new TransactionService(db, null!, TestHelper.BuildSettings(db));
        var result = await svc.UpdateCategory("t1", "Food");

        Assert.IsNotNull(result);
        Assert.AreEqual("Food", result.Category);

        var fromDb = await db.Transactions.FindAsync("t1");
        Assert.AreEqual("Food", fromDb!.Category);
    }

    [TestMethod]
    public async Task UpdateCategory_ReturnsNullForMissingTransaction()
    {
        await using var db = CreateDb();
        var svc = new TransactionService(db, null!, TestHelper.BuildSettings(db));

        var result = await svc.UpdateCategory("nonexistent", "Food");

        Assert.IsNull(result);
    }

    // ── ExportCsv ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ExportCsv_IncludesExpectedHeaders()
    {
        await using var db = CreateDb();
        db.Transactions.Add(TestHelper.MakeTxn("t1", 2025, 1, 1, 100m));
        await db.SaveChangesAsync();

        var svc = new TransactionService(db, null!, TestHelper.BuildSettings(db));
        var csv = await svc.ExportCsv(2025);
        var header = System.Text.Encoding.UTF8.GetString(csv).Split('\n')[0];

        Assert.IsTrue(header.StartsWith("Date,Name,Debit,Credit,Amount,Category,Source,TransactionId,AccountId"));
    }

    [TestMethod]
    public async Task ExportCsv_IncludesTransactionData()
    {
        await using var db = CreateDb();
        var txn = TestHelper.MakeTxn("t1", 2025, 1, 1, 50m, name: "Coffee", category: "Food");
        db.Transactions.Add(txn);
        await db.SaveChangesAsync();

        var svc = new TransactionService(db, null!, TestHelper.BuildSettings(db));
        var csv = await svc.ExportCsv(2025);
        var text = System.Text.Encoding.UTF8.GetString(csv);

        Assert.IsTrue(text.Contains("Coffee"));
        Assert.IsTrue(text.Contains("Food"));
        Assert.IsTrue(text.Contains("t1"));
    }

    [TestMethod]
    public async Task ExportCsv_FiltersByYear()
    {
        await using var db = CreateDb();
        db.Transactions.AddRange(
            TestHelper.MakeTxn("t1", 2024, 1, 1, 100m, name: "Old"),
            TestHelper.MakeTxn("t2", 2025, 1, 1, 200m, name: "New"));
        await db.SaveChangesAsync();

        var svc = new TransactionService(db, null!, TestHelper.BuildSettings(db));
        var csv = await svc.ExportCsv(2025);
        var text = System.Text.Encoding.UTF8.GetString(csv);

        Assert.IsTrue(text.Contains("New"));
        Assert.IsFalse(text.Contains("Old"));
    }
}
