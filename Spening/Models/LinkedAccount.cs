public class LinkedAccount
{
    public Guid Id { get; set; }
    public string AccessToken { get; set; } = "";   // stored encrypted
    public string AccountId { get; set; } = "";     // Plaid account_id
    public string Mask { get; set; } = "";
    public string Name { get; set; } = "";
    public string Subtype { get; set; } = "";
    public string Institution { get; set; } = "";
    public string? SyncCursor { get; set; }
    public DateTime CreatedAt { get; set; }
}
