public class Transaction
{
    public string TransactionId { get; set; } = ""; // Plaid's stable ID — PK
    public string AccountId { get; set; } = "";
    public DateOnly Date { get; set; }
    public string Name { get; set; } = "";
    public decimal Amount { get; set; }
    public string Category { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
