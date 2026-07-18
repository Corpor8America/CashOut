using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CashOut.Tests;

[TestClass]
public class CsvImportServiceTests
{
    private static AppDbContext BuildDb(string dbName)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(opts);
    }

    private static CsvImportService BuildSvc(AppDbContext db) =>
        new(db, new MerchantNormalizationService(db));

    [TestMethod]
    public async Task Import_NewRows_InsertsAll()
    {
        var db = BuildDb(nameof(Import_NewRows_InsertsAll));
        var svc = BuildSvc(db);
        var accountId = "test-acc";

        var csv = "Date,Description,Amount\n2026-06-01,Test Merchant,10.00\n2026-06-02,Another Store,25.50";
        var profile = new CsvMappingProfile { DateColumn = "Date", DescriptionColumn = "Description", AmountColumn = "Amount" };

        var result = await svc.Import(accountId, csv, profile);

        Assert.AreEqual(2, result.Imported);
        Assert.AreEqual(0, result.SkippedAlreadyPresent);
        Assert.AreEqual(0, result.SkippedRows.Count);

        var count = await db.Transactions.CountAsync(t => t.AccountId == accountId);
        Assert.AreEqual(2, count);
    }

    [TestMethod]
    public async Task Import_ExistingRows_SkipsAlreadyPresent()
    {
        var db = BuildDb(nameof(Import_ExistingRows_SkipsAlreadyPresent));
        var svc = BuildSvc(db);
        var accountId = "test-acc";

        // Pre-populate with matching transaction (same date, amount, normalizedName)
        db.Transactions.Add(new Transaction
        {
            AccountId = accountId,
            Date = new DateOnly(2026, 6, 1),
            Amount = 10.00m,
            NormalizedName = "TEST MERCHANT"
        });
        await db.SaveChangesAsync();

        var csv = "Date,Description,Amount\n2026-06-01,Test Merchant,10.00";
        var profile = new CsvMappingProfile { DateColumn = "Date", DescriptionColumn = "Description", AmountColumn = "Amount" };

        var result = await svc.Import(accountId, csv, profile);

        Assert.AreEqual(0, result.Imported);
        Assert.AreEqual(1, result.SkippedAlreadyPresent);
        Assert.AreEqual(0, result.SkippedRows.Count);

        // Verify no new transactions were created
        var count = await db.Transactions.CountAsync(t => t.AccountId == accountId);
        Assert.AreEqual(1, count);
    }

    [TestMethod]
    public async Task Import_PartialOverlap_InsertsOnlyNew()
    {
        var db = BuildDb(nameof(Import_PartialOverlap_InsertsOnlyNew));
        var svc = BuildSvc(db);
        var accountId = "test-acc";

        // Pre-populate with one transaction
        db.Transactions.Add(new Transaction
        {
            AccountId = accountId,
            Date = new DateOnly(2026, 6, 1),
            Amount = 10.00m,
            NormalizedName = "TEST MERCHANT"
        });
        await db.SaveChangesAsync();

        // CSV has the existing row + a new one
        var csv = "Date,Description,Amount\n2026-06-01,Test Merchant,10.00\n2026-06-02,New Shop,5.00";
        var profile = new CsvMappingProfile { DateColumn = "Date", DescriptionColumn = "Description", AmountColumn = "Amount" };

        var result = await svc.Import(accountId, csv, profile);

        Assert.AreEqual(1, result.Imported);
        Assert.AreEqual(1, result.SkippedAlreadyPresent);

        var count = await db.Transactions.CountAsync(t => t.AccountId == accountId);
        Assert.AreEqual(2, count);
    }

    [TestMethod]
    public async Task Import_DuplicateRowsInSameCsv_BothInserted()
    {
        var db = BuildDb(nameof(Import_DuplicateRowsInSameCsv_BothInserted));
        var svc = BuildSvc(db);
        var accountId = "test-acc";

        // Two identical rows in the same CSV
        var csv = "Date,Description,Amount\n2026-06-01,Test Merchant,10.00\n2026-06-01,Test Merchant,10.00";
        var profile = new CsvMappingProfile { DateColumn = "Date", DescriptionColumn = "Description", AmountColumn = "Amount" };

        var result = await svc.Import(accountId, csv, profile);

        // Both rows should be inserted — no intra-batch dedup
        Assert.AreEqual(2, result.Imported);
        Assert.AreEqual(0, result.SkippedAlreadyPresent);

        var count = await db.Transactions.CountAsync(t => t.AccountId == accountId);
        Assert.AreEqual(2, count);
    }
}
