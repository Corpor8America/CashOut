using System.Net.Http.Json;
using CashOut.Tests.UiTests.Helpers;
using Microsoft.Playwright;

namespace CashOut.Tests.UiTests;

[TestClass]
[TestCategory("UI")]
public class TransactionTests : UiTestBase
{
    private static async Task SeedTransactionsAsync(HttpClient api)
    {
        var accountId = Guid.NewGuid();
        await api.PostAsJsonAsync("api/accounts", new
        {
            Name = $"Txn Account {accountId:N}",
            Description = "For transaction tests"
        });

        var csv = """
            Date,Description,Amount,Category
            2026/01/05,Coffee Shop,-4.50,Food
            2026/01/06,Gas Station,-45.00,Transport
            2026/01/10,Paycheck,2500.00,Income
            2026/02/15,Electric Bill,-120.00,Utilities
            """;

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(csv), "file", "seed.csv");
        await api.PostAsync($"api/csv-import/{accountId}/import", content);
    }

    [TestInitialize]
    public async Task TransactionTestInit()
    {
        await SeedTransactionsAsync(Api);
    }

    [TestMethod]
    public async Task Transactions_EmptyYear_ShowsNoTransactions()
    {
        await Page.GotoAsync($"{BaseUrl}/transactions?year=1999");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Expect(Page.GetByText("No transactions found for this year/filter.")).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task Transactions_WithData_ShowsGroupedByMonth()
    {
        await Page.GotoAsync($"{BaseUrl}/transactions");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Expect(Page.GetByText("January")).ToBeVisibleAsync();
        await Expect(Page.GetByText("Showing")).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task Transactions_Search_FiltersByName()
    {
        await Page.GotoAsync($"{BaseUrl}/transactions");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.WaitForSelectorAsync(".mud-table");

        var searchInput = Page.GetByLabel("Search transactions...");
        await searchInput.FillAsync("Coffee");

        await Expect(Page.GetByText("Coffee Shop")).ToBeVisibleAsync();
        await Expect(Page.GetByText("Gas Station")).ToBeHiddenAsync();
    }

    [TestMethod]
    public async Task Transactions_SearchClear_ShowsAll()
    {
        await Page.GotoAsync($"{BaseUrl}/transactions");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.WaitForSelectorAsync(".mud-table");

        var searchInput = Page.GetByLabel("Search transactions...");
        await searchInput.FillAsync("Coffee");
        await Expect(Page.GetByText("Gas Station")).ToBeHiddenAsync();

        await searchInput.ClearAsync();
        await Expect(Page.GetByText("Gas Station")).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task Transactions_AccountFilter_ShowsOnlyFilteredAccount()
    {
        var acct1 = await CreateAccountViaApi($"Filter A {Guid.NewGuid():N}");
        var acct2 = await CreateAccountViaApi($"Filter B {Guid.NewGuid():N}");

        var csv1 = "Date,Description,Amount\n2026/03/01,Acct1 Item,-10.00";
        var csv2 = "Date,Description,Amount\n2026/03/02,Acct2 Item,-20.00";

        using var c1 = new MultipartFormDataContent();
        c1.Add(new StringContent(csv1), "file", "a.csv");
        await Api.PostAsync($"api/csv-import/{acct1}/import", c1);

        using var c2 = new MultipartFormDataContent();
        c2.Add(new StringContent(csv2), "file", "b.csv");
        await Api.PostAsync($"api/csv-import/{acct2}/import", c2);

        await Page.GotoAsync($"{BaseUrl}/transactions?accountId={acct1}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.WaitForSelectorAsync(".mud-table");

        await Expect(Page.GetByText("Acct1 Item")).ToBeVisibleAsync();
        await Expect(Page.GetByText("Acct2 Item")).ToBeHiddenAsync();
    }
}
