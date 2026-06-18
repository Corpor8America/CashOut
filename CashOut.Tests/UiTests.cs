using Microsoft.Playwright;
using Microsoft.Playwright.MSTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CashOut.Tests;

[TestClass]
public class UiTests : PageTest
{
    [TestMethod]
    [TestCategory("UI")]
    public async Task LandingPage_ShowsLinkedAccountsHeader()
    {
        // Act: Navigate to the app (running in Docker)
        await Page.GotoAsync("http://localhost:8080/accounts");

        // Assert: Check for the header text specifically in the H4 heading
        var header = Page.GetByRole(AriaRole.Heading, new() { Name = "Linked Accounts" });
        await Expect(header).ToBeVisibleAsync();
    }

    [TestMethod]
    [TestCategory("UI")]
    public async Task VersionApi_IsAccessible()
    {
        await Page.GotoAsync("http://localhost:8080/api/version");
        var content = await Page.ContentAsync();
        Assert.IsTrue(content.Contains("version"), "API response should contain 'version'");
    }
}
