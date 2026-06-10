/// <summary>
/// A canonical merchant name defined by the user.
/// Multiple RawBusinesses can map to one Alias.
/// The alias category overrides the raw business category in all reports.
/// </summary>
public class BusinessAlias
{
    public int Id { get; set; }
    public string AliasName { get; set; } = "";
    /// <summary>
    /// Category for this canonical merchant.
    /// Takes priority over the raw business category when resolving transactions.
    /// </summary>
    public string Category { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}