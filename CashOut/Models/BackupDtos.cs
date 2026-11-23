// DTOs for the single-file backup format (zip containing manifest.json + data.json).
// Entity classes are deliberately not serialized: Transaction.Amount is computed and
// the EF navigation properties would create cycles.

public record BackupManifest(
    string Format,
    int SchemaVersion,
    string AppVersion,
    DateTime ExportedAtUtc,
    BackupCounts Counts);

public record BackupCounts(
    int Accounts,
    int Transactions,
    int CsvMappingProfiles,
    int Categories,
    int CategoryRules);

public record BackupData(
    List<AccountBackup> Accounts,
    List<TransactionBackup> Transactions,
    List<CsvMappingProfileBackup> CsvMappingProfiles,
    List<CategoryBackup> Categories,
    List<CategoryRuleBackup> CategoryRules);

public record AccountBackup(
    Guid Id,
    string Name,
    string Description,
    DateTime CreatedAt);

public record TransactionBackup(
    string TransactionId,
    string AccountId,
    string Source,
    DateOnly Date,
    string Name,
    string RawName,
    decimal? Credit,
    decimal? Debit,
    string Category,
    int? CategoryId,
    int? CategoryRuleId,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record CsvMappingProfileBackup(
    int Id,
    string AccountId,
    int Version,
    int SkipRowsFromTop,
    int SkipRowsFromBottom,
    string DateColumn,
    string DescriptionColumn,
    string? CreditColumn,
    string? DebitColumn,
    string? AmountColumn,
    string? CategoryColumn,
    string? PdfPages,
    string? PdfRowRegex,
    decimal? PdfDateColumnEnd,
    decimal? PdfAmountColumnStart,
    bool PdfJoinContinuationRows,
    bool NegativeIsCredit,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record CategoryBackup(
    int Id,
    string Name,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record CategoryRuleBackup(
    int Id,
    string Pattern,
    int CategoryId,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record BackupImportResult(
    int Accounts,
    int Transactions,
    int CsvMappingProfiles,
    int Categories,
    int CategoryRules);
