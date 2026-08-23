using CashOut.Tests.UiTests.Helpers;
using Microsoft.Playwright;

namespace CashOut.Tests.UiTests;

[TestClass]
[TestCategory("UI")]
public class CsvImportTests : UiTestBase
{
    private const string SampleCsv = """
        Date,Description,Amount,Category
        2026/01/05,Coffee Shop,-4.50,Food
        2026/01/06,Gas Station,-45.00,Transport
        2026/01/10,Paycheck,2500.00,Income
        """;

    [TestMethod]
    public async Task CsvImport_UploadStep_ShowsUploadZone()
    {
        var accountName = $"CSV Test {Guid.NewGuid():N}";
        var accountId = await CreateAccountViaApi(accountName);

        await Page.GotoAsync($"{BaseUrl}/csv-import/{accountId}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Expect(Page.GetByText("Step 1: Upload CSV")).ToBeVisibleAsync();
        await Expect(Page.GetByText("Drag & drop your CSV or PDF here")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Browse file" })).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task CsvImport_UploadCsv_ShowsMappingStep()
    {
        var accountName = $"CSV Map {Guid.NewGuid():N}";
        var accountId = await CreateAccountViaApi(accountName);

        await Page.GotoAsync($"{BaseUrl}/csv-import/{accountId}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await UploadCsvFile(SampleCsv);

        await Expect(Page.GetByText("Step 2: Configure & Map Columns")).ToBeVisibleAsync();
        await Expect(Page.GetByText("Date")).ToBeVisibleAsync();
        await Expect(Page.GetByText("Description")).ToBeVisibleAsync();
        await Expect(Page.GetByText("Amount")).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task CsvImport_MapAndImport_ShowsResult()
    {
        var accountName = $"CSV Full {Guid.NewGuid():N}";
        var accountId = await CreateAccountViaApi(accountName);

        await Page.GotoAsync($"{BaseUrl}/csv-import/{accountId}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await UploadCsvFile(SampleCsv);
        await Expect(Page.GetByText("Step 2: Configure & Map Columns")).ToBeVisibleAsync();

        await MapColumnsAndImport();
        await Expect(Page.GetByText("Rows imported")).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task CsvImport_ImportAndNavigateToTransactions_ShowsImportedRows()
    {
        var accountName = $"CSV Verify {Guid.NewGuid():N}";
        var accountId = await CreateAccountViaApi(accountName);

        await Page.GotoAsync($"{BaseUrl}/csv-import/{accountId}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await UploadCsvFile(SampleCsv);
        await Expect(Page.GetByText("Step 2: Configure & Map Columns")).ToBeVisibleAsync();

        await MapColumnsAndImport();

        await Page.GetByRole(AriaRole.Button, new() { Name = "View Transactions" }).ClickAsync();
        await Page.WaitForURLAsync("**/transactions");

        await Expect(Page.GetByText("Coffee Shop")).ToBeVisibleAsync();
        await Expect(Page.GetByText("Gas Station")).ToBeVisibleAsync();
        await Expect(Page.GetByText("Paycheck")).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task CsvImport_ImportAnotherFile_ResetsToUploadStep()
    {
        var accountName = $"CSV Reset {Guid.NewGuid():N}";
        var accountId = await CreateAccountViaApi(accountName);

        await Page.GotoAsync($"{BaseUrl}/csv-import/{accountId}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await UploadCsvFile(SampleCsv);
        await Expect(Page.GetByText("Step 2: Configure & Map Columns")).ToBeVisibleAsync();

        await MapColumnsAndImport();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Import Another File" }).ClickAsync();
        await Expect(Page.GetByText("Step 1: Upload CSV")).ToBeVisibleAsync();
    }
}
