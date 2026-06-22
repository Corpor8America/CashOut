using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/normalization")]
public class BusinessNormalizationController : ControllerBase
{
    private readonly MerchantNormalizationService _svc;
    private readonly AppDbContext _db;

    public BusinessNormalizationController(MerchantNormalizationService svc, AppDbContext db)
        => (_svc, _db) = (svc, db);

    // ── Aliases ───────────────────────────────────────────────────────────

    [HttpGet("aliases")]
    public async Task<IActionResult> ListAliases()
    {
        var aliases = await _svc.GetAllAliases();
        return Ok(aliases.Select(a => new
        {
            a.Id,
            a.AliasName,
            a.Category,
            Patterns = a.Patterns.Select(p => new
            {
                p.Id,
                p.Pattern,
                MatchType = p.MatchType.ToString()
            })
        }));
    }

    [HttpPost("aliases")]
    public async Task<IActionResult> CreateAlias([FromBody] CreateAliasRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.AliasName))
            return BadRequest(new { error = "AliasName is required." });
        var (alias, matched) = await _svc.CreateAlias(req.AliasName, req.Category ?? "");
        return Ok(new { alias.Id, alias.AliasName, alias.Category, matched });
    }

    [HttpPatch("aliases/{id:int}/category")]
    public async Task<IActionResult> UpdateAliasCategory(
        int id, [FromBody] UpdateCategoryRequest req)
    {
        try
        {
            await _svc.UpdateAliasCategory(id, req.Category ?? "");
            return Ok();
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpPatch("aliases/{id:int}/name")]
    public async Task<IActionResult> UpdateAliasName(
        int id, [FromBody] UpdateAliasNameRequest req)
    {
        try
        {
            await _svc.UpdateAliasName(id, req.AliasName ?? "");
            return Ok();
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpDelete("aliases/{id:int}")]
    public async Task<IActionResult> DeleteAlias(int id)
    {
        try
        {
            var reprocessed = await _svc.DeleteAlias(id);
            return Ok(new { reprocessed });
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    // ── Patterns ──────────────────────────────────────────────────────────

    [HttpPost("aliases/{aliasId:int}/patterns")]
    public async Task<IActionResult> AddPattern(
        int aliasId, [FromBody] AddPatternRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Pattern))
            return BadRequest(new { error = "Pattern is required." });

        if (!Enum.TryParse<AliasPatternMatchType>(req.MatchType, ignoreCase: true, out var matchType))
            return BadRequest(new { error = $"Invalid match type: {req.MatchType}. Use Contains, StartsWith, or Regex." });

        try
        {
            var pattern = await _svc.AddPattern(aliasId, req.Pattern, matchType);
            return Ok(new { pattern.Id, pattern.AliasId, pattern.Pattern, MatchType = pattern.MatchType.ToString() });
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpDelete("patterns/{patternId:int}")]
    public async Task<IActionResult> RemovePattern(int patternId)
    {
        await _svc.RemovePattern(patternId);
        return NoContent();
    }

    [HttpPost("aliases/test")]
    public async Task<IActionResult> TestPattern([FromBody] TestPatternRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.RawInput))
            return BadRequest(new { error = "RawInput is required." });
        var result = await _svc.TestPattern(req.RawInput);
        return Ok(result);
    }

    // ── Raw Businesses ────────────────────────────────────────────────────

    [HttpGet("businesses")]
    public async Task<IActionResult> ListBusinesses([FromQuery] bool unmappedOnly = false)
    {
        var businesses = unmappedOnly
            ? await _svc.GetUnmappedBusinesses()
            : await _svc.GetAllRawBusinesses();

        return Ok(businesses.Select(b => new
        {
            b.Id,
            b.RawName,
            b.RawNameNormalized,
            b.CategoryRaw,
            b.IsMapped,
            b.CreatedAt
        }));
    }

    // ── Mappings ──────────────────────────────────────────────────────────

    [HttpGet("mappings")]
    public async Task<IActionResult> ListMappings()
    {
        var maps = await _svc.GetAllAliases();
        var rawMaps = await _db.RawBusinessAliasMaps
            .Join(_db.RawBusinesses, m => m.RawBusinessId, b => b.Id,
                (m, b) => new { m.Id, m.RawBusinessId, b.RawName, b.RawNameNormalized, m.AliasId })
            .ToListAsync();

        return Ok(rawMaps.Select(m => new
        {
            m.Id,
            m.RawBusinessId,
            RawBusinessName = m.RawName,
            RawBusinessNormalized = m.RawNameNormalized,
            m.AliasId,
            AliasName = maps.FirstOrDefault(a => a.Id == m.AliasId)?.AliasName ?? ""
        }));
    }

    [HttpPost("mappings")]
    public async Task<IActionResult> CreateMapping([FromBody] CreateMappingRequest req)
    {
        try
        {
            await _svc.MapRawToAlias(req.RawBusinessId, req.AliasId);
            return Ok();
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }

    [HttpDelete("mappings/{rawBusinessId:int}")]
    public async Task<IActionResult> DeleteMapping(int rawBusinessId)
    {
        await _svc.UnmapRawBusiness(rawBusinessId);
        return NoContent();
    }

    [HttpPost("retroactive-map")]
    public async Task<IActionResult> RetroactiveMap()
    {
        var count = await _svc.RetroactivelyMap();
        return Ok(new { matched = count, mapped = count });
    }

    // ── Request types ─────────────────────────────────────────────────────

    public record CreateAliasRequest(string AliasName, string? Category);
    public record UpdateAliasNameRequest(string? AliasName);
    public record UpdateCategoryRequest(string? Category);
    public record AddPatternRequest(string Pattern, string MatchType);
    public record CreateMappingRequest(int RawBusinessId, int AliasId);
    public record TestPatternRequest(string RawInput);
}