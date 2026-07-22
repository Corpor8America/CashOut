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
        string accountId, string csvContent, CsvMappingProfile profile)
    {
        // Resolve LinkedAccount Guid → Plaid account_id so CSV transactions
        // use the same AccountId as Plaid-synced ones for the same account.
        var resolvedAccountId = accountId;
        if (Guid.TryParse(accountId, out var guid))
        {
            var linked = await _db.LinkedAccounts.FindAsync(guid);
            if (linked != null)
                resolvedAccountId = linked.AccountId;
        }

        var rows = ParseCsv(csvContent);
        rows = ApplyRowTrimming(rows, profile.SkipRowsFromTop, profile.SkipRowsFromBottom);

        if (rows.Count <= 1)
            return new ImportResult(0, 0, new List<SkippedRow>());

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

        // Pre-load normalization data for batch processing
        var allPatterns = await _db.AliasPatterns
            .Include(p => p.Alias)
            .ToListAsync();

        var rawByNormalized = await _db.RawBusinesses
            .ToDictionaryAsync(b => b.RawNameNormalized);

        // Collect all distinct dates from parsed rows so we can batch-load
        // existing DB transactions for additive-only dedup.
        var distinctDates = new List<DateOnly>();
        foreach (var row in dataRows)
        {
            var rawDate = GetField(row, dateIdx);
            if (DateOnly.TryParse(rawDate, out var d) && !distinctDates.Contains(d))
                distinctDates.Add(d);
        }

        // Load existing transactions for each date + account (date-exact match).
        // Build a HashSet for O(1) lookup: (date, signed amount, normalizedName).
        var existingTuples = new HashSet<(DateOnly date, decimal amount, string normalizedName)>();
        foreach (var d in distinctDates)
        {
            var txnsForDate = await _db.Transactions
                .Where(t => t.AccountId == resolvedAccountId && t.Date == d)
                .ToListAsync();
            foreach (var t in txnsForDate)
                existingTuples.Add((t.Date, t.Amount, t.NormalizedName));
        }

        int imported = 0;
        int skippedAlreadyPresent = 0;
        var skippedRows = new List<SkippedRow>();

        for (int rowNum = 0; rowNum < dataRows.Count; rowNum++)
        {
            var row = dataRows[rowNum];
            var rawRowNum = rowNum + 2; // 1-based + header

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

            var categoryRaw = GetField(row, categoryIdx);

            var (alias, rawBusiness, normalizedName, effectiveCategory) = await _normalization.ResolveBulk(
                description, categoryRaw, allPatterns, rawByNormalized);

            // ── Additive-only dedup: skip if (date, signed amount, normalizedName) already in DB ──
            if (existingTuples.Contains((date, amount, normalizedName)))
            {
                skippedAlreadyPresent++;
                continue;
            }

            // When an alias matched, use the canonical alias name as the display name.
            // description (raw) is preserved in RawName.
            var displayName = alias != null ? alias.AliasName : description;

            var txn = new Transaction
            {
                TransactionId = $"csv-{Guid.NewGuid()}",
                AccountId = resolvedAccountId,
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
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _db.Transactions.Add(txn);
            // Do NOT add to existingTuples — two identical rows in the same CSV both get inserted.
            imported++;
        }

        await _db.SaveChangesAsync();
        return new ImportResult(imported, skippedAlreadyPresent, skippedRows);
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
public record ImportResult(int Imported, int SkippedAlreadyPresent, List<SkippedRow> SkippedRows);