using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Exports every persisted table into one zip backup file and restores it in place.
/// Import is validated before anything is written, then applied atomically.
/// </summary>
public class DataTransferService
{
    public const string BackupFormat = "cashout-backup";
    public const int BackupSchemaVersion = 1;
    public const string ManifestEntry = "manifest.json";
    public const string DataEntry = "data.json";

    private const long MaxEntryBytes = 100L * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = false
    };

    // Constants rather than interpolated SQL so EF's SQL-injection analyzer stays quiet.
    // EF created quoted PascalCase columns ("Id"), so identifiers must be quoted too.
    private static readonly string[] SequenceResetSql =
    {
        "SELECT setval(pg_get_serial_sequence('\"categories\"', 'Id'), COALESCE((SELECT MAX(\"Id\") FROM \"categories\"), 1), EXISTS (SELECT 1 FROM \"categories\"))",
        "SELECT setval(pg_get_serial_sequence('\"category_rules\"', 'Id'), COALESCE((SELECT MAX(\"Id\") FROM \"category_rules\"), 1), EXISTS (SELECT 1 FROM \"category_rules\"))",
        "SELECT setval(pg_get_serial_sequence('\"csv_mapping_profiles\"', 'Id'), COALESCE((SELECT MAX(\"Id\") FROM \"csv_mapping_profiles\"), 1), EXISTS (SELECT 1 FROM \"csv_mapping_profiles\"))"
    };

    private readonly AppDbContext _db;

    public DataTransferService(AppDbContext db) => _db = db;

    // ── Export ─────────────────────────────────────────────────────────────

    public async Task<byte[]> ExportAsync()
    {
        var accounts = (await _db.Accounts.AsNoTracking().OrderBy(a => a.Id).ToListAsync())
            .Select(a => new AccountBackup(a.Id, a.Name, a.Description, a.CreatedAt))
            .ToList();

        var transactions = (await _db.Transactions.AsNoTracking().OrderBy(t => t.TransactionId).ToListAsync())
            .Select(t => new TransactionBackup(
                t.TransactionId, t.AccountId, t.Source.ToString(), t.Date, t.Name, t.RawName,
                t.Credit, t.Debit, t.Category, t.CategoryId, t.CategoryRuleId,
                t.CreatedAt, t.UpdatedAt))
            .ToList();

        var profiles = (await _db.CsvMappingProfiles.AsNoTracking().OrderBy(p => p.Id).ToListAsync())
            .Select(p => new CsvMappingProfileBackup(
                p.Id, p.AccountId, p.Version, p.SkipRowsFromTop, p.SkipRowsFromBottom,
                p.DateColumn, p.DescriptionColumn, p.CreditColumn, p.DebitColumn,
                p.AmountColumn, p.CategoryColumn, p.PdfPages, p.PdfRowRegex,
                p.PdfDateColumnEnd, p.PdfAmountColumnStart, p.PdfJoinContinuationRows,
                p.NegativeIsCredit, p.CreatedAt, p.UpdatedAt))
            .ToList();

        var categories = (await _db.Categories.AsNoTracking().OrderBy(c => c.Id).ToListAsync())
            .Select(c => new CategoryBackup(c.Id, c.Name, c.CreatedAt, c.UpdatedAt))
            .ToList();

        var rules = (await _db.CategoryRules.AsNoTracking().OrderBy(r => r.Id).ToListAsync())
            .Select(r => new CategoryRuleBackup(r.Id, r.Pattern, r.CategoryId, r.CreatedAt, r.UpdatedAt))
            .ToList();

        var manifest = new BackupManifest(
            BackupFormat,
            BackupSchemaVersion,
            ReadAppVersion(),
            DateTime.UtcNow,
            new BackupCounts(accounts.Count, transactions.Count, profiles.Count, categories.Count, rules.Count));

        var data = new BackupData(accounts, transactions, profiles, categories, rules);

        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, ManifestEntry, JsonSerializer.Serialize(manifest, JsonOptions));
            WriteEntry(zip, DataEntry, JsonSerializer.Serialize(data, JsonOptions));
        }
        return stream.ToArray();
    }

    private static void WriteEntry(ZipArchive zip, string name, string json)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(json);
    }

    private static string ReadAppVersion()
    {
        var versionFile = Path.Combine(AppContext.BaseDirectory, "VERSION");
        if (File.Exists(versionFile)) return File.ReadAllText(versionFile).Trim();
        return "unknown";
    }

    // ── Import ─────────────────────────────────────────────────────────────

    public async Task<BackupImportResult> ImportAsync(Stream zipStream)
    {
        BackupManifest manifest;
        BackupData data;

        try
        {
            using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
            var manifestEntry = zip.GetEntry(ManifestEntry)
                ?? throw new DataTransferException($"Backup is missing '{ManifestEntry}'.");
            var dataEntry = zip.GetEntry(DataEntry)
                ?? throw new DataTransferException($"Backup is missing '{DataEntry}'.");

            manifest = await ReadEntryAsync<BackupManifest>(manifestEntry);
            data = await ReadEntryAsync<BackupData>(dataEntry);
        }
        catch (DataTransferException) { throw; }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or NotSupportedException)
        {
            throw new DataTransferException("The file is not a valid zip backup.", ex);
        }
        catch (JsonException ex) { throw new DataTransferException("The backup contains invalid JSON.", ex); }
        catch (IOException ex) { throw new DataTransferException("The backup file could not be read.", ex); }

        Validate(manifest, data);

        // In-memory EF (used by unit tests) does not support transactions or SQL.
        var supportsTransactions = _db.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory";
        var transaction = supportsTransactions
            ? await _db.Database.BeginTransactionAsync()
            : null;

        try
        {
            _db.ChangeTracker.Clear();

            // Delete in FK-safe order (category rules reference categories).
            _db.Transactions.RemoveRange(await _db.Transactions.ToListAsync());
            _db.CategoryRules.RemoveRange(await _db.CategoryRules.ToListAsync());
            _db.Categories.RemoveRange(await _db.Categories.ToListAsync());
            _db.CsvMappingProfiles.RemoveRange(await _db.CsvMappingProfiles.ToListAsync());
            _db.Accounts.RemoveRange(await _db.Accounts.ToListAsync());
            await _db.SaveChangesAsync();

            _db.ChangeTracker.Clear();

            // Insert parents before children; transactions last because they use category FKs.
            _db.Accounts.AddRange(data.Accounts.Select(a => new Account
            {
                Id = a.Id,
                Name = a.Name,
                Description = a.Description ?? "",
                CreatedAt = Utc(a.CreatedAt)
            }));

            _db.Categories.AddRange(data.Categories.Select(c => new Category
            {
                Id = c.Id,
                Name = c.Name,
                CreatedAt = Utc(c.CreatedAt),
                UpdatedAt = Utc(c.UpdatedAt)
            }));

            _db.CategoryRules.AddRange(data.CategoryRules.Select(r => new CategoryRule
            {
                Id = r.Id,
                Pattern = r.Pattern,
                CategoryId = r.CategoryId,
                CreatedAt = Utc(r.CreatedAt),
                UpdatedAt = Utc(r.UpdatedAt)
            }));

            _db.CsvMappingProfiles.AddRange(data.CsvMappingProfiles.Select(p => new CsvMappingProfile
            {
                Id = p.Id,
                AccountId = p.AccountId,
                Version = p.Version,
                SkipRowsFromTop = p.SkipRowsFromTop,
                SkipRowsFromBottom = p.SkipRowsFromBottom,
                DateColumn = p.DateColumn,
                DescriptionColumn = p.DescriptionColumn,
                CreditColumn = p.CreditColumn,
                DebitColumn = p.DebitColumn,
                AmountColumn = p.AmountColumn,
                CategoryColumn = p.CategoryColumn,
                PdfPages = p.PdfPages,
                PdfRowRegex = p.PdfRowRegex,
                PdfDateColumnEnd = p.PdfDateColumnEnd,
                PdfAmountColumnStart = p.PdfAmountColumnStart,
                PdfJoinContinuationRows = p.PdfJoinContinuationRows,
                NegativeIsCredit = p.NegativeIsCredit,
                CreatedAt = Utc(p.CreatedAt),
                UpdatedAt = Utc(p.UpdatedAt)
            }));

            _db.Transactions.AddRange(data.Transactions.Select(t => new Transaction
            {
                TransactionId = t.TransactionId,
                AccountId = t.AccountId,
                Source = Enum.Parse<TransactionSource>(t.Source),
                Date = t.Date,
                Name = t.Name,
                RawName = t.RawName ?? "",
                Credit = t.Credit,
                Debit = t.Debit,
                Category = t.Category ?? "",
                CategoryId = t.CategoryId,
                CategoryRuleId = t.CategoryRuleId,
                CreatedAt = Utc(t.CreatedAt),
                UpdatedAt = Utc(t.UpdatedAt)
            }));

            await _db.SaveChangesAsync();
            await ResetIdentitySequencesAsync();

            if (transaction != null) await transaction.CommitAsync();

            return new BackupImportResult(
                data.Accounts.Count, data.Transactions.Count, data.CsvMappingProfiles.Count,
                data.Categories.Count, data.CategoryRules.Count);
        }
        catch
        {
            if (transaction != null) await transaction.RollbackAsync();
            throw;
        }
        finally
        {
            if (transaction != null) await transaction.DisposeAsync();
        }
    }

    private static async Task<T> ReadEntryAsync<T>(ZipArchiveEntry entry)
    {
        if (entry.Length > MaxEntryBytes)
            throw new DataTransferException("The backup file contains an entry that is too large.");

        using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var json = await reader.ReadToEndAsync();
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
               ?? throw new DataTransferException("The backup file is empty or invalid.");
    }

    private static DateTime Utc(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc);

    // Explicit IDs do not advance Postgres identity sequences; fix that or the next
    // normal insert will collide with a restored ID.
    private async Task ResetIdentitySequencesAsync()
    {
        if (!_db.Database.IsNpgsql()) return;

        foreach (var sql in SequenceResetSql)
            await _db.Database.ExecuteSqlRawAsync(sql);
    }

    // ── Validation ─────────────────────────────────────────────────────────

    private static void Validate(BackupManifest manifest, BackupData data)
    {
        if (!string.Equals(manifest.Format, BackupFormat, StringComparison.Ordinal))
            throw new DataTransferException("This file is not a CashOut backup.");
        if (manifest.SchemaVersion > BackupSchemaVersion)
            throw new DataTransferException("This backup was created by a newer version of CashOut. Please upgrade CashOut first.");
        if (manifest.SchemaVersion != BackupSchemaVersion)
            throw new DataTransferException($"Unsupported backup schema version '{manifest.SchemaVersion}'.");
        if (string.IsNullOrWhiteSpace(manifest.AppVersion))
            throw new DataTransferException("The backup manifest is missing the app version.");
        if (manifest.Counts is null)
            throw new DataTransferException("The backup manifest is missing its counts.");

        if (data.Accounts is null || data.Transactions is null || data.CsvMappingProfiles is null ||
            data.Categories is null || data.CategoryRules is null)
            throw new DataTransferException("The backup data is incomplete.");

        var expected = new BackupCounts(
            data.Accounts.Count, data.Transactions.Count, data.CsvMappingProfiles.Count,
            data.Categories.Count, data.CategoryRules.Count);
        if (manifest.Counts != expected)
            throw new DataTransferException("The backup counts do not match the data in the file.");

        var accountIds = new HashSet<Guid>();
        foreach (var a in data.Accounts)
        {
            if (a is null || string.IsNullOrWhiteSpace(a.Name))
                throw new DataTransferException("The backup contains an invalid account.");
            if (!accountIds.Add(a.Id))
                throw new DataTransferException($"The backup contains duplicate account ID '{a.Id}'.");
        }

        var categoryIds = new HashSet<int>();
        var categoryNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in data.Categories)
        {
            if (c is null || string.IsNullOrWhiteSpace(c.Name))
                throw new DataTransferException("The backup contains an invalid category.");
            if (!categoryIds.Add(c.Id))
                throw new DataTransferException($"The backup contains duplicate category ID '{c.Id}'.");
            if (!categoryNames.Add(c.Name))
                throw new DataTransferException($"The backup contains duplicate category name '{c.Name}'.");
        }

        var ruleIds = new HashSet<int>();
        foreach (var r in data.CategoryRules)
        {
            if (r is null || string.IsNullOrWhiteSpace(r.Pattern))
                throw new DataTransferException("The backup contains an invalid category rule.");
            if (!ruleIds.Add(r.Id))
                throw new DataTransferException($"The backup contains duplicate category rule ID '{r.Id}'.");
            if (!categoryIds.Contains(r.CategoryId))
                throw new DataTransferException($"Category rule {r.Id} references missing category {r.CategoryId}.");
        }

        var profileIds = new HashSet<int>();
        foreach (var p in data.CsvMappingProfiles)
        {
            if (p is null || string.IsNullOrWhiteSpace(p.AccountId) ||
                string.IsNullOrWhiteSpace(p.DateColumn) || string.IsNullOrWhiteSpace(p.DescriptionColumn))
                throw new DataTransferException("The backup contains an invalid CSV mapping profile.");
            if (!profileIds.Add(p.Id))
                throw new DataTransferException($"The backup contains duplicate profile ID '{p.Id}'.");
        }

        var transactionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in data.Transactions)
        {
            if (t is null || string.IsNullOrWhiteSpace(t.TransactionId) || t.AccountId is null || t.Name is null)
                throw new DataTransferException("The backup contains an invalid transaction.");
            if (!transactionIds.Add(t.TransactionId))
                throw new DataTransferException($"The backup contains duplicate transaction ID '{t.TransactionId}'.");
            if (!Enum.TryParse<TransactionSource>(t.Source, ignoreCase: false, out var source) ||
                !Enum.IsDefined(source))
                throw new DataTransferException($"Transaction '{t.TransactionId}' has an unknown source '{t.Source}'.");
            if (t.CategoryId.HasValue && !categoryIds.Contains(t.CategoryId.Value))
                throw new DataTransferException($"Transaction '{t.TransactionId}' references missing category {t.CategoryId}.");
            if (t.CategoryRuleId.HasValue && !ruleIds.Contains(t.CategoryRuleId.Value))
                throw new DataTransferException($"Transaction '{t.TransactionId}' references missing rule {t.CategoryRuleId}.");
        }
    }
}

public class DataTransferException : Exception
{
    public DataTransferException(string message) : base(message) { }
    public DataTransferException(string message, Exception inner) : base(message, inner) { }
}
