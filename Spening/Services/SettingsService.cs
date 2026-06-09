using Microsoft.EntityFrameworkCore;

public class SettingsService
{
    private readonly AppDbContext _db;

    public SettingsService(AppDbContext db) => _db = db;

    // ── Read / Write ──────────────────────────────────────────────────────

    private async Task<AppSetting> GetRow()
    {
        var row = await _db.AppSettings.FindAsync(1);
        if (row == null)
        {
            // Seed a default row if somehow missing (shouldn't happen after migration)
            row = new AppSetting { Id = 1, PlaidEnvironment = "sandbox" };
            _db.AppSettings.Add(row);
            await _db.SaveChangesAsync();
        }
        return row;
    }

    public async Task<string> GetPlaidEnvironment()
    {
        var row = await GetRow();
        return row.PlaidEnvironment;
    }

    public async Task SetPlaidEnvironment(string environment)
    {
        var row = await GetRow();
        row.PlaidEnvironment = environment;
        _db.AppSettings.Update(row);
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Returns the year of the most recent transaction in the database.
    /// Falls back to the current calendar year if there are no transactions.
    /// This replaces the old static `output_year` setting per featureChanges.md.
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

        // Always include current year even if no transactions yet
        if (!yearsWithData.Contains(currentYear))
            yearsWithData.Insert(0, currentYear);

        return yearsWithData;
    }

    // ── GetAll: for backward compatibility with frontend dict consumers ───

    /// <summary>
    /// Returns settings as a dictionary for API consumers.
    /// output_year is now dynamic (derived from last transaction), not stored.
    /// </summary>
    public async Task<Dictionary<string, string>> GetAll()
    {
        var row = await GetRow();
        var outputYear = await GetOutputYear();
        return new Dictionary<string, string>
        {
            ["plaid_environment"] = row.PlaidEnvironment,
            ["output_year"] = outputYear.ToString()
        };
    }
}