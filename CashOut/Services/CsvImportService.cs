using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

public class CsvImportService
{
    private readonly AppDbContext _db;
    private readonly MerchantNormalizationService _normalization;

    public CsvImportService(AppDbContext db, MerchantNormalizationService normalization)
    {
        _db = db;
        _normalization = normalization;
    }

    // ── Profile Management ────────────────────────────────────────────────

    public async Task<CsvMappingProfile?> GetCurrentProfile(string accountId)
    {
        return await _db.CsvMappingProfiles
            .Where(p => p.AccountId == accountId)
            .OrderByDescending(p => p.Version)
            .FirstOrDefaultAsync();
    }

    public async Task<CsvMappingProfile> SaveProfile(string accountId, CsvMappingProfile profile)
    {
        var maxVersion = await _db.CsvMappingProfiles
            .Where(p => p.AccountId == accountId)
            .MaxAsync(p => (int?)p.Version) ?? 0;

        profile.AccountId = accountId;
        profile.Version = maxVersion + 1;
        profile.CreatedAt = DateTime.UtcNow;

        _db.CsvMappingProfiles.Add(profile);
        await _db.SaveChangesAsync();
        return profile;
    }

    // ── CSV Preview ───────────────────────────────────────────────────────

    /// <summary>
    /// Parses the raw CSV and returns headers + up to 5 data rows.
    /// When skipTop > 0, that many rows are discarded before the header row.
    /// When skipBottom > 0, that many rows are trimmed from the end of data rows.
    /// </summary>
    public CsvPreview Preview(string csvContent, int skipTop = 0, int skipBottom = 0)
    {
        var rows = ParseCsv(csvContent);
        rows = ApplyRowTrimming(rows, skipTop, skipBottom);

        if (rows.Count == 0) return new CsvPreview(Array.Empty<string>(), Array.Empty<string[]>());

        var headers = rows[0];
        var preview = rows.Skip(1).Take(5).ToArray();
        return new CsvPreview(headers, preview);
    }

    // ── Profile Validation ────────────────────────────────────────────────

    public List<string>? ValidateProfile(CsvMappingProfile profile, string[] csvHeaders)
    {
        var headerSet = csvHeaders.Select(h => h.ToLowerInvariant()).ToHashSet();
        var missing = profile.MappedColumns()
            .Where(col => !headerSet.Contains(col))
            .ToList();
        return missing.Count > 0 ? missing : null;
    }

    // ── Import ────────────────────────────────────────────────────────────

