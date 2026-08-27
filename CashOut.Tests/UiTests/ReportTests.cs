using System.Net.Http.Json;
using CashOut.Tests.UiTests.Helpers;
using Microsoft.Playwright;

namespace CashOut.Tests.UiTests;

[TestClass]
[TestCategory("UI")]
public class ReportTests : UiTestBase
{
    private static async Task SeedReportDataAsync(HttpClient api)
    {
        var acctResp = await api.PostAsJsonAsync("api/accounts", new
        {
            Name = $"Report Account {Guid.NewGuid():N}",
            Description = "For report tests"
        });
        var acct = await acctResp.Content.ReadFromJsonAsync<CreateAccountResponse>();
        var accountId = acct!.Id;

        await api.PostAsJsonAsync($"api/csv-import/{accountId}/profile", new
        {
            DateColumn = "Date",
            DescriptionColumn = "Description",
            AmountColumn = "Amount",
            CategoryColumn = "Category",
            NegativeIsCredit = false
        });

        var csv = """
            Date,Description,Amount,Category
            2026/01/05,Coffee Shop,-4.50,Food
            2026/01/15,Paycheck,3000.00,Income
            2026/02/10,Gas Station,-45.00,Transport
            2026/02/20,Electric Bill,-120.00,Utilities
            2026/03/01,Internet,-60.00,Utilities
            2026/03/15,Paycheck,3000.00,Income
            """;

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(csv), "file", "report-seed.csv");
        await api.PostAsync($"api/csv-import/{accountId}/import", content);
    }

    private record CreateAccountResponse(Guid Id, string Name, string Description, DateTime CreatedAt);

    [TestInitialize]
    public async Task ReportTestInit()
    {
        await SeedReportDataAsync(Api);
    }

    [TestMethod]
    public async Task CashFlow_ShowsReportTitle()
    {
        await Page.GotoAsync($"{BaseUrl}/reports/cashflow");
        await WaitForBlazorContent();

        await Expect(Page.GetByText("Inflow vs Outflow").First).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task CashFlow_WithDatos_ShowsSummaryMetrics()
    {
        await Page.GotoAsync($"{BaseUrl}/reports/cashflow");
        await WaitForBlazorContent();

        await Expect(Page.GetByText("Total Income")).ToBeVisibleAsync();
        await Expect(Page.GetByText("Total Expenses")).ToBeVisibleAsync();
        await Expect(Page.GetByText("Total Net")).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task CashFlow_WithDatos_ShowsMonthTable()
    {
        await Page.GotoAsync($"{BaseUrl}/reports/cashflow");
        await WaitForBlazorContent();

        await Expect(Page.GetByText("Select a month to view transactions.")).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task CashFlow_ClickMonth_ShowsDrilldown()
    {
        await Page.GotoAsync($"{BaseUrl}/reports/cashflow");
        await WaitForBlazorContent();

        var monthRow = Page.Locator(".mud-table tbody tr").First;
        await monthRow.ClickAsync(new() { Force = true });

        await Expect(Page.GetByText("Select a month to view transactions.").First).ToBeHiddenAsync();
    }

    [TestMethod]
    public async Task Category_ShowsReportTitle()
    {
        await Page.GotoAsync($"{BaseUrl}/reports/category");
        await WaitForBlazorContent();

        await Expect(Page.GetByText("By Category").First).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task Category_WithDatos_ShowsIncomeAndExpenseSections()
    {
        await Page.GotoAsync($"{BaseUrl}/reports/category");
        await WaitForBlazorContent();

        await Expect(Page.GetByText("Total Income")).ToBeVisibleAsync();
        await Expect(Page.GetByText("Total Expenses")).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task Category_ShowsCategoryRows()
    {
        await Page.GotoAsync($"{BaseUrl}/reports/category");
        await WaitForBlazorContent();

        await Expect(Page.GetByText("Food")).ToBeVisibleAsync();
        await Expect(Page.GetByText("Transport")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Cell, new() { Name = "Income" })).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task Category_ClickCategory_ShowsDrilldown()
    {
        await Page.GotoAsync($"{BaseUrl}/reports/category");
        await WaitForBlazorContent();

        var categoryRow = Page.Locator(".mud-table tbody tr").Filter(new() { HasText = "Food" });
        await categoryRow.ClickAsync(new() { Force = true });

        await Expect(Page.GetByText("Coffee Shop").First).ToBeVisibleAsync();
    }
}
