using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using CashOut.Tests.UiTests.Helpers;

namespace CashOut.Tests.IntegrationTests;

/// <summary>
/// End-to-end tests over the real HTTP API and a real Postgres container (Testcontainers).
/// Tagged as UI so they run in the Docker-enabled UI job, not the unit-test job.
/// </summary>
[TestClass]
[TestCategory("UI")]
public class DataTransferIntegrationTests
{
    private static CashOutAppFactory? _factory;

    [ClassInitialize]
    public static async Task Initialize(TestContext context)
    {
        _factory = new CashOutAppFactory();
        await _factory.StartAsync();
    }

    [ClassCleanup]
    public static async Task Cleanup()
    {
        if (_factory != null) await _factory.DisposeAsync();
    }

    private static HttpClient Api => _factory!.Api;

    [TestMethod]
    public async Task ExportThenImport_RestoresAllDataOverHttp_AndAllowsNewIdentityRows()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (accountId, transactionName) = await SeedAccountWithTransactionsAsync(suffix);
        var category = await PostAsync<CategoryResponse>("api/categories", new { Name = $"Backup cat {suffix}" });
        var rule = await PostAsync<RuleResponse>("api/category-rules",
            new { Pattern = $"RULE-{suffix}", CategoryId = category.Id });

        // Download a backup through the real endpoint.
        var export = await Api.GetAsync("api/data-transfer/export");
        Assert.AreEqual(HttpStatusCode.OK, export.StatusCode);
        Assert.AreEqual("application/zip", export.Content.Headers.ContentType?.MediaType);
        var zipBytes = await export.Content.ReadAsByteArrayAsync();
        using (var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read))
        {
            Assert.IsNotNull(zip.GetEntry("manifest.json"));
            Assert.IsNotNull(zip.GetEntry("data.json"));
        }

        // Add data that must disappear on restore.
        var doomed = await PostAsync<AccountResponse>("api/accounts", new { Name = $"Doomed {suffix}", Description = "" });

        using (var form = new MultipartFormDataContent())
        {
            form.Add(new ByteArrayContent(zipBytes), "file", "backup.zip");
            var response = await Api.PostAsync("api/data-transfer/import", form);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
            var result = await response.Content.ReadFromJsonAsync<BackupImportResult>();
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Accounts >= 1);
            Assert.IsTrue(result.Transactions >= 1);
            Assert.IsTrue(result.Categories >= 1);
            Assert.IsTrue(result.CategoryRules >= 1);
        }

        var accounts = await Api.GetFromJsonAsync<List<AccountResponse>>("api/accounts") ?? new();
        Assert.IsTrue(accounts.Any(a => a.Id == accountId), "Restored account is missing.");
        Assert.IsFalse(accounts.Any(a => a.Id == doomed.Id), "Pre-existing data was not cleared.");

        var transactions = await Api.GetFromJsonAsync<List<TransactionResponse>>("api/transactions") ?? new();
        Assert.IsTrue(transactions.Any(t => t.Name == transactionName), "Restored transaction is missing.");

        var categories = await Api.GetFromJsonAsync<List<CategoryResponse>>("api/categories") ?? new();
        Assert.IsTrue(categories.Any(c => c.Id == category.Id && c.Name == category.Name));

        var rules = await Api.GetFromJsonAsync<List<RuleResponse>>("api/category-rules") ?? new();
        Assert.IsTrue(rules.Any(r => r.Id == rule.Id && r.CategoryId == category.Id));

        // Explicit IDs must not leave identity sequences behind.
        var newCategory = await PostAsync<CategoryResponse>("api/categories", new { Name = $"After import {suffix}" });
        Assert.IsTrue(newCategory.Id > category.Id, "Category identity sequence was not advanced.");
        var newRule = await PostAsync<RuleResponse>("api/category-rules",
            new { Pattern = $"AFTER-{suffix}", CategoryId = newCategory.Id });
        Assert.IsTrue(newRule.Id > rule.Id, "Rule identity sequence was not advanced.");
    }

    [TestMethod]
    public async Task Import_WithInvalidZip_ReturnsBadRequest_AndLeavesDataUnchanged()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var account = await PostAsync<AccountResponse>("api/accounts", new { Name = $"Keep me {suffix}", Description = "" });

        using (var form = new MultipartFormDataContent())
        {
            form.Add(new ByteArrayContent("not a zip"u8.ToArray()), "file", "broken.zip");
            var response = await Api.PostAsync("api/data-transfer/import", form);
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        var accounts = await Api.GetFromJsonAsync<List<AccountResponse>>("api/accounts") ?? new();
        Assert.IsTrue(accounts.Any(a => a.Id == account.Id), "Failed import must not change data.");
    }

    [TestMethod]
    public async Task Import_WithNonZipExtension_ReturnsBadRequest()
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent("anything"u8.ToArray()), "file", "backup.txt");
        var response = await Api.PostAsync("api/data-transfer/import", form);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<(Guid AccountId, string TransactionName)> SeedAccountWithTransactionsAsync(string suffix)
    {
        var account = await PostAsync<AccountResponse>("api/accounts",
            new { Name = $"Source {suffix}", Description = "Backup test" });

        var profileResponse = await Api.PostAsJsonAsync($"api/csv-import/{account.Id}/profile", new
        {
            DateColumn = "Date",
            DescriptionColumn = "Description",
            AmountColumn = "Amount",
            CategoryColumn = "Category",
            NegativeIsCredit = false
        });
        profileResponse.EnsureSuccessStatusCode();

        var name = $"Imported coffee {suffix}";
        var csv = $"Date,Description,Amount,Category\n2025-03-04,{name},-4.50,Food\n";
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(csv), "file", "data.csv");
        var response = await Api.PostAsync($"api/csv-import/{account.Id}/import", form);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());

        return (account.Id, name);
    }

    private static async Task<T> PostAsync<T>(string url, object body)
    {
        var response = await Api.PostAsJsonAsync(url, body);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private record AccountResponse(Guid Id, string Name, string Description, DateTime CreatedAt);
    private record CategoryResponse(int Id, string Name, DateTime CreatedAt, DateTime UpdatedAt);
    private record RuleResponse(int Id, string Pattern, int CategoryId, string CategoryName, int MatchCount);
    private record TransactionResponse(
        string TransactionId, string AccountId, string Name, decimal Amount, string Category, int? CategoryId);
}
