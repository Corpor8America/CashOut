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
    /// Exactly one of Credit or Debit is non-null per transaction.
    /// </summary>
    public decimal? Credit { get; set; }

    /// <summary>
    /// Money leaving the account (e.g. purchase, bill payment, withdrawal).
    /// Exactly one of Credit or Debit is non-null per transaction.
    /// </summary>
    public decimal? Debit { get; set; }

    /// <summary>
    /// Computed: Debit - Credit (stored for query/sort convenience).
    /// Positive = net outflow (expense/debit transaction).
    /// Negative = net inflow (income/refund/credit transaction).
    /// Kept in sync with Credit/Debit via NormalizeSingleAmount / NormalizeSplitColumns.
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// Effective category for this transaction.
    /// Set by MerchantNormalizationService during import:
    ///   - Alias category if alias matched and has a category
    ///   - "Unassigned" otherwise (never sourced from CSV/Plaid category)
    /// Can be overridden manually by the user.
    /// </summary>
    public string Category { get; set; } = "";

    // ── Business normalization links ──────────────────────────────────────
    /// <summary>FK to the matched BusinessAlias, if any pattern matched during import.</summary>
    public int? AliasId { get; set; }

    /// <summary>
    /// FK to RawBusiness. Populated for transactions that did not match any alias pattern.
    /// Null for Plaid transactions that matched an alias (no RawBusiness created).
    /// </summary>
    public int? RawBusinessId { get; set; }

    // ── CSV deduplication ─────────────────────────────────────────────────
    /// <summary>
    /// SHA-256 hash of raw CSV column values for the mapped columns (first 16 hex chars).
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
    /// Plaid sign convention: positive = outflow, negative = inflow.
    ///
    ///   externalAmount &lt; 0  → Credit = abs(amount), Debit = null,  Amount = -credit (negative)
    ///   externalAmount >= 0 → Debit  = amount,        Credit = null, Amount = debit  (positive)
    /// </summary>
    public static (decimal? credit, decimal? debit, decimal amount) NormalizeSingleAmount(
        decimal externalAmount)
    {
        if (externalAmount < 0)
        {
            var credit = Math.Abs(externalAmount);
            return (credit, null, -credit);
        }
        else
        {
            return (null, externalAmount, externalAmount);
        }
    }

    /// <summary>
    /// Normalization for CSV rows with separate Credit and Debit columns.
    /// Exactly one must be non-null; if both are set the row should be skipped upstream.
    ///
    ///   Credit row → Amount is negative (inflow)
    ///   Debit  row → Amount is positive (outflow)
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
        // Both null or both set — caller should have caught this
        return (null, null, 0);
    }
}