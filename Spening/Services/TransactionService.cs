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

    /// <summary>
    /// Runs incremental sync across all linked accounts.
    /// Each account's cursor is saved immediately after its own successful merge
    /// so a failure on one account doesn't corrupt another account's cursor position.
    /// </summary>
    public async Task<(int added, int removed)> SyncAll()
    {
        var accounts = await _db.LinkedAccounts.ToListAsync();
        int totalAdded = 0, totalRemoved = 0;

        foreach (var acct in accounts)
        {
            try
            {
                var (newTxns, removedIds, nextCursor) =
                    await _plaid.SyncTransactions(acct.AccessToken, acct.SyncCursor);

                var (a, r) = await Merge(newTxns, removedIds);
                totalAdded += a;
                totalRemoved += r;

                // Save cursor per-account immediately after successful merge.
                // If the next account's sync fails, this account's cursor is already
                // persisted and won't re-fetch transactions on the next run.
                acct.SyncCursor = nextCursor;
                _db.LinkedAccounts.Update(acct);
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // Log and continue — one bad account shouldn't block others
                Console.Error.WriteLine(
                    $"[TransactionService] SyncAll: failed for account {acct.AccountId}: {ex.Message}");
            }
        }

        return (totalAdded, totalRemoved);
    }

    // ── Fetch ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Full re-fetch of the configured year across all accounts.
    /// Returns the total number of transactions processed (inserted + updated),
    /// not just newly inserted rows.
    /// </summary>
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

        await MergeAll(all, new List<string>());
        // Return the total count processed so the UI shows a meaningful number
        return all.Count;
    }

    // ── Merge ─────────────────────────────────────────────────────────────

    private async Task<(int added, int removed)> Merge(
        List<Transaction> incoming, List<string> removedIds)
    {
        int added = await MergeAll(incoming, removedIds);
        return (added, removedIds.Count);
    }

    /// <summary>
    /// Upserts incoming transactions and deletes removed ones.
    /// Uses a bulk ID lookup instead of per-row FindAsync to avoid N+1 queries.
    /// Returns the count of newly inserted rows.
    /// </summary>
    private async Task<int> MergeAll(List<Transaction> incoming, List<string> removedIds)
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

        if (incoming.Count > 0)
        {
            // Bulk-load all existing IDs in one query instead of one FindAsync per row.
            // For a full-year fetch of 1,000+ transactions this reduces round-trips from
            // N (one per transaction) to 1.
            var incomingIds = incoming.Select(t => t.TransactionId).ToHashSet();
            var existingIds = await _db.Transactions
                .Where(t => incomingIds.Contains(t.TransactionId))
                .Select(t => t.TransactionId)
                .ToHashSetAsync();

            // Also load the existing entity objects for those that need updating
            var existingEntities = await _db.Transactions
                .Where(t => incomingIds.Contains(t.TransactionId))
                .ToDictionaryAsync(t => t.TransactionId);

            foreach (var txn in incoming)
            {
                if (!existingIds.Contains(txn.TransactionId))
                {
                    txn.CreatedAt = DateTime.UtcNow;
                    txn.UpdatedAt = DateTime.UtcNow;
                    _db.Transactions.Add(txn);
                    added++;
                }
                else
                {
                    var existing = existingEntities[txn.TransactionId];
                    existing.Name = txn.Name;
                    existing.Amount = txn.Amount;
                    existing.Category = txn.Category;
                    existing.Date = txn.Date;
                    existing.UpdatedAt = DateTime.UtcNow;
                    _db.Transactions.Update(existing);
                }
            }
        }

        await _db.SaveChangesAsync();
        return added;
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

    /// <summary>
    /// Exports transactions for the given year as CSV bytes.
    /// Year is required — callers should resolve a default before calling.
    /// </summary>
    public async Task<byte[]> ExportCsv(int year)
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