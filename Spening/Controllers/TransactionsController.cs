using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/transactions")]
public class TransactionsController : ControllerBase
{
    private readonly TransactionService _txns;
    private readonly SettingsService _settings;

    public TransactionsController(TransactionService txns, SettingsService settings)
    {
        _txns = txns;
        _settings = settings;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int? year,
        [FromQuery] string? accountId,
        [FromQuery] string? category)
    {
        var results = await _txns.Query(year, accountId, category);
        return Ok(results);
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
        // Returns total transactions processed (inserted + updated), not just new rows
        var count = await _txns.FetchAll();
        return Ok(new { written = count });
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] int? year)
    {
        // Always resolve a concrete year before passing to ExportCsv
        var resolvedYear = year ?? await _settings.GetOutputYear();
        var csv = await _txns.ExportCsv(resolvedYear);
        return File(csv, "text/csv", $"spening-{resolvedYear}.csv");
    }
}