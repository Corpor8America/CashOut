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
    public Task GetPlaidEnvironment_DefaultsToSandbox_WhenMissing()
    {
        var db = BuildDb(nameof(GetPlaidEnvironment_DefaultsToSandbox_WhenMissing));
        var svc = new SettingsService(db, BuildConfig(new Dictionary<string, string?>() { { "PLAID_ENV", null } }));

        var env = svc.GetPlaidEnvironment();

        Assert.AreEqual("sandbox", env);
        return Task.CompletedTask;
    }
}