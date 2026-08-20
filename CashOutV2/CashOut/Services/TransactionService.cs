using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;

public class TransactionService
{
    private readonly AppDbContext _db;
    private readonly PlaidService _plaid;
    private readonly SettingsService _settings;

    public TransactionService(
        AppDbContext db,
        PlaidService plaid,
        SettingsService settings)
    {
        _db = db;
        _plaid = plaid;
        _settings = settings;
    }

    public async Task<(int added, int removed)> SyncAll()
    {
        var accounts = await _db.LinkedAccounts.ToListAsync();
        int totalAdded = 0, totalRemoved = 0;

        foreach (var acct in accounts)
        {
            try
            {
                List<Transaction> newTxns;
                List<string> removedIds;
                string nextCursor;

                try
                {
                    (newTxns, removedIds, nextCursor) =
                        await _plaid.SyncTransactions(acct.AccessToken, acct.SyncCursor);
                }
                catch (InvalidOperationException ex) when (
                    ex.Message.Contains("INVALID_CURSOR") || ex.Message.Contains("invalid cursor"))
                {
                    Console.WriteLine(
                        $"[TransactionService] INVALID_CURSOR for account {acct.AccountId} — resetting.");
                    acct.SyncCursor = null;
                    (newTxns, removedIds, nextCursor) =
                        await _plaid.SyncTransactions(acct.AccessToken, null);
                }

                var (a, r) = await MergePlaid(newTxns, removedIds);
                totalAdded += a;
                totalRemoved += r;

                acct.SyncCursor = nextCursor;
                _db.LinkedAccounts.Update(acct);
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[TransactionService] SyncAll: failed for account {acct.AccountId}: {ex.Message}");
            }
        }

        return (totalAdded, totalRemoved);
    }

    public async Task<int> FetchAll()
    {
        var year = await _settings.GetOutputYear();
        var accounts = await _db.LinkedAccounts.ToListAsync();
        var all = new List<Transaction>();

        foreach (var acct in accounts)
        {
            var txns = await _plaid.FetchTransactions(acct.AccessToken, year);
            all.AddRange(txns);
        }

        await MergePlaid(all, new List<string>());
        return all.Count;
    }

    private async Task<(int added, int removed)> MergePlaid(
        List<Transaction> incoming, List<string> removedIds)
    {
        if (removedIds.Count > 0)
        {
            var toDelete = await _db.Transactions
                .Where(t => removedIds.Contains(t.TransactionId)
                            && t.Source == TransactionSource.Plaid)
                .ToListAsync();
            _db.Transactions.RemoveRange(toDelete);
        }

        int added = 0;

        if (incoming.Count > 0)
        {
            var incomingIds = incoming.Select(t => t.TransactionId).ToHashSet();
            var existingEntities = await _db.Transactions
                .Where(t => incomingIds.Contains(t.TransactionId))
                .ToDictionaryAsync(t => t.TransactionId);

            foreach (var txn in incoming)
            {
                txn.RawName = txn.Name;

                if (!existingEntities.TryGetValue(txn.TransactionId, out var existing))
                {
                    txn.CreatedAt = DateTime.UtcNow;
                    txn.UpdatedAt = DateTime.UtcNow;
                    _db.Transactions.Add(txn);
                    added++;
                }
                else
                {
                    existing.RawName = txn.Name;
                    existing.Name = txn.Name;
                    existing.Credit = txn.Credit;
                    existing.Debit = txn.Debit;
                    existing.Date = txn.Date;
                    existing.Category = txn.Category;
                    existing.UpdatedAt = DateTime.UtcNow;
                    _db.Transactions.Update(existing);
                }
            }
        }

        await _db.SaveChangesAsync();
        return (added, removedIds.Count);
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
