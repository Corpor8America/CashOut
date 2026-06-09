public class Transaction
{
    public string TransactionId { get; set; } = ""; // Plaid's stable ID — PK
    public string AccountId { get; set; } = "";
    public DateOnly Date { get; set; }
    public string Name { get; set; } = "";
    public decimal Amount { get; set; }
    /// <summary>
    /// Category from Plaid's personal_finance_category.primary field.
    /// Stored as empty string when Plaid returns no category.
    /// Use string.IsNullOrEmpty() checks in UI — category may be "" for pending transactions.
    /// </summary>
    public string Category { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}