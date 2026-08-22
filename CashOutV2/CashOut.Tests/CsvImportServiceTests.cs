using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CashOut.Tests;

[TestClass]
public class CsvImportServiceTests
{
    private AppDbContext CreateDb([System.Runtime.CompilerServices.CallerMemberName] string name = "") =>
        TestHelper.CreateInMemoryDb(name);

    // ── Import: single Amount column ──────────────────────────────────────

    [TestMethod]
    public async Task Import_SingleAmountColumn_PositiveIsDebit()
    {
        await using var db = CreateDb();
        var svc = new CsvImportService(db);

        var accountId = "acct-1";
        var csv = "Date,Description,Amount\n" +
                  "2025-03-01,Coffee Shop,5.50\n";

        var profile = new CsvMappingProfile
        {
            DateColumn = "Date",
            DescriptionColumn = "Description",
            AmountColumn = "Amount"
        };

        var result = await svc.Import(accountId, csv, profile);

        Assert.AreEqual(1, result.Imported);
        var txn = db.Transactions.First();
        Assert.AreEqual(5.50m, txn.Debit);
        Assert.IsNull(txn.Credit);
        Assert.AreEqual(-5.50m, txn.Amount);
    }

    [TestMethod]
    public async Task Import_SingleAmountColumn_NegativeIsCredit()
    {
        await using var db = CreateDb();
        var svc = new CsvImportService(db);

        var accountId = "acct-1";
        var csv = "Date,Description,Amount\n" +
                  "2025-03-01,Paycheck,-1500.00\n";

        var profile = new CsvMappingProfile
        {
            DateColumn = "Date",
            DescriptionColumn = "Description",
            AmountColumn = "Amount"
        };

        var result = await svc.Import(accountId, csv, profile);

        Assert.AreEqual(1, result.Imported);
        var txn = db.Transactions.First();
        Assert.AreEqual(1500.00m, txn.Credit);
        Assert.IsNull(txn.Debit);
        Assert.AreEqual(1500.00m, txn.Amount);
    }

    // ── Import: split Credit/Debit columns ────────────────────────────────

    [TestMethod]
    public async Task Import_SplitColumns_CreditAndDebit()
    {
        await using var db = CreateDb();
        var svc = new CsvImportService(db);

        var accountId = "acct-1";
        var csv = "Date,Description,Credit,Debit\n" +
                  "2025-03-01,Paycheck,2000.00,\n" +
                  "2025-03-02,Rent,,1500.00\n";

        var profile = new CsvMappingProfile
        {
            DateColumn = "Date",
            DescriptionColumn = "Description",
            CreditColumn = "Credit",
            DebitColumn = "Debit"
        };

        var result = await svc.Import(accountId, csv, profile);

        Assert.AreEqual(2, result.Imported);
        var txns = db.Transactions.OrderBy(t => t.Date).ToList();

        Assert.AreEqual(2000m, txns[0].Credit);
        Assert.AreEqual(2000m, txns[0].Amount);

        Assert.AreEqual(1500m, txns[1].Debit);
        Assert.AreEqual(-1500m, txns[1].Amount);
    }

    // ── Import: day-level dedup ───────────────────────────────────────────

    [TestMethod]
    public async Task Import_DayAlreadyImported_SkipsAllRowsForThatDay()
    {
        await using var db = CreateDb();
        db.Transactions.Add(TestHelper.MakeTxn("p1", 2025, 3, 1, 5.00m));
        await db.SaveChangesAsync();
        var svc = new CsvImportService(db);

        var accountId = "acct-1";
        var csv = "Date,Description,Amount\n" +
                  "2025-03-01,Coffee Shop,5.50\n" +
                  "2025-03-01,Grocery Store,42.00\n" +
                  "2025-03-02,Rent,1500.00\n";

        var profile = new CsvMappingProfile
        {
            DateColumn = "Date",
            DescriptionColumn = "Description",
            AmountColumn = "Amount"
        };

        var result = await svc.Import(accountId, csv, profile);

        Assert.AreEqual(1, result.Imported);
        Assert.AreEqual(2, result.SkippedRows.Count);
        Assert.IsTrue(result.SkippedRows.All(r => r.Reason.Contains("already imported")));
        Assert.AreEqual(2, db.Transactions.Count());
        Assert.IsTrue(db.Transactions.Any(t => t.Name == "Rent"));
    }

    [TestMethod]
    public async Task Import_SameDayDifferentAccount_ImportsNormally()
    {
        await using var db = CreateDb();
        db.Transactions.Add(TestHelper.MakeTxn("p1", 2025, 3, 1, 5.00m, accountId: "acct-2"));
        await db.SaveChangesAsync();
        var svc = new CsvImportService(db);

        var accountId = "acct-1";
        var csv = "Date,Description,Amount\n" +
                  "2025-03-01,Coffee Shop,5.50\n";

        var profile = new CsvMappingProfile
        {
            DateColumn = "Date",
            DescriptionColumn = "Description",
            AmountColumn = "Amount"
        };

        var result = await svc.Import(accountId, csv, profile);

        Assert.AreEqual(1, result.Imported);
        Assert.AreEqual(0, result.SkippedRows.Count);
        Assert.AreEqual(2, db.Transactions.Count());
    }

