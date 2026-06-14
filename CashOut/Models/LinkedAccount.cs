public class LinkedAccount
{
    public Guid Id { get; set; }
    public string AccessToken { get; set; } = "";   // stored encrypted
    public string AccountId { get; set; } = "";     // Plaid account_id
    /// <summary>
    /// Plaid Item ID — stable identifier for the bank login (one Item = one bank connection,
    /// potentially multiple accounts). Used for group-delete on RemoveItem so we don't rely
    /// on matching encrypted tokens (which use a random nonce and differ per encryption call).
    /// </summary>
    public string ItemId { get; set; } = "";
    public string Mask { get; set; } = "";
    public string Name { get; set; } = "";
    public string Subtype { get; set; } = "";
    public string Institution { get; set; } = "";
    public string? SyncCursor { get; set; }
    public DateTime CreatedAt { get; set; }
}