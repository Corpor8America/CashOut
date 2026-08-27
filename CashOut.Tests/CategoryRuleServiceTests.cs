using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CashOut.Tests;

[TestClass]
public class CategoryRuleServiceTests
{
    private AppDbContext CreateDb([System.Runtime.CompilerServices.CallerMemberName] string name = "") =>
        TestHelper.CreateInMemoryDb(name);

    private async Task<Category> SeedCategory(AppDbContext db, string name = "Groceries")
    {
        var cat = new Category { Name = name, UpdatedAt = DateTime.UtcNow };
        db.Categories.Add(cat);
        await db.SaveChangesAsync();
        return cat;
    }

    // ── Match ───────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Match_ContainsPattern_CaseInsensitive_MatchesStoreVariants()
    {
        await using var db = CreateDb();
        var cat = await SeedCategory(db);
        var svc = new CategoryRuleService(db);
        var rules = new List<CategoryRule>
        {
            new() { Id = 1, Pattern = "Food Lion", CategoryId = cat.Id, Category = cat, UpdatedAt = DateTime.UtcNow }
        };

        var result = svc.Match("FOOD LION #1234", rules);

        Assert.AreEqual("Groceries", result);
    }

    [TestMethod]
    public async Task Match_MultipleMatches_LongestPatternWins()
    {
        await using var db = CreateDb();
        var cat = await SeedCategory(db);
        var svc = new CategoryRuleService(db);
        var rules = new List<CategoryRule>
        {
            new() { Id = 1, Pattern = "Food", CategoryId = cat.Id, Category = cat, UpdatedAt = DateTime.UtcNow },
            new() { Id = 2, Pattern = "Food Lion", CategoryId = cat.Id, Category = cat, UpdatedAt = DateTime.UtcNow }
        };

        var result = svc.Match("Food Lion #1234", rules);

        Assert.AreEqual("Groceries", result);
    }

    [TestMethod]
    public async Task Match_MultipleMatches_SameLength_OldestRuleWins()
    {
        await using var db = CreateDb();
        var cat = await SeedCategory(db);
        var svc = new CategoryRuleService(db);
        var rules = new List<CategoryRule>
        {
            new() { Id = 2, Pattern = "ABC", CategoryId = cat.Id, Category = cat, UpdatedAt = DateTime.UtcNow },
            new() { Id = 1, Pattern = "ABC", CategoryId = cat.Id, Category = cat, UpdatedAt = DateTime.UtcNow }
        };

        var result = svc.Match("ABC Store", rules);

        Assert.AreEqual("Groceries", result);
    }

    [TestMethod]
    public async Task Match_NoMatch_ReturnsNull()
    {
        await using var db = CreateDb();
        var cat = await SeedCategory(db);
        var svc = new CategoryRuleService(db);
        var rules = new List<CategoryRule>
        {
            new() { Id = 1, Pattern = "Food Lion", CategoryId = cat.Id, Category = cat, UpdatedAt = DateTime.UtcNow }
        };

        var result = svc.Match("Walmart", rules);

        Assert.IsNull(result);
    }

    // ── EffectiveCategoryName ───────────────────────────────────────────────

    [TestMethod]
    public async Task EffectiveCategoryName_TransactionWithCategoryId_ReturnsStoredCategory()
    {
        await using var db = CreateDb();
        var cat = await SeedCategory(db);
        var svc = new CategoryRuleService(db);
        var txn = TestHelper.MakeTxn("t1", 2025, 1, 1, 100m);
        txn.CategoryId = cat.Id;
        txn.EffectiveCategory = cat;
        var rules = new List<CategoryRule>();

        var result = svc.EffectiveCategoryName(txn, rules);

        Assert.AreEqual("Groceries", result);
    }

    [TestMethod]
    public async Task EffectiveCategoryName_NoCategoryId_MatchesRules()
    {
        await using var db = CreateDb();
        var cat = await SeedCategory(db);
        var svc = new CategoryRuleService(db);
        var txn = TestHelper.MakeTxn("t1", 2025, 1, 1, 100m, name: "Food Lion");
        var rules = new List<CategoryRule>
        {
            new() { Id = 1, Pattern = "Food Lion", CategoryId = cat.Id, Category = cat, UpdatedAt = DateTime.UtcNow }
        };

        var result = svc.EffectiveCategoryName(txn, rules);

        Assert.AreEqual("Groceries", result);
    }

    [TestMethod]
    public async Task EffectiveCategoryName_NoMatch_BlankCategory_ReturnsUncategorized()
    {
        await using var db = CreateDb();
        var cat = await SeedCategory(db);
        var svc = new CategoryRuleService(db);
        var txn = TestHelper.MakeTxn("t1", 2025, 1, 1, 100m, name: "Walmart", category: "");
        var rules = new List<CategoryRule>();

        var result = svc.EffectiveCategoryName(txn, rules);

        Assert.AreEqual("(uncategorized)", result);
    }

