using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/transactions")]
public class TransactionsController : ControllerBase
{
    private readonly TransactionService _txns;
    private readonly SettingsService _settings;
    private readonly AppDbContext _db;

    public TransactionsController(TransactionService txns, SettingsService settings, AppDbContext db)
    {
        _txns = txns;
        _settings = settings;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int? year,
        [FromQuery] int? month,
        [FromQuery] string? accountId,
        [FromQuery] List<string>? category)
    {
        var results = await _txns.Query(year, month, accountId, category);

        var linkedNames = await _db.LinkedAccounts
            .ToDictionaryAsync(a => a.AccountId, a => a.Name);
        var manualNames = await _db.ManualAccounts
            .ToDictionaryAsync(a => a.Id.ToString(), a => a.Name);

        var response = results.Select(t => new
        {
            t.TransactionId,
            t.AccountId,
            AccountName = linkedNames.GetValueOrDefault(t.AccountId)
                          ?? manualNames.GetValueOrDefault(t.AccountId)
                          ?? t.AccountId,
            t.Date,
            t.Name,
            t.Credit,
            t.Debit,
            t.Amount,
            t.Category
        });

        return Ok(response);
    }

    [HttpPost("sync")]
    public async Task<IActionResult> Sync()
    {
        var (added, removed) = await _txns.SyncAll();
        return Ok(new { added, removed });
    }

    [HttpPost("fetch")]
    public async Task<IActionResult> Fetch()
    {
        var count = await _txns.FetchAll();
        return Ok(new { written = count });
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] int? year)
    {
        var resolvedYear = year ?? await _settings.GetOutputYear();
        var csv = await _txns.ExportCsv(resolvedYear);
        return File(csv, "text/csv", $"cashout-{resolvedYear}.csv");
    }

    [HttpPatch("{transactionId}/category")]
    public async Task<IActionResult> UpdateCategory(
        string transactionId, [FromBody] UpdateCategoryRequest req)
    {
        var updated = await _txns.UpdateCategory(transactionId, req.Category ?? "");
        if (updated == null) return NotFound();
        return Ok(new { updated.TransactionId, updated.Category });
    }

    public record UpdateCategoryRequest(string? Category);
}