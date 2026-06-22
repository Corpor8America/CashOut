using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CashOut.Tests;

[TestClass]
public class SettingsServiceTests
{
    private static AppDbContext BuildDb(string dbName)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(opts);
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?>? initialData = null)
    {
        IEnumerable<KeyValuePair<string, string?>> data =
        initialData ?? new Dictionary<string, string?>() { { "PLAID_ENV", "production" } };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(data)
            .Build();
    }

    [TestMethod]
    public void Set_ThenGet_RoundTrips()
    {
        var db = BuildDb(nameof(Set_ThenGet_RoundTrips));
        var svc = new SettingsService(db, BuildConfig());

        var result = svc.GetPlaidEnvironment();

        Assert.AreEqual("production", result);
    }

    [TestMethod]
    public void GetPlaidEnvironment_DefaultsToSandbox_WhenMissing()
    {
        var db = BuildDb(nameof(GetPlaidEnvironment_DefaultsToSandbox_WhenMissing));
        var svc = new SettingsService(db, BuildConfig(new Dictionary<string, string?>() { { "PLAID_ENV", null } }));

        var env = svc.GetPlaidEnvironment();

        Assert.AreEqual("sandbox", env);
    }

    [TestMethod]
    public async Task ExcludedCategories_RoundTrips()
    {
        var db = BuildDb(nameof(ExcludedCategories_RoundTrips));
        var svc = new SettingsService(db, BuildConfig());

        await svc.SetExcludedCategories(new List<string> { "FOOD", "TRAVEL" });

        var result = await svc.GetExcludedCategories();
        CollectionAssert.AreEquivalent(new[] { "FOOD", "TRAVEL" }, result);
    }

    [TestMethod]
    public async Task ExcludedCategories_Default_ReturnsEmpty()
    {
        var db = BuildDb(nameof(ExcludedCategories_Default_ReturnsEmpty));
        var svc = new SettingsService(db, BuildConfig());

        var result = await svc.GetExcludedCategories();

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public async Task ExcludedCategories_StripsWhitespaceAndDeduplicates()
    {
        var db = BuildDb(nameof(ExcludedCategories_StripsWhitespaceAndDeduplicates));
        var svc = new SettingsService(db, BuildConfig());

        await svc.SetExcludedCategories(new List<string> { "  FOOD ", "TRAVEL", "food " });

        var result = await svc.GetExcludedCategories();
        Assert.AreEqual(2, result.Count);
        CollectionAssert.Contains(result, "FOOD");
        CollectionAssert.Contains(result, "TRAVEL");
    }

    [TestMethod]
    public async Task ExcludedCategories_GetAll_IncludesExcludedCategories()
    {
        var db = BuildDb(nameof(ExcludedCategories_GetAll_IncludesExcludedCategories));
        var svc = new SettingsService(db, BuildConfig());

        await svc.SetExcludedCategories(new List<string> { "FOOD", "TRAVEL" });

        var all = await svc.GetAll();
        Assert.IsTrue(all.ContainsKey("excluded_categories"));
        Assert.AreEqual("FOOD, TRAVEL", all["excluded_categories"]);
    }
}