public enum TransactionSource { Plaid, CSV }

public class Transaction
{
    public string TransactionId { get; set; } = ""; // Plaid's stable ID or a generated key for CSV
    public string AccountId { get; set; } = "";     // Plaid account_id or ManualAccount.Id.ToString()

    /// <summary>Whether this came from Plaid sync or a CSV import.</summary>
    public TransactionSource Source { get; set; } = TransactionSource.Plaid;

    public DateOnly Date { get; set; }
    public string Name { get; set; } = "";

    /// <summary>
    /// Legacy single-amount field. For Plaid: positive = expense (debit), negative = income/refund.
    /// For CSV: computed from Credit/Debit columns after mapping.
    /// Kept for backward compatibility with existing reports and queries.
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// Category from Plaid's personal_finance_category.primary, or CSV category column.
    /// Empty string when no category is available.
    /// </summary>
    public string Category { get; set; } = "";

    // ── Business normalization links ──────────────────────────────────────
    public int? RawBusinessId { get; set; }
    public int? AliasId { get; set; }

    // ── CSV deduplication ─────────────────────────────────────────────────
    /// <summary>
    /// Hash of raw CSV column values for the mapped columns.
    /// Used to prevent duplicate imports. Null for Plaid transactions.
    /// </summary>
    public string? DedupKey { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}