    // ── Create / Delete ─────────────────────────────────────────────────────

    [TestMethod]
    public async Task Create_SavesRuleAndReprocessesUncategorized()
    {
        await using var db = CreateDb();
        var cat = await SeedCategory(db);
        var txn = TestHelper.MakeTxn("t1", 2025, 1, 1, 100m, name: "Food Lion");
        db.Transactions.Add(txn);
        await db.SaveChangesAsync();
        var svc = new CategoryRuleService(db);

        var rule = await svc.Create("Food Lion", cat.Id);

        Assert.IsTrue(rule.Id > 0);
        var fromDb = await db.Transactions.FindAsync("t1");
        Assert.AreEqual(cat.Id, fromDb!.CategoryId);
        Assert.AreEqual(rule.Id, fromDb.CategoryRuleId);
    }

    [TestMethod]
    public async Task Delete_ExistingId_UncategorizesTransactionsAssignedByRule()
    {
        await using var db = CreateDb();
        var cat = await SeedCategory(db);
        var svc = new CategoryRuleService(db);
        var rule = new CategoryRule { Pattern = "Food Lion", CategoryId = cat.Id, UpdatedAt = DateTime.UtcNow };
        db.CategoryRules.Add(rule);
        await db.SaveChangesAsync();

        var txn = TestHelper.MakeTxn("t1", 2025, 1, 1, 100m);
        txn.CategoryId = cat.Id;
        txn.CategoryRuleId = rule.Id;
        db.Transactions.Add(txn);
        await db.SaveChangesAsync();

        var result = await svc.Delete(rule.Id);

        Assert.IsTrue(result);
        var fromDb = await db.Transactions.FindAsync("t1");
        Assert.IsNull(fromDb!.CategoryId);
        Assert.IsNull(fromDb.CategoryRuleId);
    }

    // ── ReprocessUncategorized ──────────────────────────────────────────────

    [TestMethod]
    public async Task ReprocessUncategorized_AssignsMatchingRules()
    {
        await using var db = CreateDb();
        var cat = await SeedCategory(db);
        var svc = new CategoryRuleService(db);
        var rule = new CategoryRule { Pattern = "Food Lion", CategoryId = cat.Id, UpdatedAt = DateTime.UtcNow };
        db.CategoryRules.Add(rule);

        var txn = TestHelper.MakeTxn("t1", 2025, 1, 1, 100m, name: "Food Lion");
        db.Transactions.Add(txn);
        await db.SaveChangesAsync();

        await svc.ReprocessUncategorized();

        var fromDb = await db.Transactions.FindAsync("t1");
        Assert.AreEqual(cat.Id, fromDb!.CategoryId);
        Assert.AreEqual(rule.Id, fromDb.CategoryRuleId);
    }

    [TestMethod]
    public async Task ReprocessUncategorized_SkipsTransactionsWithCategoryId()
    {
        await using var db = CreateDb();
        var cat = await SeedCategory(db);
        var otherCat = new Category { Name = "Other", UpdatedAt = DateTime.UtcNow };
        db.Categories.Add(otherCat);
        var svc = new CategoryRuleService(db);
        var rule = new CategoryRule { Pattern = "Food Lion", CategoryId = cat.Id, UpdatedAt = DateTime.UtcNow };
        db.CategoryRules.Add(rule);

        var txn = TestHelper.MakeTxn("t1", 2025, 1, 1, 100m, name: "Food Lion");
        txn.CategoryId = otherCat.Id;
        db.Transactions.Add(txn);
        await db.SaveChangesAsync();

        await svc.ReprocessUncategorized();

        var fromDb = await db.Transactions.FindAsync("t1");
        Assert.AreEqual(otherCat.Id, fromDb!.CategoryId);
    }

    // ── SuggestPattern ──────────────────────────────────────────────────────

    [TestMethod]
    public void SuggestPattern_StripsTrailingHashDigits()
    {
        var result = CategoryRuleService.SuggestPattern("Food Lion #1234");
        Assert.AreEqual("Food Lion", result);
    }

    [TestMethod]
    public void SuggestPattern_StripsTrailingDigitRuns()
    {
        var result = CategoryRuleService.SuggestPattern("Walmart 12345");
        Assert.AreEqual("Walmart", result);
    }

    [TestMethod]
    public void SuggestPattern_LeavesCleanNamesUnchanged()
    {
        var result = CategoryRuleService.SuggestPattern("Harris Teeter");
        Assert.AreEqual("Harris Teeter", result);
    }
}