    public async Task<ImportResult> Import(
        string accountId, string csvContent, CsvMappingProfile profile,
        HashSet<int>? forceImportRows = null)
    {
        var rows = ParseCsv(csvContent);
        rows = ApplyRowTrimming(rows, profile.SkipRowsFromTop, profile.SkipRowsFromBottom);

        if (rows.Count <= 1)
            return new ImportResult(0, 0, 0, new List<SkippedRow>());

        var headers = rows[0].Select(h => h.ToLowerInvariant()).ToList();
        var dataRows = rows.Skip(1).ToList();

        var headerArr = rows[0];
        var missing = ValidateProfile(profile, headerArr);
        if (missing != null)
            throw new InvalidOperationException(
                $"CSV mapping is invalid — missing columns: {string.Join(", ", missing)}. Please remap.");

        int ColIdx(string? colName) => string.IsNullOrEmpty(colName) ? -1
            : headers.IndexOf(colName.ToLowerInvariant());

        var dateIdx = ColIdx(profile.DateColumn);
        var descIdx = ColIdx(profile.DescriptionColumn);
        var creditIdx = ColIdx(profile.CreditColumn);
        var debitIdx = ColIdx(profile.DebitColumn);
        var amountIdx = ColIdx(profile.AmountColumn);
        var categoryIdx = ColIdx(profile.CategoryColumn);

        var existingDedupKeys = await _db.Transactions
            .Where(t => t.AccountId == accountId && t.DedupKey != null)
            .Select(t => t.DedupKey!)
            .ToHashSetAsync();

        // Pre-load normalization data for batch processing
        var allPatterns = await _db.AliasPatterns
            .Include(p => p.Alias)
            .ToListAsync();

        var rawByNormalized = await _db.RawBusinesses
            .ToDictionaryAsync(b => b.RawNameNormalized);

        // Gather min/max date boundaries to batch-load potential cross-source duplicates
        DateOnly? minDate = null;
        DateOnly? maxDate = null;
        foreach (var row in dataRows)
        {
            var rawDate = GetField(row, dateIdx);
            if (DateOnly.TryParse(rawDate, out var d))
            {
                if (minDate == null || d < minDate) minDate = d;
                if (maxDate == null || d > maxDate) maxDate = d;
            }
        }

        var existingTxns = new List<Transaction>();
        if (minDate.HasValue && maxDate.HasValue)
        {
            var searchStart = minDate.Value.AddDays(-1);
            var searchEnd = maxDate.Value.AddDays(1);

            existingTxns = await _db.Transactions
                .Where(t => t.AccountId == accountId && t.Date >= searchStart && t.Date <= searchEnd)
                .ToListAsync();
        }

        var importedTxns = new List<Transaction>();
        int imported = 0;
        int skippedDup = 0;
        int skippedCrossSourceDup = 0;
        var skippedRows = new List<SkippedRow>();

        for (int rowNum = 0; rowNum < dataRows.Count; rowNum++)
        {
            var row = dataRows[rowNum];
            var rawRowNum = rowNum + 2; // 1-based + header

            var dedupKey = BuildDedupKey(row, dateIdx, descIdx, creditIdx, debitIdx, amountIdx, categoryIdx);

            bool forceImport = forceImportRows != null && forceImportRows.Contains(rawRowNum);

            if (!forceImport && existingDedupKeys.Contains(dedupKey))
            {
                skippedDup++;
                continue;
            }

            // Parse date
            var rawDate = GetField(row, dateIdx);
            if (!DateOnly.TryParse(rawDate, out var date))
            {
                skippedRows.Add(new SkippedRow(rawRowNum, TruncateRow(row), "Date could not be parsed"));
                continue;
            }

            // ── Amount normalization ──────────────────────────────────────
            decimal? credit;
            decimal? debit;
            decimal amount;

            if (amountIdx >= 0)
            {
                var rawAmt = GetField(row, amountIdx);
                if (!TryParseAmount(rawAmt, out var parsed))
                {
                    skippedRows.Add(new SkippedRow(rawRowNum, TruncateRow(row), "Amount could not be parsed"));
                    continue;
                }
                if (parsed == 0)
                {
                    skippedRows.Add(new SkippedRow(rawRowNum, TruncateRow(row), "Amount is zero"));
                    continue;
                }
                (credit, debit, amount) = Transaction.NormalizeSingleAmount(parsed);
            }
            else
            {
                var rawCredit = GetField(row, creditIdx);
                var rawDebit = GetField(row, debitIdx);

                bool hasCredit = !string.IsNullOrWhiteSpace(rawCredit);
                bool hasDebit = !string.IsNullOrWhiteSpace(rawDebit);

                if (hasCredit && hasDebit)
                {
                    skippedRows.Add(new SkippedRow(rawRowNum, TruncateRow(row),
                        "Both Credit and Debit contain values"));
                    continue;
                }
                if (!hasCredit && !hasDebit)
                {
                    skippedRows.Add(new SkippedRow(rawRowNum, TruncateRow(row),
                        "Neither Credit nor Debit contains a value"));
                    continue;
                }

                decimal? parsedCredit = null;
                decimal? parsedDebit = null;

                if (hasCredit)
                {
                    if (!TryParseAmount(rawCredit, out var c))
                    {
                        skippedRows.Add(new SkippedRow(rawRowNum, TruncateRow(row),
                            "Credit amount could not be parsed"));
                        continue;
                    }
                    parsedCredit = c;
                }

                if (hasDebit)
                {
                    if (!TryParseAmount(rawDebit, out var d))
                    {
                        skippedRows.Add(new SkippedRow(rawRowNum, TruncateRow(row),
                            "Debit amount could not be parsed"));
                        continue;
                    }
                    parsedDebit = d;
                }

                (credit, debit, amount) = Transaction.NormalizeSplitColumns(parsedCredit, parsedDebit);

                if (credit == null && debit == null)
                {
                    skippedRows.Add(new SkippedRow(rawRowNum, TruncateRow(row), "Amount is zero"));
                    continue;
                }
            }

            var description = GetField(row, descIdx);
            if (string.IsNullOrWhiteSpace(description))
            {
                skippedRows.Add(new SkippedRow(rawRowNum, TruncateRow(row), "Description is empty"));
                continue;
            }

            // CSV/Plaid category stored on RawBusiness for reference only — not used for categorization
            var categoryRaw = GetField(row, categoryIdx);

            // Resolve merchant — alias name becomes the display name when matched
            var (alias, rawBusiness, normalizedName, effectiveCategory) = await _normalization.ResolveBulk(
                description, categoryRaw, allPatterns, rawByNormalized);

            // ── Cross-source deduplication (In-Memory) ─────────────────────
            var matchDateMin = date.AddDays(-1);
            var matchDateMax = date.AddDays(1);
            var matchAmount = Math.Abs(amount);

            bool IsCrossSourceMatch(Transaction t) =>
                t.AccountId == accountId &&
                t.Date >= matchDateMin && t.Date <= matchDateMax &&
                Math.Abs(t.Amount) == matchAmount &&
                (t.NormalizedName == normalizedName || (alias != null && t.AliasId == alias.Id));

            var isCrossSourceDuplicate = existingTxns.Any(IsCrossSourceMatch)
                || importedTxns.Any(IsCrossSourceMatch);

            if (!forceImport && isCrossSourceDuplicate)
            {
                skippedCrossSourceDup++;
                continue;
            }

            // When an alias matched, use the canonical alias name as the display name.
            // description (raw) is preserved in RawName.
            var displayName = alias != null ? alias.AliasName : description;

            var txn = new Transaction
            {
                TransactionId = $"csv-{Guid.NewGuid()}",
                AccountId = accountId,
                Source = TransactionSource.CSV,
                Date = date,
                Name = displayName,
                RawName = description,
                NormalizedName = normalizedName,
                Credit = credit,
                Debit = debit,
                Amount = amount,
                Category = effectiveCategory,
                AliasId = alias?.Id,
                Alias = alias,
                RawBusinessId = rawBusiness?.Id == 0 ? null : rawBusiness?.Id,
                RawBusiness = rawBusiness,
                DedupKey = dedupKey,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _db.Transactions.Add(txn);
            importedTxns.Add(txn);
            existingDedupKeys.Add(dedupKey);
            imported++;
        }

        await _db.SaveChangesAsync();
        return new ImportResult(imported, skippedDup, skippedCrossSourceDup, skippedRows);
    }

    // ── Duplicate Scan ────────────────────────────────────────────────────

    public async Task<DuplicateScanResult> ScanForDuplicates(
        string accountId, string csvContent, CsvMappingProfile profile)
    {
        var rows = ParseCsv(csvContent);
        rows = ApplyRowTrimming(rows, profile.SkipRowsFromTop, profile.SkipRowsFromBottom);

        if (rows.Count <= 1)
            return new DuplicateScanResult(new List<ScannedRow>(), 0, 0, 0);

        var headers = rows[0].Select(h => h.ToLowerInvariant()).ToList();
        var dataRows = rows.Skip(1).ToList();

        var headerArr = rows[0];
        var missing = ValidateProfile(profile, headerArr);
        if (missing != null)
            throw new InvalidOperationException(
                $"CSV mapping is invalid — missing columns: {string.Join(", ", missing)}. Please remap.");

        int ColIdx(string? colName) => string.IsNullOrEmpty(colName) ? -1
            : headers.IndexOf(colName.ToLowerInvariant());

        var dateIdx = ColIdx(profile.DateColumn);
        var descIdx = ColIdx(profile.DescriptionColumn);
        var creditIdx = ColIdx(profile.CreditColumn);
        var debitIdx = ColIdx(profile.DebitColumn);
        var amountIdx = ColIdx(profile.AmountColumn);
        var categoryIdx = ColIdx(profile.CategoryColumn);

        var existingDedupKeys = await _db.Transactions
            .Where(t => t.AccountId == accountId && t.DedupKey != null)
            .Select(t => t.DedupKey!)
            .ToHashSetAsync();

        var allPatterns = await _db.AliasPatterns
            .Include(p => p.Alias)
            .ToListAsync();

        var rawByNormalized = await _db.RawBusinesses
            .ToDictionaryAsync(b => b.RawNameNormalized);

        DateOnly? minDate = null;
        DateOnly? maxDate = null;
        foreach (var row in dataRows)
        {
            var rawDate = GetField(row, dateIdx);
            if (DateOnly.TryParse(rawDate, out var d))
            {
                if (minDate == null || d < minDate) minDate = d;
                if (maxDate == null || d > maxDate) maxDate = d;
            }
        }

        var existingTxns = new List<Transaction>();
        if (minDate.HasValue && maxDate.HasValue)
        {
            var searchStart = minDate.Value.AddDays(-1);
            var searchEnd = maxDate.Value.AddDays(1);

            existingTxns = await _db.Transactions
                .Where(t => t.AccountId == accountId && t.Date >= searchStart && t.Date <= searchEnd)
                .ToListAsync();
        }

        var scannedRows = new List<ScannedRow>();
        int newCount = 0;
        int internalDupCount = 0;
        int crossSourceDupCount = 0;
        var importedTxns = new List<Transaction>();

        for (int rowNum = 0; rowNum < dataRows.Count; rowNum++)
        {
            var row = dataRows[rowNum];
            var rawRowNum = rowNum + 2;

            var dedupKey = BuildDedupKey(row, dateIdx, descIdx, creditIdx, debitIdx, amountIdx, categoryIdx);

            if (existingDedupKeys.Contains(dedupKey))
            {
                internalDupCount++;
                scannedRows.Add(new ScannedRow(rawRowNum, TruncateRow(row), "InternalDuplicate", null));
                continue;
            }

            var rawDate = GetField(row, dateIdx);
            if (!DateOnly.TryParse(rawDate, out var date))
            {
                scannedRows.Add(new ScannedRow(rawRowNum, TruncateRow(row), "Invalid", null));
                continue;
            }

            decimal? credit;
            decimal? debit;
            decimal amount;

            if (amountIdx >= 0)
            {
                var rawAmt = GetField(row, amountIdx);
                if (!TryParseAmount(rawAmt, out var parsed) || parsed == 0)
                {
                    scannedRows.Add(new ScannedRow(rawRowNum, TruncateRow(row), "Invalid", null));
                    continue;
                }
                (credit, debit, amount) = Transaction.NormalizeSingleAmount(parsed);
            }
            else
            {
                var rawCredit = GetField(row, creditIdx);
                var rawDebit = GetField(row, debitIdx);
                bool hasCredit = !string.IsNullOrWhiteSpace(rawCredit);
                bool hasDebit = !string.IsNullOrWhiteSpace(rawDebit);

                if ((!hasCredit && !hasDebit) || (hasCredit && hasDebit))
                {
                    scannedRows.Add(new ScannedRow(rawRowNum, TruncateRow(row), "Invalid", null));
                    continue;
                }

                decimal? parsedCredit = null;
                decimal? parsedDebit = null;
                if (hasCredit && TryParseAmount(rawCredit, out var c)) parsedCredit = c;
                if (hasDebit && TryParseAmount(rawDebit, out var d)) parsedDebit = d;

                (credit, debit, amount) = Transaction.NormalizeSplitColumns(parsedCredit, parsedDebit);
                if (credit == null && debit == null)
                {
                    scannedRows.Add(new ScannedRow(rawRowNum, TruncateRow(row), "Invalid", null));
                    continue;
                }
            }

            var description = GetField(row, descIdx);
            if (string.IsNullOrWhiteSpace(description))
            {
                scannedRows.Add(new ScannedRow(rawRowNum, TruncateRow(row), "Invalid", null));
                continue;
            }

            var categoryRaw = GetField(row, categoryIdx);
            var (alias, rawBusiness, normalizedName, _) = await _normalization.ResolveBulk(
                description, categoryRaw, allPatterns, rawByNormalized);

            var matchDateMin = date.AddDays(-1);
            var matchDateMax = date.AddDays(1);
            var matchAmount = Math.Abs(amount);

            bool IsCrossSourceMatch(Transaction t) =>
                t.AccountId == accountId &&
                t.Date >= matchDateMin && t.Date <= matchDateMax &&
                Math.Abs(t.Amount) == matchAmount &&
                (t.NormalizedName == normalizedName || (alias != null && t.AliasId == alias.Id));

            var matchedTxn = existingTxns.FirstOrDefault(IsCrossSourceMatch)
                ?? importedTxns.FirstOrDefault(IsCrossSourceMatch);

            if (matchedTxn != null)
            {
                crossSourceDupCount++;
                var info = new ExistingTransactionInfo(
                    matchedTxn.TransactionId, matchedTxn.Date, matchedTxn.Name, matchedTxn.Amount);
                scannedRows.Add(new ScannedRow(rawRowNum, TruncateRow(row), "CrossSourceDuplicate", info));
            }
            else
            {
                newCount++;
                scannedRows.Add(new ScannedRow(rawRowNum, TruncateRow(row), "New", null));

                // Track this as imported so later rows in the batch can match against it
                importedTxns.Add(new Transaction
                {
                    AccountId = accountId,
                    Date = date,
                    Amount = amount,
                    NormalizedName = normalizedName,
                    AliasId = alias?.Id
                });
            }
        }

        return new DuplicateScanResult(scannedRows, newCount, internalDupCount, crossSourceDupCount);
    }

    // ── Row Trimming ──────────────────────────────────────────────────────

    /// <summary>
    /// Applies top/bottom row skipping to a parsed row list.
    /// skipTop removes rows before the header (the header is the first row
    /// after skipping). skipBottom removes rows from the tail of data rows.
    /// </summary>
    private static List<string[]> ApplyRowTrimming(
        List<string[]> rows, int skipTop, int skipBottom)
    {
        if (skipTop > 0)
            rows = rows.Skip(skipTop).ToList();

        if (skipBottom > 0 && rows.Count > 1)
        {
            // Keep header (index 0) + data rows minus the bottom trim
            var header = rows[0];
            var dataRows = rows.Skip(1).ToList();
            var trimmedData = dataRows.Take(Math.Max(0, dataRows.Count - skipBottom)).ToList();
            rows = new List<string[]> { header };
            rows.AddRange(trimmedData);
        }

        return rows;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string GetField(string[] row, int idx) =>
        idx >= 0 && idx < row.Length ? row[idx].Trim() : "";

    private static bool TryParseAmount(string raw, out decimal result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var cleaned = raw.Replace("$", "").Replace(",", "").Trim();
        if (cleaned.StartsWith('(') && cleaned.EndsWith(')'))
            cleaned = "-" + cleaned[1..^1];
        return decimal.TryParse(cleaned, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out result);
    }

    private static string BuildDedupKey(string[] row, params int[] indices)
    {
        var values = indices
            .Where(i => i >= 0 && i < row.Length)
            .Select(i => row[i].Trim());
        var combined = string.Join("|", values);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
        return Convert.ToHexString(hash)[..16];
    }

    private static string TruncateRow(string[] row, int maxLen = 80)
    {
        var joined = string.Join(", ", row);
        return joined.Length > maxLen ? joined[..maxLen] + "…" : joined;
    }

    private static List<string[]> ParseCsv(string content)
    {
        var result = new List<string[]>();
        var lines = content.ReplaceLineEndings("\n").Split('\n');

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            result.Add(SplitCsvLine(line));
        }

        return result;
    }

    private static string[] SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(c);
            }
            else
            {
                if (c == '"') inQuotes = true;
                else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
        }
        fields.Add(sb.ToString());
        return fields.ToArray();
    }
}

// ── Result types ──────────────────────────────────────────────────────────────

public record CsvPreview(string[] Headers, string[][] Rows);
public record SkippedRow(int RowNumber, string RawData, string Reason);
public record ImportResult(int Imported, int SkippedDuplicates, int SkippedCrossSourceDuplicates, List<SkippedRow> SkippedRows)
{
    public int TotalSkipped => SkippedDuplicates + SkippedCrossSourceDuplicates + SkippedRows.Count;
}

public record ExistingTransactionInfo(string TransactionId, DateOnly Date, string Name, decimal Amount);
public record ScannedRow(int RowNumber, string RawData, string DuplicateType, ExistingTransactionInfo? MatchedTransaction);
public record DuplicateScanResult(List<ScannedRow> Rows, int NewCount, int InternalDuplicateCount, int CrossSourceDuplicateCount);