/// <summary>
/// Application settings stored as a single typed row (Id = 1).
/// Plaid environment is no longer stored here — it is read from the PLAID_ENV
/// environment variable at runtime (see SettingsService.GetPlaidEnvironment()).
/// This table is retained as an anchor for future typed settings columns.
/// </summary>
public class AppSetting
{
    public int Id { get; set; } = 1;
}