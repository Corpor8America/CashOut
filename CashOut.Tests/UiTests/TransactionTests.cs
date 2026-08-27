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
        var acctResp = await api.PostAsJsonAsync("api/accounts", new
        {
            Name = $"Txn Account {Guid.NewGuid():N}",
            Description = "For transaction tests"
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
            2026/01/06,Gas Station,-45.00,Transport
            2026/01/10,Paycheck,2500.00,Income
            2026/02/15,Electric Bill,-120.00,Utilities
            """;

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(csv), "file", "seed.csv");
        await api.PostAsync($"api/csv-import/{accountId}/import", content);
    }

    private record CreateAccountResponse(Guid Id, string Name, string Description, DateTime CreatedAt);

    [TestInitialize]
    public async Task TransactionTestInit()
    {
        await SeedTransactionsAsync(Api);
    }

    [TestMethod]
    public async Task Transactions_EmptyYear_ShowsNoTransactions()
    {
        await Page.GotoAsync($"{BaseUrl}/transactions?year=1999");
        await WaitForBlazorContent();

        await Expect(Page.GetByText("No transactions found for this year/filter.")).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task Transactions_WithData_ShowsGroupedByMonth()
    {
        await Page.GotoAsync($"{BaseUrl}/transactions");
        await WaitForBlazorContent();

        await Expect(Page.GetByText("January")).ToBeVisibleAsync();
        await Expect(Page.GetByText("Showing")).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task Transactions_Search_FiltersByName()
    {
        await Page.GotoAsync($"{BaseUrl}/transactions");
        await WaitForBlazorContent();
        await Expect(Page.GetByText("Showing")).ToBeVisibleAsync();

        var searchInput = Page.GetByLabel("Search transactions...");
        await searchInput.FillAsync("Coffee");

        await Expect(Page.GetByText("Coffee Shop").First).ToBeVisibleAsync();
        await Expect(Page.GetByText("Gas Station").First).ToBeHiddenAsync();
    }

    [TestMethod]
    public async Task Transactions_SearchClear_ShowsAll()
    {
        await Page.GotoAsync($"{BaseUrl}/transactions");
        await WaitForBlazorContent();
        await Expect(Page.GetByText("Showing")).ToBeVisibleAsync();

        var searchInput = Page.GetByLabel("Search transactions...");
        await searchInput.FillAsync("Coffee");
        await Expect(Page.GetByText("Gas Station").First).ToBeHiddenAsync();

        await searchInput.ClearAsync();
        await Expect(Page.GetByText("Gas Station").First).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task Transactions_AccountFilter_ShowsOnlyFilteredAccount()
    {
        var acct1 = await CreateAccountViaApi($"Filter A {Guid.NewGuid():N}");
        var acct2 = await CreateAccountViaApi($"Filter B {Guid.NewGuid():N}");

        var csv1 = "Date,Description,Amount\n2026/03/01,Acct1 Item,-10.00";
        var csv2 = "Date,Description,Amount\n2026/03/02,Acct2 Item,-20.00";

        await ImportCsvViaApi(acct1, csv1, "a.csv");
        await ImportCsvViaApi(acct2, csv2, "b.csv");

        await Page.GotoAsync($"{BaseUrl}/transactions?accountId={acct1}");
        await WaitForBlazorContent();
        await Expect(Page.GetByText("Showing")).ToBeVisibleAsync();

        await Expect(Page.GetByText("Acct1 Item")).ToBeVisibleAsync();
        await Expect(Page.GetByText("Acct2 Item")).ToBeHiddenAsync();
    }
}
