using System.Text.Json;

public class CsvMappingProfile
{
    public int Id { get; set; }
    public string AccountId { get; set; } = "";
    public int Version { get; set; } = 1;
    public int SkipRowsFromTop { get; set; } = 0;
    public int SkipRowsFromBottom { get; set; } = 0;
    public string DateColumn { get; set; } = "";
    public string DescriptionColumn { get; set; } = "";
    public string? CreditColumn { get; set; }
    public string? DebitColumn { get; set; }
    public string? AmountColumn { get; set; }
    public string? CategoryColumn { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

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
