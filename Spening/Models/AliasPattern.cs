public enum AliasPatternMatchType { Contains, StartsWith, Regex }

/// <summary>
/// A substring or regex rule that maps normalized merchant strings to a BusinessAlias.
/// One alias can have many patterns; the first matching alias (lowest id) wins.
/// </summary>
public class AliasPattern
{
    public int Id { get; set; }
    public int AliasId { get; set; }

    /// <summary>The pattern string (uppercase, already normalized for Contains/StartsWith).</summary>
    public string Pattern { get; set; } = "";

    public AliasPatternMatchType MatchType { get; set; } = AliasPatternMatchType.Contains;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public BusinessAlias Alias { get; set; } = null!;
}