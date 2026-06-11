using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;

public class TransactionService
{
    private readonly AppDbContext _db;
    private readonly PlaidService _plaid;
    private readonly SettingsService _settings;
    private readonly BusinessNormalizationService _normalization;

    public TransactionService(
        AppDbContext db,
        PlaidService plaid,
        SettingsService settings,
        BusinessNormalizationService normalization)
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
                    // Reset cursor and perform a full resync
                    Console.WriteLine(
                        $"[TransactionService] INVALID_CURSOR for account {acct.AccountId} — resetting cursor and resyncing.");
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
            var incomingIds = incoming.Select(t => t.TransactionId).ToHashSet();

            var existingEntities = await _db.Transactions
                .Where(t => incomingIds.Contains(t.TransactionId))
                .ToDictionaryAsync(t => t.TransactionId);

            foreach (var txn in incoming)
            {
                // Business normalization
                var rawBusinessId = await _normalization.GetOrCreateRawBusiness(
                    txn.Name, txn.Category);
                var aliasId = await _normalization.GetAliasId(rawBusinessId);

                // Category priority: alias > raw business > plaid category
                var effectiveCategory = txn.Category;
                if (aliasId.HasValue)
                {
                    var alias = await _db.BusinessAliases.FindAsync(aliasId.Value);
                    if (!string.IsNullOrEmpty(alias?.Category))
                        effectiveCategory = alias.Category;
                }
                else
                {
                    var raw = await _db.RawBusinesses.FindAsync(rawBusinessId);
                    if (!string.IsNullOrEmpty(raw?.Category))
                        effectiveCategory = raw.Category;
                }

                if (!existingEntities.TryGetValue(txn.TransactionId, out var existing))
                {
                    txn.RawBusinessId = rawBusinessId;
                    txn.AliasId = aliasId;
                    txn.Category = effectiveCategory;
                    txn.CreatedAt = DateTime.UtcNow;
                    txn.UpdatedAt = DateTime.UtcNow;
                    _db.Transactions.Add(txn);
                    added++;
                }
                else
                {
                    // Update fields — but preserve user-set category/alias overrides.
                    // Only update the alias/rawBusiness links if they haven't been user-modified.
                    existing.Name = txn.Name;
                    existing.Amount = txn.Amount;
                    existing.Date = txn.Date;
                    existing.UpdatedAt = DateTime.UtcNow;

                    // Only update category if the transaction hasn't been user-overridden.
                    // Since we don't have an explicit "user-edited" flag yet, we update
                    // normalization links but leave a pre-existing non-empty category intact
                    // if no alias is mapped (respecting raw business category as the override).
                    existing.RawBusinessId = rawBusinessId;
                    existing.AliasId = aliasId;
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

    // ── CSV Export ────────────────────────────────────────────────────────

    public async Task<byte[]> ExportCsv(int year)
    {
        var transactions = await Query(year);
        var sb = new StringBuilder();
        sb.AppendLine("Date,Name,Amount,Category,Source,TransactionId,AccountId");

        foreach (var t in transactions)
        {
            sb.AppendLine(
                $"{t.Date}," +
                $"{EscapeCsv(t.Name)}," +
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