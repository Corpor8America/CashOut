using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;

public class TransactionService
{
    private readonly AppDbContext _db;
    private readonly PlaidService _plaid;
    private readonly SettingsService _settings;
    private readonly MerchantNormalizationService _normalization;

    public TransactionService(
        AppDbContext db,
        PlaidService plaid,
        SettingsService settings,
        MerchantNormalizationService normalization)
    {
        _db = db;
        _plaid = plaid;
        _settings = settings;
        _normalization = normalization;
    }

    // ── Sync ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs incremental sync across all linked accounts.
    /// Each account's cursor is saved immediately after its own successful merge.
    /// If Plaid returns INVALID_CURSOR, resets cursor and does a full resync for that account.
    /// Never modifies or deletes CSV transactions.
    /// </summary>
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

    // ── Fetch ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Full re-fetch of the most recent year across all linked accounts.
    /// Returns the total number of Plaid transactions processed.
    /// Never touches CSV transactions.
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

        await MergePlaid(all, new List<string>());
        return all.Count;
    }

    // ── Merge (Plaid only) ────────────────────────────────────────────────

    private async Task<(int added, int removed)> MergePlaid(
        List<Transaction> incoming, List<string> removedIds)
    {
        // Remove only Plaid transactions — never touch CSV
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
            // Batch-load existing transactions to avoid N+1
            var incomingIds = incoming.Select(t => t.TransactionId).ToHashSet();
            var existingEntities = await _db.Transactions
                .Where(t => incomingIds.Contains(t.TransactionId))
                .ToDictionaryAsync(t => t.TransactionId);

            // Batch-load alias patterns and raw businesses for normalization
            var allPatterns = await _db.AliasPatterns
                .Include(p => p.Alias)
                .ToListAsync();

            var rawNames = incoming
                .Select(t => MerchantNormalizationService.Normalize(t.Name))
                .ToHashSet();

            var rawByNormalized = await _db.RawBusinesses
                .Where(b => rawNames.Contains(b.RawNameNormalized))
                .ToDictionaryAsync(b => b.RawNameNormalized);

            foreach (var txn in incoming)
            {
                var (aliasId, rawBusinessId, normalizedName, effectiveCategory) = await _normalization.ResolveBulk(
                    txn.Name, txn.Category, allPatterns, rawByNormalized);

                // When an alias matched, display the canonical alias name.
                // RawName always preserves the original string from Plaid.
                var displayName = aliasId.HasValue
                    ? allPatterns.First(p => p.AliasId == aliasId).Alias.AliasName
                    : txn.Name;

                if (!existingEntities.TryGetValue(txn.TransactionId, out var existing))
                {
                    txn.AliasId = aliasId;
                    txn.RawBusinessId = rawBusinessId;
                    txn.RawName = txn.Name;
                    txn.NormalizedName = normalizedName;
                    txn.Name = displayName;
                    txn.Category = effectiveCategory;
                    txn.CreatedAt = DateTime.UtcNow;
                    txn.UpdatedAt = DateTime.UtcNow;
                    _db.Transactions.Add(txn);
                    added++;
                }
                else
                {
                    existing.RawName = txn.Name;
                    existing.NormalizedName = normalizedName;
                    existing.Name = displayName;
                    existing.Credit = txn.Credit;
                    existing.Debit = txn.Debit;
                    existing.Amount = txn.Amount;
                    existing.Date = txn.Date;
                    existing.UpdatedAt = DateTime.UtcNow;
                    existing.AliasId = aliasId;
                    existing.RawBusinessId = rawBusinessId;
                    // Only update category if alias is set or existing has no category
                    if (aliasId.HasValue || string.IsNullOrEmpty(existing.Category))
                        existing.Category = effectiveCategory;
                    _db.Transactions.Update(existing);
                }
            }
        }

        await _db.SaveChangesAsync();
        return (added, removedIds.Count);
    }

    // ── Query ─────────────────────────────────────────────────────────────

    public async Task<List<Transaction>> Query(
        int? year = null, string? accountId = null, string? category = null,
        TransactionSource? source = null)
    {
        var q = _db.Transactions.AsQueryable();

        if (year.HasValue)
            q = q.Where(t => t.Date.Year == year.Value);

        if (!string.IsNullOrEmpty(accountId))
            q = q.Where(t => t.AccountId == accountId);

        if (!string.IsNullOrEmpty(category))
            q = q.Where(t => t.Category == category);

        if (source.HasValue)
            q = q.Where(t => t.Source == source.Value);

        return await q.OrderByDescending(t => t.Date).ToListAsync();
    }

    // ── Category Edit ─────────────────────────────────────────────────────

    /// <summary>
    /// Persists a user-supplied category override on a single transaction.
    /// This is the highest-priority category source and will not be overwritten
    /// by future sync/fetch operations (MergePlaid only updates category when
    /// an alias is set or the existing category is empty).
    /// </summary>
    public async Task<Transaction?> UpdateCategory(string transactionId, string category)
    {
        var txn = await _db.Transactions.FindAsync(transactionId);
        if (txn == null) return null;

        txn.Category = category.Trim();
        txn.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return txn;
    }

    // ── CSV Export ────────────────────────────────────────────────────────

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