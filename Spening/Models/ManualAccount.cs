/// <summary>
/// A manually managed account — no Plaid connection.
/// All transactions come from CSV uploads.
/// Suitable for cash, unsupported institutions, or historical imports.
/// </summary>
public class ManualAccount
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}