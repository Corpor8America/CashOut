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

    // ── Row trimming ──────────────────────────────────────────────────────

    /// <summary>
    /// Number of rows to skip from the top of the file before the header row.
    /// Default 0 = first row is the header. Use when the bank prepends account
    /// info, report titles, or blank rows above the actual CSV data.
    /// </summary>
    public int SkipRowsFromTop { get; set; } = 0;

    /// <summary>
    /// Number of rows to trim from the bottom of the file after data rows.
    /// Use when the bank appends totals, footers, or summary rows.
    /// </summary>
    public int SkipRowsFromBottom { get; set; } = 0;

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
        if (!string.IsNullOrEmpty(DateColumn)) yield return DateColumn.ToLowerInvariant();
        if (!string.IsNullOrEmpty(DescriptionColumn)) yield return DescriptionColumn.ToLowerInvariant();
        if (!string.IsNullOrEmpty(CreditColumn)) yield return CreditColumn.ToLowerInvariant();
        if (!string.IsNullOrEmpty(DebitColumn)) yield return DebitColumn.ToLowerInvariant();
        if (!string.IsNullOrEmpty(AmountColumn)) yield return AmountColumn.ToLowerInvariant();
        if (!string.IsNullOrEmpty(CategoryColumn)) yield return CategoryColumn.ToLowerInvariant();
    }
}