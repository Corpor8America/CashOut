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

    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await _settings.GetAll());

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

    [HttpPost("cleanup")]
    public async Task<IActionResult> CleanupOrphans()
    {
        var linked = await _db.LinkedAccounts.ToListAsync();
        var manual = await _db.ManualAccounts.ToListAsync();

        var validIds = new HashSet<string>();
        foreach (var la in linked)
        {
            validIds.Add(la.AccountId);
            validIds.Add(la.Id.ToString());
        }
        foreach (var ma in manual)
            validIds.Add(ma.Id.ToString());

        var orphanTxns = await _db.Transactions
            .Where(t => !validIds.Contains(t.AccountId))
            .ToListAsync();
        var orphanProfiles = await _db.CsvMappingProfiles
            .Where(p => !validIds.Contains(p.AccountId))
            .ToListAsync();

        var txnCount = orphanTxns.Count;
        var profileCount = orphanProfiles.Count;

        _db.Transactions.RemoveRange(orphanTxns);
        _db.CsvMappingProfiles.RemoveRange(orphanProfiles);
        await _db.SaveChangesAsync();

        return Ok(new { transactionsRemoved = txnCount, profilesRemoved = profileCount });
    }
}
