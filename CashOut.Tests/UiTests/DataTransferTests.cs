using CashOut.Tests.UiTests.Helpers;
using Microsoft.Playwright;

namespace CashOut.Tests.UiTests;

[TestClass]
[TestCategory("UI")]
public class DataTransferTests : UiTestBase
{
    [TestMethod]
    public async Task ExportButton_DownloadsZipBackup()
    {
        await Page.GotoAsync($"{BaseUrl}/data-transfer");
        await WaitForBlazorContent();

        var download = await Page.RunAndWaitForDownloadAsync(() =>
            Page.GetByRole(AriaRole.Link, new() { Name = "Download Backup (.zip)" }).ClickAsync());

        StringAssert.EndsWith(download.SuggestedFilename, ".zip");
    }

    [TestMethod]
    public async Task ImportButton_RequiresFileAndExplicitConfirmation()
    {
        await Page.GotoAsync($"{BaseUrl}/data-transfer");
        await WaitForBlazorContent();

        var importButton = Page.GetByRole(AriaRole.Button, new() { Name = "Import Backup" });
        await Expect(importButton).ToBeDisabledAsync();

        var backup = await Api.GetByteArrayAsync("api/data-transfer/export");
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}_backup.zip");
        await File.WriteAllBytesAsync(tempFile, backup);
        await Page.Locator("#backupFileInput").SetInputFilesAsync(tempFile);
        await Expect(Page.GetByText($"Selected: {Path.GetFileName(tempFile)}")).ToBeVisibleAsync();

        await Expect(importButton).ToBeDisabledAsync();

        await Page.GetByRole(AriaRole.Checkbox).First.CheckAsync();
        await Expect(importButton).ToBeEnabledAsync();
    }
}
