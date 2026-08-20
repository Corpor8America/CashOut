# Step 03 — Transaction & CSV Import Pipeline

**Goal:** Create simplified TransactionService and CsvImportService that store transactions raw — no merchant normalization, no alias resolution, no NormalizedName field.

**Prerequisites:** Steps 01–02 complete.

---

## 3.1 TransactionService

Handles Plaid sync/fetch (with upsert), querying, and CSV export. No normalization dependency.

**File:** `CashOut/Services/TransactionService.cs`

```csharp
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

    // ── Sync ──────────────────────────────────────────────────────────────

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

    // ── Merge (Plaid only — raw storage, no normalization) ────────────────

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
                if (!existingEntities.TryGetValue(txn.TransactionId, out var existing))
                {
                    _db.Transactions.Add(txn);
                    added++;
                }
                else
                {
                    existing.RawName = txn.Name;
                    existing.Name = txn.Name;
                    existing.Credit = txn.Credit;
                    existing.Debit = txn.Debit;
                    existing.Amount = txn.Amount;
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

    // ── Query ─────────────────────────────────────────────────────────────

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
```

## 3.2 CsvImportService

Handles CSV preview, profile management, and import. No normalization — transactions stored with raw name and whatever category is in the CSV.

**File:** `CashOut/Services/CsvImportService.cs`

```csharp
using System.Text;
using Microsoft.EntityFrameworkCore;

public class CsvImportService
{
    private readonly AppDbContext _db;

    public CsvImportService(AppDbContext db)
    {
        _db = db;
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

        // Collect distinct dates for batch dedup
        var distinctDates = new List<DateOnly>();
        foreach (var row in dataRows)
        {
            var rawDate = GetField(row, dateIdx);
            if (DateOnly.TryParse(rawDate, out var d) && !distinctDates.Contains(d))
                distinctDates.Add(d);
        }

        // Dedup: (date, signed amount, rawName) — no NormalizedName
        var existingTuples = new HashSet<(DateOnly date, decimal amount, string rawName)>();
        foreach (var d in distinctDates)
        {
            var txnsForDate = await _db.Transactions
                .Where(t => t.AccountId == resolvedAccountId && t.Date == d)
                .ToListAsync();
            foreach (var t in txnsForDate)
                existingTuples.Add((t.Date, t.Amount, t.RawName));
        }

        int imported = 0;
        int skippedAlreadyPresent = 0;
        var skippedRows = new List<SkippedRow>();

        for (int rowNum = 0; rowNum < dataRows.Count; rowNum++)
        {
            var row = dataRows[rowNum];
            var rawRowNum = rowNum + 2;

            var rawDate = GetField(row, dateIdx);
            if (!DateOnly.TryParse(rawDate, out var date))
            {
                skippedRows.Add(new SkippedRow(rawRowNum, TruncateRow(row), "Date could not be parsed"));
                continue;
            }

            // Amount normalization
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

            // Additive-only dedup
            if (existingTuples.Contains((date, amount, description)))
            {
                skippedAlreadyPresent++;
                continue;
            }

            var txn = new Transaction
            {
                TransactionId = $"csv-{Guid.NewGuid()}",
                AccountId = resolvedAccountId,
                Source = TransactionSource.CSV,
                Date = date,
                Name = description,
                RawName = description,
                Credit = credit,
                Debit = debit,
                Amount = amount,
                Category = categoryRaw,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _db.Transactions.Add(txn);
            imported++;
        }

        await _db.SaveChangesAsync();
        return new ImportResult(imported, skippedAlreadyPresent, skippedRows);
    }

    // ── Row Trimming ──────────────────────────────────────────────────────

    private static List<string[]> ApplyRowTrimming(
        List<string[]> rows, int skipTop, int skipBottom)
    {
        if (skipTop > 0)
            rows = rows.Skip(skipTop).ToList();

        if (skipBottom > 0 && rows.Count > 1)
        {
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
```

## 3.3 PdfImportService

PDF statement parser using PdfPig. Extracts text and uses regex heuristics to detect transactions.

**File:** `CashOut/Services/PdfImportService.cs`

