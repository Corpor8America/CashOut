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
    public async Task Import_SkipsCrossSourceDuplicates()
    {
        var db = BuildDb(nameof(Import_SkipsCrossSourceDuplicates));
        var svc = BuildSvc(db);
        var accountId = "test-acc";

        // Pre-populate an existing transaction
        db.Transactions.Add(new Transaction
        {
            AccountId = accountId,
            Date = new DateOnly(2026, 6, 1),
            Amount = 10.00m,
            NormalizedName = "TEST MERCHANT"
        });
        await db.SaveChangesAsync();

        // CSV content that should match the existing transaction
        // Normalized "Test Merchant" -> "TEST MERCHANT"
        var csv = "Date,Description,Amount\n2026-06-01,Test Merchant,10.00";
        var profile = new CsvMappingProfile { DateColumn = "Date", DescriptionColumn = "Description", AmountColumn = "Amount" };

        var result = await svc.Import(accountId, csv, profile);

        Assert.AreEqual(0, result.Imported);
        Assert.AreEqual(1, result.SkippedCrossSourceDuplicates);
    }

    [TestMethod]
    public async Task ScanForDuplicates_IdentifiesInternalDuplicates()
    {
        var db = BuildDb(nameof(ScanForDuplicates_IdentifiesInternalDuplicates));
        var svc = BuildSvc(db);
        var accountId = "test-acc";

        var csv = "Date,Description,Amount\n2026-06-01,Test Merchant,10.00";
        var profile = new CsvMappingProfile { DateColumn = "Date", DescriptionColumn = "Description", AmountColumn = "Amount" };

        // Import once to create the dedup key
        var first = await svc.Import(accountId, csv, profile);
        Assert.AreEqual(1, first.Imported);

        // Scan should flag it as internal duplicate
        var scan = await svc.ScanForDuplicates(accountId, csv, profile);

        Assert.AreEqual(1, scan.InternalDuplicateCount);
        Assert.AreEqual(0, scan.NewCount);
        Assert.AreEqual("InternalDuplicate", scan.Rows[0].DuplicateType);
    }

    [TestMethod]
    public async Task ScanForDuplicates_IdentifiesCrossSourceDuplicates()
    {
        var db = BuildDb(nameof(ScanForDuplicates_IdentifiesCrossSourceDuplicates));
        var svc = BuildSvc(db);
        var accountId = "test-acc";

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

        var scan = await svc.ScanForDuplicates(accountId, csv, profile);

        Assert.AreEqual(1, scan.CrossSourceDuplicateCount);
        Assert.AreEqual(0, scan.NewCount);
        Assert.AreEqual("CrossSourceDuplicate", scan.Rows[0].DuplicateType);
        Assert.IsNotNull(scan.Rows[0].MatchedTransaction);
    }

    [TestMethod]
    public async Task Import_ForceImportRows_OverridesDedup()
    {
        var db = BuildDb(nameof(Import_ForceImportRows_OverridesDedup));
        var svc = BuildSvc(db);
        var accountId = "test-acc";

        // Import once to create the dedup key
        var csv = "Date,Description,Amount\n2026-06-01,Test Merchant,10.00";
        var profile = new CsvMappingProfile { DateColumn = "Date", DescriptionColumn = "Description", AmountColumn = "Amount" };
        await svc.Import(accountId, csv, profile);

        // Force-import row 2 (the data row) despite the duplicate
        var forceRows = new HashSet<int> { 2 };
        var result = await svc.Import(accountId, csv, profile, forceRows);

        Assert.AreEqual(1, result.Imported);
        Assert.AreEqual(0, result.SkippedDuplicates);

        // Verify both transactions exist
        var count = await db.Transactions.CountAsync(t => t.AccountId == accountId);
        Assert.AreEqual(2, count);
    }
}