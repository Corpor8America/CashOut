using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;

public class TransactionService
{
    private readonly AppDbContext _db;
    private readonly PlaidService _plaid;
    private readonly SettingsService _settings;

    public TransactionService(AppDbContext db, PlaidService plaid, SettingsService settings)
    {
        _db = db;
        _plaid = plaid;
        _settings = settings;
    }

    // ── Sync ──────────────────────────────────────────────────────────────

    /// <summary>Runs incremental sync across all linked accounts.</summary>
    public async Task<(int added, int removed)> SyncAll()
    {
        var accounts = await _db.LinkedAccounts.ToListAsync();
        int totalAdded = 0, totalRemoved = 0;

        foreach (var acct in accounts)
        {
            var (newTxns, removedIds, nextCursor) =
                await _plaid.SyncTransactions(acct.AccessToken, acct.SyncCursor);

            var (a, r) = await Merge(newTxns, removedIds);
            totalAdded += a;
            totalRemoved += r;

            acct.SyncCursor = nextCursor;
            _db.LinkedAccounts.Update(acct);
        }

        await _db.SaveChangesAsync();
        return (totalAdded, totalRemoved);
    }

    // ── Fetch ─────────────────────────────────────────────────────────────

    /// <summary>Full re-fetch of the configured year across all accounts.</summary>
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

        var (_, added, _) = await MergeWithTotal(all, new List<string>());
        return added;
    }

    // ── Merge ─────────────────────────────────────────────────────────────

    private async Task<(int added, int removed)> Merge(
        List<Transaction> incoming, List<string> removedIds)
    {
        var (_, added, removed) = await MergeWithTotal(incoming, removedIds);
        return (added, removed);
    }

    private async Task<(int total, int added, int removed)> MergeWithTotal(
        List<Transaction> incoming, List<string> removedIds)
    {
        // Remove deleted transactions
        if (removedIds.Count > 0)
        {
            var toDelete = await _db.Transactions
                .Where(t => removedIds.Contains(t.TransactionId))
                .ToListAsync();
            _db.Transactions.RemoveRange(toDelete);
        }

        int added = 0;
        foreach (var txn in incoming)
        {
            var existing = await _db.Transactions.FindAsync(txn.TransactionId);
            if (existing == null)
            {
                txn.CreatedAt = DateTime.UtcNow;
                txn.UpdatedAt = DateTime.UtcNow;
                _db.Transactions.Add(txn);
                added++;
            }
            else
            {
                existing.Name = txn.Name;
                existing.Amount = txn.Amount;
                existing.Category = txn.Category;
                existing.Date = txn.Date;
                existing.UpdatedAt = DateTime.UtcNow;
                _db.Transactions.Update(existing);
            }
        }

        await _db.SaveChangesAsync();

        var total = await _db.Transactions.CountAsync();
        return (total, added, removedIds.Count);
    }

    // ── Query ─────────────────────────────────────────────────────────────

    public async Task<List<Transaction>> Query(
        int? year = null, string? accountId = null, string? category = null)
    {
        var q = _db.Transactions.AsQueryable();

        if (year.HasValue)
            q = q.Where(t => t.Date.Year == year.Value);

        if (!string.IsNullOrEmpty(accountId))
            q = q.Where(t => t.AccountId == accountId);

        if (!string.IsNullOrEmpty(category))
            q = q.Where(t => t.Category == category);

        return await q.OrderByDescending(t => t.Date).ToListAsync();
    }

    // ── CSV Export ────────────────────────────────────────────────────────

    public async Task<byte[]> ExportCsv(int? year = null)
    {
        var transactions = await Query(year);
        var sb = new StringBuilder();
        sb.AppendLine("Date,Name,Amount,Category,TransactionId,AccountId");

        foreach (var t in transactions)
        {
            sb.AppendLine(
                $"{t.Date}," +
                $"{EscapeCsv(t.Name)}," +
                $"{t.Amount.ToString(CultureInfo.InvariantCulture)}," +
                $"{EscapeCsv(t.Category)}," +
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
