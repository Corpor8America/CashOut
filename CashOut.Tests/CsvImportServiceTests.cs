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
}