using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CashOut.Tests;

[TestClass]
public class CategoryServiceTests
{
    private AppDbContext CreateDb([System.Runtime.CompilerServices.CallerMemberName] string name = "") =>
        TestHelper.CreateInMemoryDb(name);

    [TestMethod]
    public async Task Create_TrimsName_ReturnsCategory()
    {
        await using var db = CreateDb();
        var svc = new CategoryService(db);

        var result = await svc.Create("  Groceries  ");

        Assert.AreEqual("Groceries", result.Name);
        Assert.IsTrue(result.Id > 0);
    }

    [TestMethod]
    public async Task Update_ExistingId_UpdatesName()
    {
        await using var db = CreateDb();
        var cat = new Category { Name = "Old", UpdatedAt = DateTime.UtcNow };
        db.Categories.Add(cat);
        await db.SaveChangesAsync();

        var svc = new CategoryService(db);
        var result = await svc.Update(cat.Id, "New Name");

        Assert.IsNotNull(result);
        Assert.AreEqual("New Name", result.Name);
    }

    [TestMethod]
    public async Task Delete_ExistingId_RemovesCategoryAndRules()
    {
        await using var db = CreateDb();
        var cat = new Category { Name = "Food", UpdatedAt = DateTime.UtcNow };
        db.Categories.Add(cat);
        await db.SaveChangesAsync();

        var rule = new CategoryRule { Pattern = "Lion", CategoryId = cat.Id, UpdatedAt = DateTime.UtcNow };
        db.CategoryRules.Add(rule);
        await db.SaveChangesAsync();

        var svc = new CategoryService(db);
        var result = await svc.Delete(cat.Id);

        Assert.IsTrue(result);
        Assert.AreEqual(0, await db.Categories.CountAsync());
        Assert.AreEqual(0, await db.CategoryRules.CountAsync());
    }

    [TestMethod]
    public async Task Delete_CategoryWithManualAssignments_UncategorizesTransactions()
    {
        await using var db = CreateDb();
        var cat = new Category { Name = "Food", UpdatedAt = DateTime.UtcNow };
        db.Categories.Add(cat);
        await db.SaveChangesAsync();

        var txn = TestHelper.MakeTxn("t1", 2025, 1, 1, 100m);
        txn.CategoryId = cat.Id;
        txn.CategoryRuleId = null;
        db.Transactions.Add(txn);
        await db.SaveChangesAsync();

        var svc = new CategoryService(db);
        await svc.Delete(cat.Id);

        var fromDb = await db.Transactions.FindAsync("t1");
        Assert.IsNotNull(fromDb);
        Assert.IsNull(fromDb.CategoryId);
        Assert.IsNull(fromDb.CategoryRuleId);
    }
}
