using Microsoft.EntityFrameworkCore;

public class SettingsService
{
    private readonly AppDbContext _db;

    public SettingsService(AppDbContext db)
    {
        _db = db;
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
}