```csharp
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

public partial class PdfImportService
{
    public string ExtractCsv(byte[] pdfBytes)
    {
        using var pdf = PdfDocument.Open(pdfBytes);
        var sb = new StringBuilder();

        var allText = new StringBuilder();
        foreach (var page in pdf.GetPages())
            allText.AppendLine(page.Text);

        var text = allText.ToString();
        var year = DetectYear(text);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        sb.AppendLine("Date,Description,Amount");

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            if (TryParseTransaction(trimmed, year, out var date, out var description, out var amount))
                sb.AppendLine(CsvEscape(date) + "," + CsvEscape(description) + "," + amount.ToString("F2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    private static int DetectYear(string text)
    {
        var yearMatch = YearPattern().Match(text);
        if (yearMatch.Success)
        {
            var y = int.Parse(yearMatch.Groups[1].Value);
            if (y >= 2020 && y <= 2030) return y;
        }
        return DateTime.UtcNow.Year;
    }

    private static bool TryParseTransaction(string line, int year, out string date, out string description, out decimal amount)
    {
        date = "";
        description = "";
        amount = 0;

        var trimmed = line.Trim();
        if (trimmed.Length < 10) return false;

        var amtMatch = AmountEndPattern().Match(trimmed);
        if (!amtMatch.Success) return false;

        var amtStr = amtMatch.Groups[1].Value;
        var negative = amtMatch.Groups[2].Success;
        if (!decimal.TryParse(amtStr.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var rawAmt))
            return false;
        amount = negative ? -rawAmt : rawAmt;

        var dateDescPortion = trimmed[..amtMatch.Index].TrimEnd();
        if (dateDescPortion.Length == 0) return false;

        var dateMatch = DateStartPattern().Match(dateDescPortion);
        if (!dateMatch.Success) return false;

        var rawDate = dateMatch.Groups[1].Value;
        description = dateDescPortion[dateMatch.Length..].Trim();
        if (string.IsNullOrWhiteSpace(description)) return false;

        if (DateOnly.TryParse(rawDate, out var d))
        {
            date = d.ToString("yyyy-MM-dd");
            return true;
        }

        if (MDSlashPattern().Match(rawDate).Success)
        {
            var parts = rawDate.Split('/');
            if (int.TryParse(parts[0], out var m) && int.TryParse(parts[1], out var day) && m >= 1 && m <= 12 && day >= 1 && day <= 31)
            {
                date = new DateOnly(year, m, day).ToString("yyyy-MM-dd");
                return true;
            }
        }

        return false;
    }

    private static string CsvEscape(string s)
    {
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    [GeneratedRegex(@"Statement Period|Account Summary|(?:20|30)\d{2}|\b(?:202[0-9]|2030)\b")]
    private static partial Regex YearPattern();

    [GeneratedRegex(@"\s+(-?\$?[\d,]+\.\d{2})\s*$")]
    private static partial Regex AmountEndPattern();

    [GeneratedRegex(@"^\s*(-?\$[\d,]+\.\d{2}|\(\$[\d,]+\.\d{2}\)|[\d,]+\.[\d]{2}-)\s*$")]
    private static partial Regex AmountOnlyPattern();

    [GeneratedRegex(@"^\s*(\d{1,2}/\d{1,2}(?:/\d{4})?|[A-Z][a-z]{2}\s+\d{1,2},?\s+\d{4}|\d{4}-\d{2}-\d{2}|\d{1,2}\s+[A-Z][a-z]{2}\s+\d{4})")]
    private static partial Regex DateStartPattern();

    [GeneratedRegex(@"^\d{1,2}/\d{1,2}(?:/\d{4})?$")]
    private static partial Regex MDSlashPattern();
}
```

## 3.4 Register services in Program.cs

Add to the services section in `CashOut/Program.cs`:

```csharp
builder.Services.AddScoped<CsvImportService>();
builder.Services.AddScoped<PdfImportService>();
builder.Services.AddScoped<TransactionService>();
```

## 3.5 Verify build

```bash
dotnet build CashOut/CashOut.csproj
```

---

## Verification

1. `TransactionService` compiles without `MerchantNormalizationService` dependency
2. `CsvImportService` constructor takes only `AppDbContext` (no normalization service)
3. `MergePlaid` stores transactions raw — sets `RawName = Name`, no alias resolution
4. `CsvImportService.Import` deduplicates on `(date, amount, rawName)` not `(date, amount, normalizedName)`
5. `dotnet build` succeeds
