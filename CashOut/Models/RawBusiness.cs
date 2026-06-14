/// <summary>
/// Stores the first occurrence of an unmapped merchant name exactly as it appeared
/// in Plaid or CSV data, alongside its normalized form used for pattern matching.
/// Retained permanently for audit and retroactive fixing.
/// CSV/Plaid categories are stored as CategoryRaw for reference only — never used for categorization.
/// </summary>
public class RawBusiness
{
    public int Id { get; set; }

    /// <summary>Original merchant string exactly as received from CSV/Plaid.</summary>
    public string RawName { get; set; } = "";

    /// <summary>
    /// Normalized form: uppercase, punctuation removed, long numeric sequences stripped.
    /// Used as the match target for AliasPatterns. Unique index.
    /// </summary>
    public string RawNameNormalized { get; set; } = "";

    /// <summary>
    /// Category string from the CSV/Plaid source. Stored for reference only.
    /// Never used for transaction categorization.
    /// </summary>
    public string CategoryRaw { get; set; } = "";

    /// <summary>True once a user has mapped this business to an alias.</summary>
    public bool IsMapped { get; set; } = false;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}