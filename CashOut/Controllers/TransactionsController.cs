using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/transactions")]
public class TransactionsController : ControllerBase
{
    private readonly TransactionService _txns;
    private readonly SettingsService _settings;
    private readonly AppDbContext _db;

    public TransactionsController(
        TransactionService txns,
        SettingsService settings,
        AppDbContext db)
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
        [FromQuery] List<string>? category,
        [FromQuery] List<int>? effectiveCategoryId)
    {
        var results = await _txns.Query(year, month, accountId, category,
            effectiveCategoryIds: effectiveCategoryId);

        var accountNames = await _db.Accounts
            .ToDictionaryAsync(a => a.Id.ToString(), a => a.Name);

        var response = results.Select(t => new
        {
            t.TransactionId,
            t.AccountId,
            AccountName = accountNames.GetValueOrDefault(t.AccountId) ?? t.AccountId,
            t.Date,
            t.Name,
            t.Credit,
            t.Debit,
            t.Amount,
            t.Category,
            EffectiveCategoryId = t.CategoryId,
            EffectiveCategoryName = t.EffectiveCategory?.Name ?? "",
            IsManualAssignment = t.CategoryId.HasValue && t.CategoryRuleId == null,
        });

        return Ok(response);
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

    [HttpPatch("{transactionId}/effective-category")]
    public async Task<IActionResult> AssignEffectiveCategory(
        string transactionId,
        [FromBody] AssignEffectiveCategoryRequest req)
    {
        var updated = await _txns.AssignEffectiveCategory(
            transactionId, req.CategoryId, req.CategoryRuleId);
        if (updated == null) return NotFound();
        return Ok(new
        {
            updated.TransactionId,
            EffectiveCategoryId = updated.CategoryId,
            EffectiveCategoryName = updated.EffectiveCategory?.Name ?? "",
        });
    }

    public record UpdateCategoryRequest(string? Category);
    public record AssignEffectiveCategoryRequest(int? CategoryId, int? CategoryRuleId);
}
