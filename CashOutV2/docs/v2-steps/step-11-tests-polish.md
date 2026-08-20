# Step 11 — Tests & Polish

**Goal:** Create test project with MSTest + in-memory EF Core. Write tests for ReportService, CsvImportService, and TransactionService. Verify the full build and test suite pass.

**Prerequisites:** Steps 01–10 complete.

---

## 11.1 Test Project Setup

**File:** `CashOut.Tests/CashOut.Tests.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.9.0" />
    <PackageReference Include="MSTest.TestAdapter" Version="3.2.0" />
    <PackageReference Include="MSTest.TestFramework" Version="3.2.0" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="9.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\CashOut\CashOut.csproj" />
  </ItemGroup>
</Project>
```

## 11.2 In-Memory DbContext Helper

**File:** `CashOut.Tests/TestHelper.cs`

```csharp
using Microsoft.EntityFrameworkCore;

namespace CashOut.Tests;

public static class TestHelper
{
    public static AppDbContext CreateInMemoryDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(options);
    }
}
```

## 11.3 ReportService Tests

**File:** `CashOut.Tests/ReportServiceTests.cs`

```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CashOut.Tests;

[TestClass]
public class ReportServiceTests
{
    private AppDbContext CreateDb([System.Runtime.CompilerServices.CallerMemberName] string name = "") =>
        TestHelper.CreateInMemoryDb(name);

    private static Transaction MakeTxn(
        DateOnly date, decimal amount, string category = "Food",
        string accountId = "acct1", TransactionSource source = TransactionSource.Plaid)
    {
        var (credit, debit, normalized) = Transaction.NormalizeSingleAmount(amount);
        return new Transaction
        {
            TransactionId = Guid.NewGuid().ToString(),
            AccountId = accountId,
            Date = date,
            Name = $"Txn {amount}",
            RawName = $"Txn {amount}",
            Credit = credit,
            Debit = debit,
            Amount = normalized,
            Category = category,
            Source = source,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    [TestMethod]
    public async Task GetMonthly_GroupsByMonth_AndSumsCorrectly()
    {
        await using var db = CreateDb();
        var year = DateTime.Now.Year;

        db.Transactions.AddRange(
            MakeTxn(new DateOnly(year, 1, 5), -100),
            MakeTxn(new DateOnly(year, 1, 15), 200),
            MakeTxn(new DateOnly(year, 2, 10), -300)
        );
        await db.SaveChangesAsync();

        var settings = new SettingsService(db, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        var svc = new ReportService(db, settings);

        var rows = await svc.GetMonthly(year);

        Assert.AreEqual(2, rows.Count);
        Assert.AreEqual(200m, rows[0].Total);
        Assert.AreEqual(1, rows[0].Count);
        Assert.AreEqual(-300m, rows[1].Total);
        Assert.AreEqual(1, rows[1].Count);
    }

    [TestMethod]
    public async Task GetByCategory_GroupsByCategory_AndSortsDescending()
    {
        await using var db = CreateDb();
        var year = DateTime.Now.Year;

        db.Transactions.AddRange(
            MakeTxn(new DateOnly(year, 3, 1), 50, "Food"),
            MakeTxn(new DateOnly(year, 3, 5), 30, "Food"),
            MakeTxn(new DateOnly(year, 3, 10), 100, "Transport")
        );
        await db.SaveChangesAsync();

        var settings = new SettingsService(db, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        var svc = new ReportService(db, settings);

        var result = await svc.GetByCategory(year);

        Assert.AreEqual(2, result.Categories.Count);
        Assert.AreEqual("Transport", result.Categories[0].Category);
        Assert.AreEqual(100m, result.Categories[0].Total);
        Assert.AreEqual("Food", result.Categories[1].Category);
        Assert.AreEqual(80m, result.Categories[1].Total);
    }

    [TestMethod]
    public async Task GetCashFlow_SeparatesIncomeAndExpenses()
    {
        await using var db = CreateDb();
        var year = DateTime.Now.Year;

        db.Transactions.AddRange(
            MakeTxn(new DateOnly(year, 1, 5), -1000),
            MakeTxn(new DateOnly(year, 1, 10), 200),
            MakeTxn(new DateOnly(year, 1, 15), 150)
        );
        await db.SaveChangesAsync();

        var settings = new SettingsService(db, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        var svc = new ReportService(db, settings);

        var result = await svc.GetCashFlow(year);

        Assert.AreEqual(1000m, result.TotalIncome);
        Assert.AreEqual(350m, result.TotalExpenses);
        Assert.AreEqual(650m, result.NetCashFlow);
    }

    [TestMethod]
    public async Task GetMonthly_FiltersByYear()
    {
        await using var db = CreateDb();

        db.Transactions.AddRange(
            MakeTxn(new DateOnly(2024, 1, 5), -100),
            MakeTxn(new DateOnly(2025, 1, 15), 200)
        );
        await db.SaveChangesAsync();

        var settings = new SettingsService(db, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        var svc = new ReportService(db, settings);

        var rows = await svc.GetMonthly(2025);

        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(200m, rows[0].Total);
    }
}
```

