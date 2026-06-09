/// <summary>
/// Application settings stored as a single typed row (Id = 1).
/// Using a structured model rather than a key-value store gives compile-time safety
/// and makes the schema self-documenting.
/// </summary>
public class AppSetting
{
    public int Id { get; set; } = 1;
    public string PlaidEnvironment { get; set; } = "sandbox";
}