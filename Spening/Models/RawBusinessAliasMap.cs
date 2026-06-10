/// <summary>
/// Maps a raw business name to a canonical alias.
/// One raw business can map to at most one alias (enforced via unique index on RawBusinessId).
/// Many raw businesses can map to the same alias.
/// </summary>
public class RawBusinessAliasMap
{
    public int Id { get; set; }
    public int RawBusinessId { get; set; }
    public int AliasId { get; set; }

    public RawBusiness RawBusiness { get; set; } = null!;
    public BusinessAlias Alias { get; set; } = null!;
}