using CashOut.Tests.UiTests.Helpers;
using Microsoft.Playwright;

namespace CashOut.Tests.UiTests;

[TestClass]
[TestCategory("UI")]
public class AccountTests : UiTestBase
{
    [TestMethod]
    public async Task Accounts_EmptyState_ShowsNoAccountsMessage()
    {
        await Page.GotoAsync($"{BaseUrl}/accounts");
        await WaitForBlazorContent();

        await Expect(Page.GetByText("No accounts yet. Create one to import CSV transactions.")).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task Accounts_CreateAccount_ShowsInList()
    {
        var accountName = $"Test Account {Guid.NewGuid():N}";

        await Page.GotoAsync($"{BaseUrl}/accounts");
        await WaitForBlazorContent();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Create Account" }).ClickAsync();
        await Page.GetByLabel("Account Name").FillAsync(accountName);
        await Page.GetByLabel("Description (optional)").FillAsync("Test description");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await Expect(Page.GetByText("Account created.")).ToBeVisibleAsync();
        await Expect(Page.GetByText(accountName)).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task Accounts_CreateAccount_CancelHideForm()
    {
        await Page.GotoAsync($"{BaseUrl}/accounts");
        await WaitForBlazorContent();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Create Account" }).ClickAsync();
        await Expect(Page.GetByLabel("Account Name")).ToBeVisibleAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await Expect(Page.GetByLabel("Account Name")).ToBeHiddenAsync();
    }

    [TestMethod]
    public async Task Accounts_DeleteAccount_RemovesFromList()
    {
        var accountName = $"Delete Me {Guid.NewGuid():N}";
        await CreateAccountViaApi(accountName);

        await Page.GotoAsync($"{BaseUrl}/accounts");
        await WaitForBlazorContent();
        await Expect(Page.GetByText(accountName)).ToBeVisibleAsync();

        var row = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = accountName });
        await row.GetByRole(AriaRole.Button).Last.ClickAsync();

        await Expect(Page.GetByText($"Deleted {accountName}.")).ToBeVisibleAsync();
        var remainingRows = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = accountName });
        await Expect(remainingRows).ToHaveCountAsync(0);
    }

    [TestMethod]
    public async Task Accounts_ImportCsvButton_NavigatesToImportPage()
    {
        var accountName = $"Import Test {Guid.NewGuid():N}";
        var accountId = await CreateAccountViaApi(accountName);

        await Page.GotoAsync($"{BaseUrl}/accounts");
        await WaitForBlazorContent();

        var row = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = accountName });
        await row.GetByRole(AriaRole.Link, new() { Name = "Import CSV" }).ClickAsync();

        await Page.WaitForURLAsync($"**/csv-import/{accountId}");
        await Expect(Page.GetByText("CSV Import")).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task Accounts_AccountLink_NavigatesToTransactions()
    {
        var accountName = $"Txn Link {Guid.NewGuid():N}";
        var accountId = await CreateAccountViaApi(accountName);

        await Page.GotoAsync($"{BaseUrl}/accounts");
        await WaitForBlazorContent();

        await Page.GetByRole(AriaRole.Link, new() { Name = accountName }).ClickAsync();
        await Page.WaitForURLAsync($"**/transactions?accountId={accountId}");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Transactions" })).ToBeVisibleAsync();
    }
}