## 11.4 CsvImportService Tests

**File:** `CashOut.Tests/CsvImportServiceTests.cs`

```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CashOut.Tests;

[TestClass]
public class CsvImportServiceTests
{
    private AppDbContext CreateDb([System.Runtime.CompilerServices.CallerMemberName] string name = "") =>
        TestHelper.CreateInMemoryDb(name);

    [TestMethod]
    public async Task Import_SingleAmountColumn_PositiveIsDebit()
    {
        await using var db = CreateDb();
        var svc = new CsvImportService(db);

        var accountId = Guid.NewGuid().ToString();
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
        Assert.AreEqual(5.50m, txn.Amount);
    }

    [TestMethod]
    public async Task Import_SingleAmountColumn_NegativeIsCredit()
    {
        await using var db = CreateDb();
        var svc = new CsvImportService(db);

        var accountId = Guid.NewGuid().ToString();
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
        Assert.AreEqual(-1500.00m, txn.Amount);
    }

    [TestMethod]
    public async Task Import_SplitColumns_CreditAndDebit()
    {
        await using var db = CreateDb();
        var svc = new CsvImportService(db);

        var accountId = Guid.NewGuid().ToString();
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
        Assert.AreEqual(-2000m, txns[0].Amount);

        Assert.AreEqual(1500m, txns[1].Debit);
        Assert.AreEqual(1500m, txns[1].Amount);
    }

    [TestMethod]
    public async Task Import_SkipsDuplicateTransactions()
    {
        await using var db = CreateDb();
        var svc = new CsvImportService(db);

        var accountId = Guid.NewGuid().ToString();
        var profile = new CsvMappingProfile
        {
            DateColumn = "Date",
            DescriptionColumn = "Description",
            AmountColumn = "Amount"
        };

        var csv1 = "Date,Description,Amount\n2025-03-01,Coffee,4.50\n";
        await svc.Import(accountId, csv1, profile);

        var csv2 = "Date,Description,Amount\n2025-03-01,Coffee,4.50\n";
        var result = await svc.Import(accountId, csv2, profile);

        Assert.AreEqual(0, result.Imported);
        Assert.AreEqual(1, result.SkippedAlreadyPresent);
    }

    [TestMethod]
    public async Task Import_SkipsUnparseableDates()
    {
        await using var db = CreateDb();
        var svc = new CsvImportService(db);

        var accountId = Guid.NewGuid().ToString();
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
    }

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
}
```

## 11.5 Full solution file

**File:** `CashOut.sln` (update or create at repo root)

```text
Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
VisualStudioVersion = 17.0.31903.59
MinimumVisualStudioVersion = 10.0.40219.1
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "CashOut", "CashOut\CashOut.csproj", "{GUID-HERE}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "CashOut.Tests", "CashOut.Tests\CashOut.Tests.csproj", "{GUID-HERE}"
EndProject
Global
  GlobalSection(SolutionConfigurationPlatforms) = preSolution
    Debug|Any CPU = Debug|Any CPU
    Release|Any CPU = Release|Any CPU
  EndGlobalSection
  GlobalSection(ProjectConfigurationPlatforms) = postSolution
    {GUID-HERE}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
    {GUID-HERE}.Debug|Any CPU.Build.0 = Debug|Any CPU
    {GUID-HERE}.Release|Any CPU.ActiveCfg = Release|Any CPU
    {GUID-HERE}.Release|Any CPU.Build.0 = Release|Any CPU
  EndGlobalSection
EndGlobal
```

## 11.6 Verify everything

```bash
dotnet build CashOut/CashOut.csproj
dotnet build CashOut.Tests/CashOut.Tests.csproj
dotnet test CashOut.Tests/CashOut.Tests.csproj --filter "TestCategory!=UI"
```

---

## Verification

1. Test project compiles
2. All unit tests pass (ReportService grouping, CSV import amount normalization, dedup, skip handling)
3. No references to `NormalizedName`, `AliasId`, `RawBusinessId`, `MerchantNormalizationService` in any test
4. Full app build succeeds
5. `dotnet test` passes
