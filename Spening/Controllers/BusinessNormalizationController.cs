using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/normalization")]
public class BusinessNormalizationController : ControllerBase
{
    private readonly BusinessNormalizationService _svc;

    private readonly AppDbContext _db;
    public BusinessNormalizationController(BusinessNormalizationService svc, AppDbContext db)
        => (_svc, _db) = (svc, db);

    [HttpGet("businesses")]
    public async Task<IActionResult> ListBusinesses() =>
        Ok(await _svc.GetAllRawBusinesses());

    [HttpPatch("businesses/{id:int}/category")]
    public async Task<IActionResult> UpdateCategory(int id, [FromBody] UpdateCategoryRequest req)
    {
        try
        {
            await _svc.UpdateRawBusinessCategory(id, req.Category ?? "");
            return Ok();
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpGet("aliases")]
    public async Task<IActionResult> ListAliases() =>
        Ok(await _svc.GetAllAliases());

    [HttpPost("aliases")]
    public async Task<IActionResult> CreateAlias([FromBody] CreateAliasRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.AliasName))
            return BadRequest(new { error = "AliasName is required." });
        var alias = await _svc.CreateAlias(req.AliasName, req.Category ?? "");
        return Ok(alias);
    }

    [HttpGet("mappings")]
    public async Task<IActionResult> ListMappings()
    {
        var maps = await _svc.GetAllMappings();
        return Ok(maps.Select(m => new
        {
            m.Id,
            RawBusinessId = m.RawBusinessId,
            RawBusinessName = m.RawBusiness.RawName,
            AliasId = m.AliasId,
            AliasName = m.Alias.AliasName
        }));
    }

    [HttpPost("mappings")]
    public async Task<IActionResult> CreateMapping([FromBody] CreateMappingRequest req)
    {
        await _svc.MapRawToAlias(req.RawBusinessId, req.AliasId);
        return Ok();
    }

    [HttpDelete("mappings/{rawBusinessId:int}")]
    public async Task<IActionResult> DeleteMapping(int rawBusinessId)
    {
        await _svc.RemoveMapping(rawBusinessId);
        return NoContent();
    }

    [HttpPatch("aliases/{id:int}/category")]
    public async Task<IActionResult> UpdateAliasCategory(int id, [FromBody] UpdateCategoryRequest req)
    {
        var alias = await _db.BusinessAliases.FindAsync(id);
        if (alias == null) return NotFound();

        alias.Category = req.Category ?? "";
        await _db.SaveChangesAsync();
        return Ok(alias);
    }

    public record UpdateCategoryRequest(string? Category);
    public record CreateAliasRequest(string AliasName, string? Category);
    public record CreateMappingRequest(int RawBusinessId, int AliasId);
}