    [TestMethod]
    public async Task Import_ReimportSameFile_AllDaysSkipped()
    {
        await using var db = CreateDb();
        var svc = new CsvImportService(db);

        var accountId = "acct-1";
        var csv = "Date,Description,Amount\n" +
                  "2025-03-01,Coffee Shop,5.50\n" +
                  "2025-03-02,Rent,1500.00\n";

        var profile = new CsvMappingProfile
        {
            DateColumn = "Date",
            DescriptionColumn = "Description",
            AmountColumn = "Amount"
        };

        var first = await svc.Import(accountId, csv, profile);
        Assert.AreEqual(2, first.Imported);

        var second = await svc.Import(accountId, csv, profile);

        Assert.AreEqual(0, second.Imported);
        Assert.AreEqual(2, second.SkippedRows.Count);
        Assert.AreEqual(2, db.Transactions.Count());
    }

    // ── Import: skip rows ─────────────────────────────────────────────────

    [TestMethod]
    public async Task Import_SkipsUnparseableDates()
    {
        await using var db = CreateDb();
        var svc = new CsvImportService(db);

        var accountId = "acct-1";
        var csv = "Date,Description,Amount\n" +
                  "not-a-date,Coffee,5.00\n" +
                  "2025-03-01,Tea,3.00\n";

        var profile = new CsvMappingProfile
        {
            DateColumn = "Date",
            DescriptionColumn = "Description",
            AmountColumn = "Amount"
        };

        var result = await svc.Import(accountId, csv, profile);

        Assert.AreEqual(1, result.Imported);
        Assert.AreEqual(1, result.SkippedRows.Count);
        Assert.IsTrue(result.SkippedRows[0].Reason.Contains("Date"));
    }

    [TestMethod]
    public async Task Import_SkipsZeroAmounts()
    {
        await using var db = CreateDb();
        var svc = new CsvImportService(db);

        var accountId = "acct-1";
        var csv = "Date,Description,Amount\n2025-03-01,Coffee,0.00\n";

        var profile = new CsvMappingProfile
        {
            DateColumn = "Date",
            DescriptionColumn = "Description",
            AmountColumn = "Amount"
        };

        var result = await svc.Import(accountId, csv, profile);

        Assert.AreEqual(0, result.Imported);
        Assert.AreEqual(1, result.SkippedRows.Count);
        Assert.IsTrue(result.SkippedRows[0].Reason.Contains("zero"));
    }

    [TestMethod]
    public async Task Import_SkipsBothCreditAndDebit()
    {
        await using var db = CreateDb();
        var svc = new CsvImportService(db);

        var accountId = "acct-1";
        var csv = "Date,Description,Credit,Debit\n2025-03-01,Coffee,10.00,10.00\n";

        var profile = new CsvMappingProfile
        {
            DateColumn = "Date",
            DescriptionColumn = "Description",
            CreditColumn = "Credit",
            DebitColumn = "Debit"
        };

        var result = await svc.Import(accountId, csv, profile);

        Assert.AreEqual(0, result.Imported);
        Assert.AreEqual(1, result.SkippedRows.Count);
        Assert.IsTrue(result.SkippedRows[0].Reason.Contains("Both"));
    }

    [TestMethod]
    public async Task Import_SkipsNeitherCreditNorDebit()
    {
        await using var db = CreateDb();
        var svc = new CsvImportService(db);

        var accountId = "acct-1";
        var csv = "Date,Description,Credit,Debit\n2025-03-01,Coffee,,\n";

        var profile = new CsvMappingProfile
        {
            DateColumn = "Date",
            DescriptionColumn = "Description",
            CreditColumn = "Credit",
            DebitColumn = "Debit"
        };

        var result = await svc.Import(accountId, csv, profile);

        Assert.AreEqual(0, result.Imported);
        Assert.AreEqual(1, result.SkippedRows.Count);
        Assert.IsTrue(result.SkippedRows[0].Reason.Contains("Neither"));
    }

    [TestMethod]
    public async Task Import_SkipsEmptyDescription()
    {
        await using var db = CreateDb();
        var svc = new CsvImportService(db);

        var accountId = "acct-1";
        var csv = "Date,Description,Amount\n2025-03-01,,5.00\n";

        var profile = new CsvMappingProfile
        {
            DateColumn = "Date",
            DescriptionColumn = "Description",
            AmountColumn = "Amount"
        };

        var result = await svc.Import(accountId, csv, profile);

        Assert.AreEqual(0, result.Imported);
        Assert.AreEqual(1, result.SkippedRows.Count);
        Assert.IsTrue(result.SkippedRows[0].Reason.Contains("empty"));
    }

    // ── Import: skip rows trimming ────────────────────────────────────────

