/// <summary>
/// Canonical merchant identity defined by the user.
/// Multiple RawBusinesses can map to one Alias via RawBusinessAliasMap.
/// Multiple AliasPatterns can map raw merchant strings to this alias during import.
/// The Category here overrides all other category sources.
/// If Category is empty the transaction is placed in "Unassigned".
/// </summary>
public class BusinessAlias
{
    public int Id { get; set; }
    public string AliasName { get; set; } = "";

    /// <summary>
    /// Category override for all transactions resolved to this alias.
    /// Empty string = Unassigned (user must manually categorize).
    /// </summary>
    public string Category { get; set; } = "";

    public DateTime CreatedAt { get; set; }

    public ICollection<AliasPattern> Patterns { get; set; } = new List<AliasPattern>();
}