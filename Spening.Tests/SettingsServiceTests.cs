using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Spening.Tests;

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

    [TestMethod]
    public async Task Set_ThenGet_RoundTrips()
    {
        var db = BuildDb(nameof(Set_ThenGet_RoundTrips));
        var svc = new SettingsService(db);

        await svc.SetPlaidEnvironment("production");
        var result = await svc.GetPlaidEnvironment();

        Assert.AreEqual("production", result);
    }

    [TestMethod]
    public async Task Set_ExistingKey_Overwrites()
    {
        var db = BuildDb(nameof(Set_ExistingKey_Overwrites));
        var svc = new SettingsService(db);

        await svc.SetPlaidEnvironment("sandbox");
        await svc.SetPlaidEnvironment("production");
        var result = await svc.GetPlaidEnvironment();

        Assert.AreEqual("production", result);
    }

    [TestMethod]
    public async Task GetAll_ReturnsAllRows()
    {
        var db = BuildDb(nameof(GetAll_ReturnsAllRows));
        var svc = new SettingsService(db);

        await svc.SetPlaidEnvironment("sandbox");

        var all = await svc.GetAll();

        Assert.AreEqual(2, all.Count);
        Assert.AreEqual("sandbox", all["plaid_environment"]);
    }

    [TestMethod]
    public async Task GetPlaidEnvironment_DefaultsToSandbox_WhenMissing()
    {
        var db = BuildDb(nameof(GetPlaidEnvironment_DefaultsToSandbox_WhenMissing));
        var svc = new SettingsService(db);

        var env = await svc.GetPlaidEnvironment();

        Assert.AreEqual("sandbox", env);
    }

    [TestMethod]
    public async Task GetOutputYear_DefaultsToCurrentYear_WhenMissing()
    {
        var db = BuildDb(nameof(GetOutputYear_DefaultsToCurrentYear_WhenMissing));
        var svc = new SettingsService(db);

        var year = await svc.GetOutputYear();

        Assert.AreEqual(DateTime.UtcNow.Year, year);
    }
}