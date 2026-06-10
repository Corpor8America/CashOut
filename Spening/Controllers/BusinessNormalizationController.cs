using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/normalization")]
public class BusinessNormalizationController : ControllerBase
{
    private readonly BusinessNormalizationService _svc;

    public BusinessNormalizationController(BusinessNormalizationService svc) => _svc = svc;

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
    public async Task<IActionResult> ListMappings() =>
        Ok(await _svc.GetAllMappings());

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

    public record UpdateCategoryRequest(string? Category);
    public record CreateAliasRequest(string AliasName, string? Category);
    public record CreateMappingRequest(int RawBusinessId, int AliasId);
}