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

        return "sandbox";
    }

    public async Task<int> GetOutputYear()
    {
        var maxDate = await _db.Transactions
            .OrderByDescending(t => t.Date)
            .Select(t => (DateOnly?)t.Date)
            .FirstOrDefaultAsync();

        return maxDate?.Year ?? DateTime.UtcNow.Year;
    }

    public async Task<List<int>> GetAvailableYears()
    {
        var yearsWithData = await _db.Transactions
            .Select(t => t.Date.Year)
            .Distinct()
            .OrderByDescending(y => y)
            .ToListAsync();

        return yearsWithData;
    }

    public Task<List<string>> GetExcludedCategories()
    {
        return Task.FromResult(new List<string>());
    }

    public async Task<Dictionary<string, string>> GetAll()
    {
        var outputYear = await GetOutputYear();
        return new Dictionary<string, string>
        {
            ["plaid_environment"] = GetPlaidEnvironment(),
            ["output_year"] = outputYear.ToString()
        };
    }
}
