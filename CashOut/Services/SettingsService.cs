using Microsoft.EntityFrameworkCore;

public class SettingsService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public SettingsService(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    // ── Read / Write ──────────────────────────────────────────────────────

    private async Task<AppSetting> GetRow()
    {
        var row = await _db.AppSettings.FindAsync(1);
        if (row == null)
        {
            row = new AppSetting { Id = 1 };
            _db.AppSettings.Add(row);
            await _db.SaveChangesAsync();
        }
        return row;
    }

    /// <summary>
    /// Reads the Plaid environment from the PLAID_ENV environment variable.
    /// Falls back to ASPNETCORE_ENVIRONMENT-based default (sandbox for non-production).
    /// The DB no longer stores this value — it is deployment configuration.
    /// </summary>
    public string GetPlaidEnvironment()
    {
        var env = _config["PLAID_ENV"]
            ?? Environment.GetEnvironmentVariable("PLAID_ENV");

        if (!string.IsNullOrWhiteSpace(env))
        {
            env = env.Trim().ToLowerInvariant();
            if (env is "sandbox" or "development" or "production")
                return env;
        }

        // Sensible default: sandbox unless explicitly configured otherwise
        return "sandbox";
    }

    /// <summary>
    /// Returns the year of the most recent transaction in the database.
    /// Falls back to the current calendar year if there are no transactions.
    /// </summary>
    public async Task<int> GetOutputYear()
    {
        var maxDate = await _db.Transactions
            .OrderByDescending(t => t.Date)
            .Select(t => (DateOnly?)t.Date)
            .FirstOrDefaultAsync();

        return maxDate?.Year ?? DateTime.UtcNow.Year;
    }

    /// <summary>
    /// Returns a list of available years (up to 7) for the year picker dropdown,
    /// derived from actual transaction data. Always includes the current year.
    /// </summary>
    public async Task<List<int>> GetAvailableYears()
    {
        var currentYear = DateTime.UtcNow.Year;
        var minYear = currentYear - 6; // 7 years inclusive

        var yearsWithData = await _db.Transactions
            .Where(t => t.Date.Year >= minYear)
            .Select(t => t.Date.Year)
            .Distinct()
            .OrderByDescending(y => y)
            .ToListAsync();

        if (!yearsWithData.Contains(currentYear))
            yearsWithData.Insert(0, currentYear);

        return yearsWithData;
    }

    /// <summary>
    /// Returns the list of category names excluded from all reports.
    /// Stored as a comma-separated string in AppSetting.ExcludedCategories.
    /// </summary>
    public async Task<List<string>> GetExcludedCategories()
    {
        var row = await GetRow();
        if (string.IsNullOrWhiteSpace(row.ExcludedCategories))
            return new List<string>();
        return row.ExcludedCategories
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Updates the list of excluded categories. Persists as a comma-separated string.
    /// </summary>
    public async Task SetExcludedCategories(List<string> categories)
    {
        var row = await GetRow();
        row.ExcludedCategories = string.Join(",", categories
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase));
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Returns settings as a dictionary for API consumers.
    /// plaid_environment is now read-only (from env var, not DB).
    /// output_year is dynamic (derived from last transaction).
    /// </summary>
    public async Task<Dictionary<string, string>> GetAll()
    {
        var outputYear = await GetOutputYear();
        var excluded = string.Join(", ", await GetExcludedCategories());
        return new Dictionary<string, string>
        {
            ["plaid_environment"] = GetPlaidEnvironment(),
            ["output_year"] = outputYear.ToString(),
            ["excluded_categories"] = excluded
        };
    }
}