    [TestMethod]
    public async Task Import_SkipTopRows()
    {
        await using var db = CreateDb();
        var svc = new CsvImportService(db);

        var accountId = "acct-1";
        var csv = "Skip this\nAlso skip\nDate,Description,Amount\n2025-03-01,Coffee,5.00\n";

        var profile = new CsvMappingProfile
        {
            DateColumn = "Date",
            DescriptionColumn = "Description",
            AmountColumn = "Amount",
            SkipRowsFromTop = 2
        };

        var result = await svc.Import(accountId, csv, profile);

        Assert.AreEqual(1, result.Imported);
    }

    [TestMethod]
    public async Task Import_SkipBottomRows()
    {
        await using var db = CreateDb();
        var svc = new CsvImportService(db);

        var accountId = "acct-1";
        var csv = "Date,Description,Amount\n2025-03-01,Coffee,5.00\nTotal,100.00\n";

        var profile = new CsvMappingProfile
        {
            DateColumn = "Date",
            DescriptionColumn = "Description",
            AmountColumn = "Amount",
            SkipRowsFromBottom = 1
        };

        var result = await svc.Import(accountId, csv, profile);

        Assert.AreEqual(1, result.Imported);
    }

    // ── Preview ───────────────────────────────────────────────────────────

    [TestMethod]
    public void Preview_ReturnsHeadersAndRows()
    {
        var svc = new CsvImportService(CreateDb());
        var csv = "Date,Description,Amount\n2025-03-01,Coffee,5.00\n2025-03-02,Tea,3.00\n";

        var preview = svc.Preview(csv);

        Assert.AreEqual(3, preview.Headers.Length);
        Assert.AreEqual("Date", preview.Headers[0]);
        Assert.AreEqual(2, preview.Rows.Length);
    }

    [TestMethod]
    public void Preview_ReturnsAllRows()
    {
        var svc = new CsvImportService(CreateDb());
        var rows = string.Join("\n",
            Enumerable.Range(1, 10).Select(i => $"2025-03-{i:D2},Item{i},1.00"));
        var csv = "Date,Description,Amount\n" + rows;

        var preview = svc.Preview(csv);

        Assert.AreEqual(10, preview.Rows.Length);
    }

    [TestMethod]
    public void Preview_RespectsSkipTopAndBottom()
    {
        var svc = new CsvImportService(CreateDb());
        var csv = "Header\nSkip\nDate,Description,Amount\n2025-03-01,Coffee,5.00\n2025-03-02,Tea,3.00\nSkip\n";

        var preview = svc.Preview(csv, skipTop: 2, skipBottom: 1);

        Assert.AreEqual("Date", preview.Headers[0]);
        Assert.AreEqual(2, preview.Rows.Length);
    }

    // ── ValidateProfile ───────────────────────────────────────────────────

    [TestMethod]
    public void ValidateProfile_Valid_ReturnsNull()
    {
        var svc = new CsvImportService(CreateDb());
        var profile = new CsvMappingProfile
        {
            DateColumn = "Date",
            DescriptionColumn = "Description",
            AmountColumn = "Amount"
        };
        var headers = new[] { "Date", "Description", "Amount", "Extra" };

        var missing = svc.ValidateProfile(profile, headers);

        Assert.IsNull(missing);
    }

    [TestMethod]
    public void ValidateProfile_MissingColumn_ReturnsMissing()
    {
        var svc = new CsvImportService(CreateDb());
        var profile = new CsvMappingProfile
        {
            DateColumn = "Date",
            DescriptionColumn = "Description",
            AmountColumn = "Amount"
        };
        var headers = new[] { "Date", "Description" };

        var missing = svc.ValidateProfile(profile, headers);

        Assert.IsNotNull(missing);
        Assert.IsTrue(missing.Contains("amount"));
    }

    // ── SaveProfile / GetCurrentProfile ───────────────────────────────────

    [TestMethod]
    public async Task SaveProfile_IncrementsVersion()
    {
        await using var db = CreateDb();
        var svc = new CsvImportService(db);

        var p1 = new CsvMappingProfile { DateColumn = "Date", DescriptionColumn = "Desc" };
        var saved1 = await svc.SaveProfile("acct-1", p1);
        Assert.AreEqual(1, saved1.Version);

        var p2 = new CsvMappingProfile { DateColumn = "Date", DescriptionColumn = "Desc" };
        var saved2 = await svc.SaveProfile("acct-1", p2);
        Assert.AreEqual(2, saved2.Version);
    }

    [TestMethod]
    public async Task GetCurrentProfile_ReturnsLatestVersion()
    {
        await using var db = CreateDb();
        var svc = new CsvImportService(db);

        await svc.SaveProfile("acct-1", new CsvMappingProfile { DateColumn = "V1" });
        await svc.SaveProfile("acct-1", new CsvMappingProfile { DateColumn = "V2" });

        var current = await svc.GetCurrentProfile("acct-1");

        Assert.IsNotNull(current);
        Assert.AreEqual("V2", current.DateColumn);
    }

    [TestMethod]
    public async Task GetCurrentProfile_ReturnsNullForUnknownAccount()
    {
        await using var db = CreateDb();
        var svc = new CsvImportService(db);

        var result = await svc.GetCurrentProfile("nonexistent");

        Assert.IsNull(result);
    }
}
