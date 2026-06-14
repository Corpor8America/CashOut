using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/csv-import")]
public class CsvImportController : ControllerBase
{
    private readonly CsvImportService _csv;

    public CsvImportController(CsvImportService csv) => _csv = csv;

    /// <summary>Returns the current mapping profile for an account (if any).</summary>
    [HttpGet("{accountId}/profile")]
    public async Task<IActionResult> GetProfile(string accountId)
    {
        var profile = await _csv.GetCurrentProfile(accountId);
        if (profile == null) return NotFound();
        return Ok(profile);
    }

    /// <summary>Saves a new mapping profile version for an account.</summary>
    [HttpPost("{accountId}/profile")]
    public async Task<IActionResult> SaveProfile(
        string accountId, [FromBody] CsvMappingProfile profile)
    {
        var saved = await _csv.SaveProfile(accountId, profile);
        return Ok(saved);
    }

    /// <summary>
    /// Parses a CSV file upload and returns headers + 5-row preview.
    /// skipTop and skipBottom are applied before selecting the header row.
    /// </summary>
    [HttpPost("{accountId}/preview")]
    public async Task<IActionResult> Preview(
        string accountId,
        [FromForm] IFormFile file,
        [FromQuery] int skipTop = 0,
        [FromQuery] int skipBottom = 0)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded." });

        using var reader = new System.IO.StreamReader(file.OpenReadStream());
        var content = await reader.ReadToEndAsync();
        var preview = _csv.Preview(content, skipTop, skipBottom);
        return Ok(preview);
    }

    /// <summary>Imports a CSV file using the account's current mapping profile.</summary>
    [HttpPost("{accountId}/import")]
    public async Task<IActionResult> Import(string accountId, [FromForm] IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded." });

        var profile = await _csv.GetCurrentProfile(accountId);
        if (profile == null)
            return BadRequest(new { error = "No mapping profile found for this account. Please map columns first." });

        using var reader = new System.IO.StreamReader(file.OpenReadStream());
        var content = await reader.ReadToEndAsync();

        try
        {
            var result = await _csv.Import(accountId, content, profile);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Exports skipped rows from the most recent import as a downloadable CSV.</summary>
    [HttpPost("{accountId}/skipped-export")]
    public IActionResult ExportSkipped([FromBody] List<SkippedRow> skippedRows)
    {
        var sb = new System.Text.StringBuilder("Row,RawData,Reason\n");
        foreach (var row in skippedRows)
            sb.AppendLine($"{row.RowNumber},{EscCsv(row.RawData)},{EscCsv(row.Reason)}");
        return File(System.Text.Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "skipped-rows.csv");
    }

    private static string EscCsv(string s) =>
        s.Contains(',') || s.Contains('"') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
}