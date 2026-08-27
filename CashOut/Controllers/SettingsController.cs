using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/settings")]
public class SettingsController : ControllerBase
{
    private readonly SettingsService _settings;
    private readonly AppDbContext _db;

    public SettingsController(SettingsService settings, AppDbContext db)
    {
        _settings = settings;
        _db = db;
    }

    [HttpGet("years")]
    public async Task<IActionResult> AvailableYears() =>
        Ok(await _settings.GetAvailableYears());

    [HttpGet("excluded-categories")]
    public async Task<IActionResult> GetExcludedCategories() =>
        Ok(await _settings.GetExcludedCategories());

    [HttpGet("categories")]
    public async Task<IActionResult> GetAllCategories()
    {
        var categories = await _db.Transactions
            .Where(t => t.Category != null && t.Category != "")
            .Select(t => t.Category)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();
        return Ok(categories);
    }
}
