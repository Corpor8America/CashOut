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
        await WaitForBlazorContent();

        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Accounts" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Transactions" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Inflow vs Outflow" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "By Category" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "By Tracked Category" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Category Rules" })).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task Sidebar_NavigateToTransactions()
    {
        await Page.GotoAsync($"{BaseUrl}/accounts");
        await WaitForBlazorContent();

        await Page.GetByRole(AriaRole.Link, new() { Name = "Transactions" }).ClickAsync();
        await Page.WaitForURLAsync("**/transactions");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Transactions" })).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task Sidebar_NavigateToCashFlowReport()
    {
        await Page.GotoAsync($"{BaseUrl}/accounts");
        await WaitForBlazorContent();

        await Page.GetByRole(AriaRole.Link, new() { Name = "Inflow vs Outflow" }).ClickAsync();
        await Page.WaitForURLAsync("**/reports/cashflow");
        await Expect(Page.GetByText("Inflow vs Outflow").First).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task Sidebar_NavigateToCategoryReport()
    {
        await Page.GotoAsync($"{BaseUrl}/accounts");
        await WaitForBlazorContent();

        await Page.GetByRole(AriaRole.Link, new() { Name = "By Category" }).ClickAsync();
        await Page.WaitForURLAsync("**/reports/category");
        await Expect(Page.GetByText("By Category").First).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task Sidebar_NavigateToEffectiveCategoryReport()
    {
        await Page.GotoAsync($"{BaseUrl}/accounts");
        await WaitForBlazorContent();

        await Page.GetByRole(AriaRole.Link, new() { Name = "By Tracked Category" }).ClickAsync();
        await Page.WaitForURLAsync("**/reports/effective-category");
        await Expect(Page.GetByText("By Tracked Category").First).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task Sidebar_NavigateToCategoryRules()
    {
        await Page.GotoAsync($"{BaseUrl}/accounts");
        await WaitForBlazorContent();

        await Page.GetByRole(AriaRole.Link, new() { Name = "Category Rules" }).ClickAsync();
        await Page.WaitForURLAsync("**/category-rules");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Category Rules" })).ToBeVisibleAsync();
    }
}
