using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/category-rules")]
public class CategoryRulesController : ControllerBase
{
    private readonly CategoryRuleService _rules;
    private readonly AppDbContext _db;

    public CategoryRulesController(CategoryRuleService rules, AppDbContext db)
    {
        _rules = rules;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var rules = await _rules.GetAll();
        var names = await _db.Transactions.Select(t => t.Name).ToListAsync();

        var response = rules.Select(r => new
        {
            r.Id,
            r.Pattern,
            CategoryName = r.Category.Name,
            r.CategoryId,
            MatchCount = names.Count(n => n.Contains(r.Pattern, StringComparison.OrdinalIgnoreCase)),
        });

        return Ok(response);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UpsertRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Pattern) || req.CategoryId == null)
            return BadRequest("Pattern and CategoryId are required.");
        var rule = await _rules.Create(req.Pattern, req.CategoryId.Value);
        return Ok(rule);
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpsertRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Pattern) || req.CategoryId == null)
            return BadRequest("Pattern and CategoryId are required.");
        var rule = await _rules.Update(id, req.Pattern, req.CategoryId.Value);
        return rule == null ? NotFound() : Ok(rule);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await _rules.Delete(id);
        return deleted ? Ok() : NotFound();
    }

    [HttpGet("suggest-pattern")]
    public IActionResult SuggestPattern([FromQuery] string transactionName)
        => Ok(new { Pattern = CategoryRuleService.SuggestPattern(transactionName) });

    public record UpsertRequest(string Pattern, int? CategoryId);
}
