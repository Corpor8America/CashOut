using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CashOut.Tests;

public static class TestHelper
{
    public static AppDbContext CreateInMemoryDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(options);
    }

    public static IConfiguration BuildConfig(Dictionary<string, string?>? initialData = null)
    {
        IEnumerable<KeyValuePair<string, string?>> data =
            initialData ?? new Dictionary<string, string?>();

        return new ConfigurationBuilder()
            .AddInMemoryCollection(data)
            .Build();
    }

    public static SettingsService BuildSettings(AppDbContext db) =>
        new(db, BuildConfig());

    public static Transaction MakeTxn(
        string id, int year, int month, int day,
        decimal amount, string name = "Merchant",
        string category = "FOOD", string accountId = "acct-1",
        TransactionSource source = TransactionSource.Plaid)
    {
        var (credit, debit) = Transaction.NormalizeSingleAmount(amount);
        return new Transaction
        {
            TransactionId = id,
            AccountId = accountId,
            Date = new DateOnly(year, month, day),
            Name = name,
            RawName = name,
            Credit = credit,
            Debit = debit,
            Category = category,
            Source = source,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }
}
