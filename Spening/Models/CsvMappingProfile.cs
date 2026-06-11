using System.Text.Json;

/// <summary>
/// Stores a user's column mapping for a specific account's CSV format.
/// Versioned so re-mapping creates a new profile rather than overwriting history.
/// </summary>
public class CsvMappingProfile
{
    public int Id { get; set; }

    /// <summary>The account this mapping belongs to (linked or manual account ID as string).</summary>
    public string AccountId { get; set; } = "";

    /// <summary>Profile version — increments on each remap.</summary>
    public int Version { get; set; } = 1;

    // ── Column name mappings (case-insensitive header values) ─────────────
    public string DateColumn { get; set; } = "";
    public string DescriptionColumn { get; set; } = "";

    /// <summary>Credit (positive inflow) column. Null if using single amount column.</summary>
    public string? CreditColumn { get; set; }

    /// <summary>Debit (positive outflow) column. Null if using single amount column.</summary>
    public string? DebitColumn { get; set; }

    /// <summary>Single signed amount column. Set when institution uses one amount field.</summary>
    public string? AmountColumn { get; set; }

    /// <summary>Optional category column.</summary>
    public string? CategoryColumn { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Returns all mapped column names (lower-cased) so callers can validate
    /// that the uploaded CSV still contains them.
    /// </summary>
    public IEnumerable<string> MappedColumns()
    {
        yield return DateColumn.ToLowerInvariant();
        yield return DescriptionColumn.ToLowerInvariant();
        if (!string.IsNullOrEmpty(CreditColumn)) yield return CreditColumn.ToLowerInvariant();
        if (!string.IsNullOrEmpty(DebitColumn)) yield return DebitColumn.ToLowerInvariant();
        if (!string.IsNullOrEmpty(AmountColumn)) yield return AmountColumn.ToLowerInvariant();
        if (!string.IsNullOrEmpty(CategoryColumn)) yield return CategoryColumn.ToLowerInvariant();
    }
}