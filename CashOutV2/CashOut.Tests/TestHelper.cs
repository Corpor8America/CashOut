using Microsoft.EntityFrameworkCore;

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

    public static SettingsService BuildSettings(AppDbContext db) =>
        new(db);

    public static Transaction MakeTxn(
        string id, int year, int month, int day,
        decimal amount, string name = "Merchant",
        string category = "FOOD", string accountId = "acct-1",
        TransactionSource source = TransactionSource.CSV)
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
