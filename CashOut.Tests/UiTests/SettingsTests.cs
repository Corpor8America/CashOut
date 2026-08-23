using CashOut.Tests.UiTests.Helpers;
using Microsoft.Playwright;

namespace CashOut.Tests.UiTests;

[TestClass]
[TestCategory("UI")]
public class SettingsTests : UiTestBase
{
    [TestMethod]
    public async Task Settings_ShowsVersion()
    {
        await Page.GotoAsync($"{BaseUrl}/settings");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Expect(Page.GetByText("Version")).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task Settings_ShowsCleanupSection()
    {
        await Page.GotoAsync($"{BaseUrl}/settings");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Expect(Page.GetByText("Data Cleanup")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Find & Remove Orphans" })).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task Settings_CleanupFlow_ShowsConfirmation()
    {
        await Page.GotoAsync($"{BaseUrl}/settings");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Find & Remove Orphans" }).ClickAsync();

        await Expect(Page.GetByText("This will permanently delete orphaned data. Are you sure?")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Yes, clean up" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Cancel" })).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task Settings_CleanupCancel_ReturnsToInitial()
    {
        await Page.GotoAsync($"{BaseUrl}/settings");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Find & Remove Orphans" }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();

        await Expect(Page.GetByText("This will permanently delete orphaned data. Are you sure?")).ToBeHiddenAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Find & Remove Orphans" })).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task Settings_CleanupConfirm_ShowsResult()
    {
        await Page.GotoAsync($"{BaseUrl}/settings");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Find & Remove Orphans" }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Yes, clean up" }).ClickAsync();

        await Expect(Page.GetByText("Removed")).ToBeVisibleAsync();
        await Expect(Page.GetByText("transaction(s)")).ToBeVisibleAsync();
    }
}
