using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;

public class TransactionService
{
    private readonly AppDbContext _db;
    private readonly SettingsService _settings;

    public TransactionService(
        AppDbContext db,
        SettingsService settings)
    {
        _db = db;
        _settings = settings;
    }

    public async Task<List<Transaction>> Query(
        int? year = null, int? month = null, string? accountId = null,
        List<string>? categories = null,
        TransactionSource? source = null)
    {
        var q = _db.Transactions.AsQueryable();

        if (year.HasValue)
            q = q.Where(t => t.Date.Year == year.Value);

        if (month.HasValue)
            q = q.Where(t => t.Date.Month == month.Value);

        if (!string.IsNullOrEmpty(accountId))
            q = q.Where(t => t.AccountId == accountId);

        if (categories is { Count: > 0 })
            q = q.Where(t => categories.Contains(t.Category));

        if (source.HasValue)
            q = q.Where(t => t.Source == source.Value);

        return await q.OrderByDescending(t => t.Date).ToListAsync();
    }

    public async Task<Transaction?> UpdateCategory(string transactionId, string category)
    {
        var txn = await _db.Transactions.FindAsync(transactionId);
        if (txn == null) return null;

        txn.Category = category;
        txn.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return txn;
    }

    public async Task<byte[]> ExportCsv(int year)
    {
        var transactions = await Query(year);
        var sb = new StringBuilder();
        sb.AppendLine("Date,Name,Debit,Credit,Amount,Category,Source,TransactionId,AccountId");

        foreach (var t in transactions)
        {
            sb.AppendLine(
                $"{t.Date}," +
                $"{EscapeCsv(t.Name)}," +
                $"{t.Debit?.ToString(CultureInfo.InvariantCulture) ?? ""}," +
                $"{t.Credit?.ToString(CultureInfo.InvariantCulture) ?? ""}," +
                $"{t.Amount.ToString(CultureInfo.InvariantCulture)}," +
                $"{EscapeCsv(t.Category)}," +
                $"{t.Source}," +
                $"{t.TransactionId}," +
                $"{t.AccountId}");
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string EscapeCsv(string s) =>
        s.Contains(',') || s.Contains('"') || s.Contains('\n')
            ? $"\"{s.Replace("\"", "\"\"")}\""
            : s;
}
