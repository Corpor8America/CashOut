using Microsoft.EntityFrameworkCore;

public class SettingsService
{
    private readonly AppDbContext _db;

    public SettingsService(AppDbContext db) => _db = db;

    public async Task<string> Get(string key, string defaultValue = "")
    {
        var row = await _db.AppSettings.FindAsync(key);
        return row?.Value ?? defaultValue;
    }

    public async Task Set(string key, string value)
    {
        var row = await _db.AppSettings.FindAsync(key);
        if (row == null)
        {
            _db.AppSettings.Add(new AppSetting { Key = key, Value = value });
        }
        else
        {
            row.Value = value;
            _db.AppSettings.Update(row);
        }
        await _db.SaveChangesAsync();
    }

    public async Task<Dictionary<string, string>> GetAll()
    {
        return await _db.AppSettings
            .ToDictionaryAsync(s => s.Key, s => s.Value);
    }

    // Convenience helpers
    public async Task<string> GetPlaidEnvironment() =>
        await Get("plaid_environment", "sandbox");

    public async Task<int> GetOutputYear() =>
        int.TryParse(await Get("output_year"), out var y) ? y : DateTime.UtcNow.Year;
}
