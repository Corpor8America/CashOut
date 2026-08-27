public enum TransactionSource { CSV }

public class Transaction
{
    public string TransactionId { get; set; } = "";
    public string AccountId { get; set; } = "";
    public TransactionSource Source { get; set; } = TransactionSource.CSV;
    public DateOnly Date { get; set; }
    public string Name { get; set; } = "";
    public string RawName { get; set; } = "";

    /// <summary>
    /// Money entering the account (e.g. payroll, refund).
    /// Exactly one of Credit or Debit is non-null per transaction.
    /// </summary>
    public decimal? Credit { get; set; }

    /// <summary>
    /// Money leaving the account (e.g. purchase, bill payment).
    /// Exactly one of Credit or Debit is non-null per transaction.
    /// </summary>
    public decimal? Debit { get; set; }

    public decimal Amount => (Credit ?? 0) - (Debit ?? 0);

    public string Category { get; set; } = "";

    public int? CategoryId { get; set; }
    public Category? EffectiveCategory { get; set; }

    public int? CategoryRuleId { get; set; }
    public CategoryRule? AssignedByRule { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public static (decimal? credit, decimal? debit) NormalizeSingleAmount(
        decimal externalAmount)
    {
        if (externalAmount < 0)
        {
            return (Math.Abs(externalAmount), null);
        }
        else
        {
            return (null, externalAmount);
        }
    }

    public static (decimal? credit, decimal? debit) NormalizeSplitColumns(
        decimal? rawCredit, decimal? rawDebit)
    {
        if (rawCredit.HasValue && !rawDebit.HasValue)
        {
            return (Math.Abs(rawCredit.Value), null);
        }
        if (rawDebit.HasValue && !rawCredit.HasValue)
        {
            return (null, Math.Abs(rawDebit.Value));
        }
        return (null, null);
    }
}
