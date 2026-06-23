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

    /// <summary>
    /// Returns current settings.
    /// - plaid_environment: read-only, from PLAID_ENV environment variable
    /// - output_year: read-only, derived from most recent transaction
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await _settings.GetAll());

    /// <summary>Returns the list of years available for filtering (up to 7 years of data).</summary>
    [HttpGet("years")]
    public async Task<IActionResult> AvailableYears() =>
        Ok(await _settings.GetAvailableYears());

    /// <summary>Returns the list of excluded category names.</summary>
    [HttpGet("excluded-categories")]
    public async Task<IActionResult> GetExcludedCategories() =>
        Ok(await _settings.GetExcludedCategories());

    /// <summary>Updates the list of excluded category names.</summary>
    [HttpPut("excluded-categories")]
    public async Task<IActionResult> SetExcludedCategories([FromBody] List<string> categories)
    {
        await _settings.SetExcludedCategories(categories);
        return Ok(await _settings.GetExcludedCategories());
    }

    /// <summary>Returns all distinct category names from transactions (for the UI picker).</summary>
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

    [HttpPut]
    public IActionResult Update()
    {
        return BadRequest(new
        {
            error = "Settings are managed via environment variables. " +
                    "Set PLAID_ENV to configure the Plaid environment (sandbox/development/production)."
        });
    }

    /// <summary>
    /// Finds and removes orphaned transactions and CSV mapping profiles whose
    /// AccountId does not match any LinkedAccount (by either Plaid account_id or
    /// PK) or ManualAccount. Returns counts of what was removed.
    /// </summary>
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