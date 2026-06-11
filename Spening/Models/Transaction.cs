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
    /// Money entering the account (e.g. payroll, refund, credit card payment received).
    /// Exactly one of Credit or Debit is non-null.
    /// </summary>
    public decimal? Credit { get; set; }

    /// <summary>
    /// Money leaving the account (e.g. purchase, bill payment, withdrawal).
    /// Exactly one of Credit or Debit is non-null.
    /// </summary>
    public decimal? Debit { get; set; }

    /// <summary>
    /// Computed: Debit - Credit. Always >= 0.
    /// Positive = net outflow (expense), negative = net inflow (income/refund).
    /// Stored for query/sort convenience and backward compatibility with reports.
    /// Must be kept in sync with Credit/Debit via the SetAmount helper.
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

    // ── Normalization helpers ─────────────────────────────────────────────

    /// <summary>
    /// Universal normalization rule for a single signed external amount.
    /// Applies to Plaid, CSV single-amount columns, and manual entries.
    ///
    /// If externalAmount &lt; 0 → Credit = abs(externalAmount), Debit = null
    /// If externalAmount >= 0 → Debit = externalAmount, Credit = null
    ///
    /// Amount is always stored as Debit - Credit (positive = outflow).
    /// </summary>
    public static (decimal? credit, decimal? debit, decimal amount) NormalizeSingleAmount(decimal externalAmount)
    {
        if (externalAmount < 0)
        {
            var credit = Math.Abs(externalAmount);
            return (credit, null, -credit); // Amount = Debit(0) - Credit = negative inflow
        }
        else
        {
            return (null, externalAmount, externalAmount); // Amount = Debit - Credit(0) = positive outflow
        }
    }

    /// <summary>
    /// Normalization for CSV rows with separate Credit and Debit columns.
    /// Exactly one must be non-null; if both are set the row should be skipped upstream.
    /// </summary>
    public static (decimal? credit, decimal? debit, decimal amount) NormalizeSplitColumns(
        decimal? rawCredit, decimal? rawDebit)
    {
        if (rawCredit.HasValue && !rawDebit.HasValue)
        {
            var c = Math.Abs(rawCredit.Value);
            return (c, null, -c);
        }
        if (rawDebit.HasValue && !rawCredit.HasValue)
        {
            var d = Math.Abs(rawDebit.Value);
            return (null, d, d);
        }
        // Both null or both set — caller should have caught this; return zeroed
        return (null, null, 0);
    }
}