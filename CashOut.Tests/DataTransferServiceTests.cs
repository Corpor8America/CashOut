using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace CashOut.Tests;

[TestClass]
public class DataTransferServiceTests
{
    private static readonly DateTime Timestamp = new(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    [TestMethod]
    public async Task ExportAsync_WithAllEntities_WritesManifestAndAllTables()
    {
        using var db = TestHelper.CreateInMemoryDb(nameof(ExportAsync_WithAllEntities_WritesManifestAndAllTables));
        Seed(db);

        var bytes = await new DataTransferService(db).ExportAsync();
        var (manifest, data) = ReadBackup(bytes);

        Assert.AreEqual(DataTransferService.BackupFormat, manifest.Format);
        Assert.AreEqual(1, manifest.SchemaVersion);
        Assert.AreEqual(1, manifest.Counts.Accounts);
        Assert.AreEqual(1, manifest.Counts.Transactions);
        Assert.AreEqual(2, manifest.Counts.CsvMappingProfiles);
        Assert.AreEqual(2, manifest.Counts.Categories);
        Assert.AreEqual(1, manifest.Counts.CategoryRules);

        Assert.AreEqual("CSV", data.Transactions[0].Source);
        Assert.AreEqual(5.25m, data.Transactions[0].Debit);
        Assert.IsNull(data.Transactions[0].Credit);

        var dataJson = ReadEntry(bytes, DataTransferService.DataEntry);
        Assert.IsFalse(dataJson.Contains("\"Amount\":"), "Computed Amount must not be exported.");
        Assert.IsFalse(dataJson.Contains("\"EffectiveCategory\":"), "Navigation properties must not be exported.");
    }

    [TestMethod]
    public async Task RoundTrip_PreservesIdsDatesCreditDebitProfilesAndCategoryLinks()
    {
        using var source = TestHelper.CreateInMemoryDb(nameof(RoundTrip_PreservesIdsDatesCreditDebitProfilesAndCategoryLinks));
        var accountId = Seed(source);
        var bytes = await new DataTransferService(source).ExportAsync();

        using var target = TestHelper.CreateInMemoryDb(nameof(RoundTrip_PreservesIdsDatesCreditDebitProfilesAndCategoryLinks) + "-target");
        var result = await new DataTransferService(target).ImportAsync(new MemoryStream(bytes));

        Assert.AreEqual(1, result.Accounts);
        Assert.AreEqual(1, result.Transactions);
        Assert.AreEqual(2, result.CsvMappingProfiles);
        Assert.AreEqual(2, result.Categories);
        Assert.AreEqual(1, result.CategoryRules);

        var account = await target.Accounts.SingleAsync();
        Assert.AreEqual(accountId, account.Id);
        Assert.AreEqual("Main account", account.Description);
        Assert.AreEqual(Timestamp, account.CreatedAt);

        var txn = await target.Transactions.SingleAsync();
        Assert.AreEqual("csv-1", txn.TransactionId);
        Assert.AreEqual(accountId.ToString(), txn.AccountId);
        Assert.AreEqual(new DateOnly(2025, 2, 3), txn.Date);
        Assert.AreEqual("Coffee", txn.Name);
        Assert.AreEqual("COFFEE SHOP", txn.RawName);
        Assert.AreEqual(5.25m, txn.Debit);
        Assert.IsNull(txn.Credit);
        Assert.AreEqual(-5.25m, txn.Amount);
        Assert.AreEqual(1, txn.CategoryId);
        Assert.AreEqual(1, txn.CategoryRuleId);
        Assert.AreEqual(Timestamp, txn.CreatedAt);
        Assert.AreEqual(Timestamp, txn.UpdatedAt);

        var profiles = await target.CsvMappingProfiles.OrderBy(p => p.Version).ToListAsync();
        Assert.AreEqual(2, profiles.Count);
        Assert.AreEqual(1, profiles[0].Version);
        Assert.AreEqual("Date", profiles[0].DateColumn);
        Assert.AreEqual(2, profiles[1].Version);
        Assert.AreEqual("Posting Date", profiles[1].DateColumn);
        Assert.AreEqual(Timestamp, profiles[1].UpdatedAt);

        var category = await target.Categories.SingleAsync(c => c.Id == 1);
        Assert.AreEqual("Food", category.Name);
        var rule = await target.CategoryRules.SingleAsync();
        Assert.AreEqual("COFFEE", rule.Pattern);
        Assert.AreEqual(1, rule.CategoryId);
        Assert.AreEqual(Timestamp, rule.CreatedAt);
    }

    [TestMethod]
    public async Task ImportAsync_WithExistingData_ReplacesAllRows()
    {
        using var source = TestHelper.CreateInMemoryDb(nameof(ImportAsync_WithExistingData_ReplacesAllRows));
        Seed(source);
        var bytes = await new DataTransferService(source).ExportAsync();

        using var target = TestHelper.CreateInMemoryDb(nameof(ImportAsync_WithExistingData_ReplacesAllRows) + "-target");
        target.Accounts.Add(new Account { Id = Guid.NewGuid(), Name = "Old account", CreatedAt = Timestamp });
        target.Categories.Add(new Category { Id = 99, Name = "Old category", CreatedAt = Timestamp, UpdatedAt = Timestamp });
        target.Transactions.Add(TestHelper.MakeTxn("old-1", 2024, 1, 1, -1m));
        await target.SaveChangesAsync();

        await new DataTransferService(target).ImportAsync(new MemoryStream(bytes));

        Assert.AreEqual("Main", (await target.Accounts.SingleAsync()).Name);
        Assert.AreEqual(2, await target.Categories.CountAsync());
        Assert.IsFalse(await target.Categories.AnyAsync(c => c.Id == 99), "Old category was not removed.");
        Assert.AreEqual(1, await target.Transactions.CountAsync());
        Assert.AreEqual("csv-1", (await target.Transactions.SingleAsync()).TransactionId);
    }

    [TestMethod]
    public async Task ImportAsync_WithEmptyBackup_ClearsDatabase()
    {
        using var empty = TestHelper.CreateInMemoryDb(nameof(ImportAsync_WithEmptyBackup_ClearsDatabase));
        var bytes = await new DataTransferService(empty).ExportAsync();

        using var target = TestHelper.CreateInMemoryDb(nameof(ImportAsync_WithEmptyBackup_ClearsDatabase) + "-target");
        Seed(target);

        var result = await new DataTransferService(target).ImportAsync(new MemoryStream(bytes));

        Assert.AreEqual(0, result.Accounts);
        Assert.AreEqual(0, await target.Accounts.CountAsync());
        Assert.AreEqual(0, await target.Transactions.CountAsync());
        Assert.AreEqual(0, await target.Categories.CountAsync());
        Assert.AreEqual(0, await target.CategoryRules.CountAsync());
        Assert.AreEqual(0, await target.CsvMappingProfiles.CountAsync());
    }

    [TestMethod]
    public async Task ImportAsync_WithInvalidZip_ThrowsAndLeavesDataUnchanged()
    {
        using var db = TestHelper.CreateInMemoryDb(nameof(ImportAsync_WithInvalidZip_ThrowsAndLeavesDataUnchanged));
        Seed(db);

        await AssertRejectedAsync(db, Encoding.UTF8.GetBytes("not a zip"));

        Assert.AreEqual(1, await db.Transactions.CountAsync());
        Assert.AreEqual(1, await db.Accounts.CountAsync());
    }

    [TestMethod]
    public async Task ImportAsync_WithWrongSchemaVersion_ThrowsAndLeavesDataUnchanged()
    {
        using var db = TestHelper.CreateInMemoryDb(nameof(ImportAsync_WithWrongSchemaVersion_ThrowsAndLeavesDataUnchanged));
        Seed(db);
        var (manifest, data) = ReadBackup(await new DataTransferService(db).ExportAsync());
        var bytes = MakeZip(manifest with { SchemaVersion = 2 }, data);

        await AssertRejectedAsync(db, bytes);

        Assert.AreEqual(1, await db.Transactions.CountAsync());
    }

    [TestMethod]
    public async Task ImportAsync_WithMalformedJson_ThrowsAndLeavesDataUnchanged()
    {
        using var db = TestHelper.CreateInMemoryDb(nameof(ImportAsync_WithMalformedJson_ThrowsAndLeavesDataUnchanged));
        Seed(db);
        var (manifest, _) = ReadBackup(await new DataTransferService(db).ExportAsync());

        var bytes = MakeZipRaw(manifest, "{ not json");
        await AssertRejectedAsync(db, bytes);

        Assert.AreEqual(1, await db.Transactions.CountAsync());
    }

    [TestMethod]
    public async Task ImportAsync_WithUnknownField_ThrowsAndLeavesDataUnchanged()
    {
        using var db = TestHelper.CreateInMemoryDb(nameof(ImportAsync_WithUnknownField_ThrowsAndLeavesDataUnchanged));
        Seed(db);
        var export = await new DataTransferService(db).ExportAsync();
        var manifest = ReadManifest(export);
        var data = JsonSerializer.Deserialize<BackupData>(ReadEntry(export, DataTransferService.DataEntry))!;
        var json = JsonSerializer.Serialize(data).Replace("\"Accounts\":", "\"Unexpected\":0,\"Accounts\":");
        var bytes = MakeZipRaw(manifest, json);

        await AssertRejectedAsync(db, bytes);

        Assert.AreEqual(1, await db.Transactions.CountAsync());
    }

    [TestMethod]
    public async Task ImportAsync_WithDuplicateCategoryId_ThrowsAndLeavesDataUnchanged()
    {
        using var db = TestHelper.CreateInMemoryDb(nameof(ImportAsync_WithDuplicateCategoryId_ThrowsAndLeavesDataUnchanged));
        Seed(db);
        var (manifest, data) = ReadBackup(await new DataTransferService(db).ExportAsync());
        data.Categories.Add(data.Categories[0] with { Name = "Another name" });
        var bytes = MakeZip(manifest with { Counts = manifest.Counts with { Categories = data.Categories.Count } }, data);

        await AssertRejectedAsync(db, bytes);

        Assert.AreEqual(2, await db.Categories.CountAsync());
    }

    [TestMethod]
    public async Task ImportAsync_WithBrokenCategoryRuleFk_ThrowsAndLeavesDataUnchanged()
    {
        using var db = TestHelper.CreateInMemoryDb(nameof(ImportAsync_WithBrokenCategoryRuleFk_ThrowsAndLeavesDataUnchanged));
        Seed(db);
        var (manifest, data) = ReadBackup(await new DataTransferService(db).ExportAsync());
        data.CategoryRules[0] = data.CategoryRules[0] with { CategoryId = 999 };
        var bytes = MakeZip(manifest, data);

        await AssertRejectedAsync(db, bytes);

        Assert.AreEqual(1, await db.CategoryRules.CountAsync());
        Assert.AreEqual(1, (await db.CategoryRules.SingleAsync()).CategoryId);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static async Task AssertRejectedAsync(AppDbContext db, byte[] backup)
    {
        try
        {
            await new DataTransferService(db).ImportAsync(new MemoryStream(backup));
            Assert.Fail("Expected the import to be rejected.");
        }
        catch (DataTransferException) { }
    }

    private static Guid Seed(AppDbContext db)
    {
        var accountId = Guid.NewGuid();
        db.Accounts.Add(new Account
        {
            Id = accountId, Name = "Main", Description = "Main account", CreatedAt = Timestamp
        });
        db.Categories.Add(new Category { Id = 1, Name = "Food", CreatedAt = Timestamp, UpdatedAt = Timestamp });
        db.Categories.Add(new Category { Id = 2, Name = "Income", CreatedAt = Timestamp, UpdatedAt = Timestamp });
        db.CategoryRules.Add(new CategoryRule
        {
            Id = 1, Pattern = "COFFEE", CategoryId = 1, CreatedAt = Timestamp, UpdatedAt = Timestamp
        });
        db.CsvMappingProfiles.Add(new CsvMappingProfile
        {
            Id = 1, AccountId = accountId.ToString(), Version = 1, DateColumn = "Date",
            DescriptionColumn = "Description", CreatedAt = Timestamp, UpdatedAt = Timestamp
        });
        db.CsvMappingProfiles.Add(new CsvMappingProfile
        {
            Id = 2, AccountId = accountId.ToString(), Version = 2, DateColumn = "Posting Date",
            DescriptionColumn = "Description", CreatedAt = Timestamp, UpdatedAt = Timestamp
        });
        db.Transactions.Add(new Transaction
        {
            TransactionId = "csv-1", AccountId = accountId.ToString(), Source = TransactionSource.CSV,
            Date = new DateOnly(2025, 2, 3), Name = "Coffee", RawName = "COFFEE SHOP",
            Credit = null, Debit = 5.25m, Category = "Food", CategoryId = 1, CategoryRuleId = 1,
            CreatedAt = Timestamp, UpdatedAt = Timestamp
        });
        db.SaveChanges();
        return accountId;
    }

    private static (BackupManifest Manifest, BackupData Data) ReadBackup(byte[] bytes)
    {
        var manifest = ReadManifest(bytes);
        var data = JsonSerializer.Deserialize<BackupData>(ReadEntry(bytes, DataTransferService.DataEntry))!;
        return (manifest, data);
    }

    private static BackupManifest ReadManifest(byte[] bytes) =>
        JsonSerializer.Deserialize<BackupManifest>(ReadEntry(bytes, DataTransferService.ManifestEntry))!;

    private static string ReadEntry(byte[] bytes, string name)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry(name)!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static byte[] MakeZip(BackupManifest manifest, BackupData data) =>
        MakeZipRaw(manifest, JsonSerializer.Serialize(data));

    private static byte[] MakeZipRaw(BackupManifest manifest, string dataJson)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifestEntry = zip.CreateEntry(DataTransferService.ManifestEntry);
            using (var writer = new StreamWriter(manifestEntry.Open(), new UTF8Encoding(false)))
                writer.Write(JsonSerializer.Serialize(manifest));

            var dataEntry = zip.CreateEntry(DataTransferService.DataEntry);
            using (var writer = new StreamWriter(dataEntry.Open(), new UTF8Encoding(false)))
                writer.Write(dataJson);
        }
        return stream.ToArray();
    }
}
