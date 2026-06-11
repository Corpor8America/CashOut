/// <summary>
/// Stores the exact merchant name as it appears in CSV or Plaid data.
/// Case-insensitive unique key is enforced at the DB level.
/// category is initialized from the first transaction for this name
/// and is never overwritten automatically — only by user action.
/// </summary>
public class RawBusiness
{
    public int Id { get; set; }
    public string RawName { get; set; } = "";
    /// <summary>
    /// Category string from the first transaction seen for this merchant.
    /// Empty string if no category was provided. Never auto-overwritten.
    /// </summary>
    public string Category { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}