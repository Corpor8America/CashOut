public class CategoryRule
{
    public int Id { get; set; }
    public string Pattern { get; set; } = "";
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
