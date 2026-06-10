using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

public class CsvImportService
{
    private readonly AppDbContext _db;
    private readonly BusinessNormalizationService _normalization;

    public CsvImportService(AppDbContext db, BusinessNormalizationService normalization)
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
        // Get next version number
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
    /// Parses CSV content and returns headers + up to 5 preview rows.
    /// Used by the UI mapping step.
    /// </summary>
    public CsvPreview Preview(string csvContent)
    {
        var rows = ParseCsv(csvContent);
        if (rows.Count == 0) return new CsvPreview(Array.Empty<string>(), Array.Empty<string[]>());

        var headers = rows[0];
        var preview = rows.Skip(1).Take(5).ToArray();
        return new CsvPreview(headers, preview);
    }

    // ── Profile Validation ────────────────────────────────────────────────

    /// <summary>
    /// Checks whether all mapped columns exist in the provided CSV headers.
    /// Returns null if valid, or a list of missing column names.
    /// </summary>
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
        var rows = ParseCsv(csvContent);
        if (rows.Count <= 1)
            return new ImportResult(0, 0, new List<SkippedRow>());

        var headers = rows[0].Select(h => h.ToLowerInvariant()).ToList();
        var dataRows = rows.Skip(1).ToList();

        // Validate profile against actual headers
        var headerArr = rows[0];
        var missing = ValidateProfile(profile, headerArr);
        if (missing != null)
            throw new InvalidOperationException(
                $"CSV mapping is invalid — missing columns: {string.Join(", ", missing)}. Please remap.");

        // Helper to get column index by name
        int ColIdx(string? colName) => string.IsNullOrEmpty(colName) ? -1
            : headers.IndexOf(colName.ToLowerInvariant());

        var dateIdx = ColIdx(profile.DateColumn);
        var descIdx = ColIdx(profile.DescriptionColumn);
        var creditIdx = ColIdx(profile.CreditColumn);
        var debitIdx = ColIdx(profile.DebitColumn);
        var amountIdx = ColIdx(profile.AmountColumn);
        var categoryIdx = ColIdx(profile.CategoryColumn);

        // Load existing dedup keys for this account in one query
        var existingDedupKeys = await _db.Transactions
            .Where(t => t.AccountId == accountId && t.DedupKey != null)
            .Select(t => t.DedupKey!)
            .ToHashSetAsync();

        int imported = 0;
        int skippedDup = 0;
        var skippedRows = new List<SkippedRow>();

        for (int rowNum = 0; rowNum < dataRows.Count; rowNum++)
        {
            var row = dataRows[rowNum];
            var rawRowNum = rowNum + 2; // 1-based, accounting for header row

            // Build dedup key from raw mapped column values
            var dedupKey = BuildDedupKey(row, dateIdx, descIdx, creditIdx, debitIdx, amountIdx, categoryIdx);

            if (existingDedupKeys.Contains(dedupKey))
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

            // Parse amount
            decimal credit = 0, debit = 0;
            string skipReason = "";

            if (amountIdx >= 0)
            {
                // Single amount column
                var rawAmt = GetField(row, amountIdx);
                if (!TryParseAmount(rawAmt, out var parsed))
                {
                    skippedRows.Add(new SkippedRow(rawRowNum, TruncateRow(row), "Amount could not be parsed"));
                    continue;
                }
                if (parsed > 0) credit = parsed;
                else if (parsed < 0) debit = Math.Abs(parsed);
                // Zero amounts: skip
                else
                {
                    skippedRows.Add(new SkippedRow(rawRowNum, TruncateRow(row), "Amount is zero"));
                    continue;
                }
            }
            else
            {
                // Separate credit/debit columns
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

                if (hasCredit && !TryParseAmount(rawCredit, out credit))
                {
                    skippedRows.Add(new SkippedRow(rawRowNum, TruncateRow(row),
                        "Credit amount could not be parsed"));
                    continue;
                }

                if (hasDebit && !TryParseAmount(rawDebit, out debit))
                {
                    skippedRows.Add(new SkippedRow(rawRowNum, TruncateRow(row),
                        "Debit amount could not be parsed"));
                    continue;
                }

                debit = Math.Abs(debit);
            }

            var description = GetField(row, descIdx);
            if (string.IsNullOrWhiteSpace(description))
            {
                skippedRows.Add(new SkippedRow(rawRowNum, TruncateRow(row), "Description is empty"));
                continue;
            }

            var category = GetField(row, categoryIdx);

            // Business normalization
            var rawBusinessId = await _normalization.GetOrCreateRawBusiness(description, category);
            var aliasId = await _normalization.GetAliasId(rawBusinessId);

            // Effective category via priority
            var effectiveCategory = category;
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

            // amount convention: positive = expense (debit), negative = income (credit)
            var amount = debit > 0 ? debit : -credit;

            var txn = new Transaction
            {
                TransactionId = $"csv-{Guid.NewGuid()}",
                AccountId = accountId,
                Source = TransactionSource.CSV,
                Date = date,
                Name = description,
                Amount = amount,
                Category = effectiveCategory,
                RawBusinessId = rawBusinessId,
                AliasId = aliasId,
                DedupKey = dedupKey,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _db.Transactions.Add(txn);
            existingDedupKeys.Add(dedupKey);
            imported++;
        }

        await _db.SaveChangesAsync();
        return new ImportResult(imported, skippedDup, skippedRows);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string GetField(string[] row, int idx) =>
        idx >= 0 && idx < row.Length ? row[idx].Trim() : "";

    private static bool TryParseAmount(string raw, out decimal result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        // Strip $, commas, spaces
        var cleaned = raw.Replace("$", "").Replace(",", "").Trim();

        // Handle parentheses as negative: (123.45) → -123.45
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
        return Convert.ToHexString(hash)[..16]; // 64-bit prefix — collision-resistant enough for this use
    }

    private static string TruncateRow(string[] row, int maxLen = 80)
    {
        var joined = string.Join(", ", row);
        return joined.Length > maxLen ? joined[..maxLen] + "…" : joined;
    }

    /// <summary>
    /// Minimal RFC 4180-compliant CSV parser.
    /// Returns a list of rows, each as a string array of fields.
    /// </summary>
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
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
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

public record ImportResult(int Imported, int SkippedDuplicates, List<SkippedRow> SkippedRows)
{
    public int TotalSkipped => SkippedDuplicates + SkippedRows.Count;
}