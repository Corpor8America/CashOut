using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/data-transfer")]
public class DataTransferController : ControllerBase
{
    private readonly DataTransferService _dataTransfer;

    public DataTransferController(DataTransferService dataTransfer) => _dataTransfer = dataTransfer;

    [HttpGet("export")]
    public async Task<IActionResult> Export()
    {
        var bytes = await _dataTransfer.ExportAsync();
        var fileName = $"cashout-backup-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip";
        return File(bytes, "application/zip", fileName);
    }

    [HttpPost("import")]
    [RequestSizeLimit(100L * 1024 * 1024)]
    public async Task<IActionResult> Import([FromForm] IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Select a .zip backup file to import." });
        if (!file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "The backup file must be a .zip file." });

        try
        {
            using var stream = file.OpenReadStream();
            var result = await _dataTransfer.ImportAsync(stream);
            return Ok(result);
        }
        catch (DataTransferException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
