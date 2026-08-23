using System.Net.Http.Json;
using Microsoft.Playwright;
using Microsoft.Playwright.MSTest;

namespace CashOut.Tests.UiTests.Helpers;

[TestClass]
public abstract class UiTestBase : PageTest
{
    private static CashOutAppFactory? _appFactory;
    private static readonly object _lock = new();

    protected static CashOutAppFactory AppFactory
    {
        get
        {
            if (_appFactory == null)
            {
                lock (_lock)
                {
                    _appFactory ??= new CashOutAppFactory();
                    _appFactory.StartAsync().GetAwaiter().GetResult();
                }
            }
            return _appFactory;
        }
    }

    protected static string BaseUrl => AppFactory.Server.BaseAddress.ToString().TrimEnd('/');

    protected static HttpClient Api => AppFactory.CreateClient();

    [TestInitialize]
    public async Task UiTestInitialize()
    {
        Page.SetDefaultTimeout(10000);
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    protected async Task<Guid> CreateAccountViaApi(string name, string description = "")
    {
        var response = await Api.PostAsJsonAsync("api/accounts", new { Name = name, Description = description });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<CreateAccountResponse>();
        return json!.Id;
    }

    protected async Task ImportCsvViaApi(Guid accountId, string csvContent, string fileName = "import.csv")
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(csvContent), "file", fileName);

        var response = await Api.PostAsync($"api/csv-import/{accountId}/import", form);
        response.EnsureSuccessStatusCode();
    }

    protected async Task<List<AccountDto>> GetAccountsViaApi()
    {
        return await Api.GetFromJsonAsync<List<AccountDto>>("api/accounts") ?? new();
    }

    protected async Task UploadCsvFile(string csvContent, string fileName = "test.csv")
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}_{fileName}");
        await File.WriteAllTextAsync(tempFile, csvContent);
        try
        {
            var fileInput = Page.Locator("#csvFileInput");
            await fileInput.SetInputFilesAsync(tempFile);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    protected async Task MapColumnsAndImport()
    {
        await Page.GetByLabel("Date column *").ClickAsync();
        await Page.GetByRole(AriaRole.Option, new() { Name = "Date" }).ClickAsync();

        await Page.GetByLabel("Description column *").ClickAsync();
        await Page.GetByRole(AriaRole.Option, new() { Name = "Description" }).ClickAsync();

        await Page.GetByLabel("Credit column").ClickAsync();
        await Page.GetByRole(AriaRole.Option, new() { Name = "Amount" }).ClickAsync();

        await Page.GetByLabel("Debit column").ClickAsync();
        await Page.GetByRole(AriaRole.Option, new() { Name = "Amount" }).ClickAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Import" }).ClickAsync();
        await Expect(Page.GetByText("Import Complete")).ToBeVisibleAsync();
    }

    private record CreateAccountResponse(Guid Id, string Name, string Description, DateTime CreatedAt);
    public record AccountDto(Guid Id, string Name, string Description, DateTime CreatedAt);
}
