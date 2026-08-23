using CashOut.Tests.UiTests.Helpers;
using Microsoft.Playwright;

namespace CashOut.Tests.UiTests;

[TestClass]
[TestCategory("UI")]
public class NavigationTests : UiTestBase
{
    [TestMethod]
    public async Task Index_RedirectsToAccounts()
    {
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForURLAsync("**/accounts");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Accounts" })).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task Sidebar_DisplaysAllNavLinks()
    {
        await Page.GotoAsync($"{BaseUrl}/accounts");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Accounts" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Transactions" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Inflow vs Outflow" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "By Category" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Settings" })).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task Sidebar_NavigateToTransactions()
    {
        await Page.GotoAsync($"{BaseUrl}/accounts");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Page.GetByRole(AriaRole.Link, new() { Name = "Transactions" }).ClickAsync();
        await Page.WaitForURLAsync("**/transactions");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Transactions" })).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task Sidebar_NavigateToCashFlowReport()
    {
        await Page.GotoAsync($"{BaseUrl}/accounts");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Page.GetByRole(AriaRole.Link, new() { Name = "Inflow vs Outflow" }).ClickAsync();
        await Page.WaitForURLAsync("**/reports/cashflow");
        await Expect(Page.GetByText("Inflow vs Outflow")).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task Sidebar_NavigateToCategoryReport()
    {
        await Page.GotoAsync($"{BaseUrl}/accounts");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Page.GetByRole(AriaRole.Link, new() { Name = "By Category" }).ClickAsync();
        await Page.WaitForURLAsync("**/reports/category");
        await Expect(Page.GetByText("By Category")).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task Sidebar_NavigateToSettings()
    {
        await Page.GotoAsync($"{BaseUrl}/accounts");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Page.GetByRole(AriaRole.Link, new() { Name = "Settings" }).ClickAsync();
        await Page.WaitForURLAsync("**/settings");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Settings" })).ToBeVisibleAsync();
    }
}
