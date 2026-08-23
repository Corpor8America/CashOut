using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/csv-import")]
public class CsvImportController : ControllerBase
{
    private readonly CsvImportService _csv;
    private readonly PdfImportService _pdf;

    public CsvImportController(CsvImportService csv, PdfImportService pdf)
    {
        _csv = csv;
        _pdf = pdf;
    }

    [HttpGet("{accountId}/profile")]
    public async Task<IActionResult> GetProfile(string accountId)
    {
        var profile = await _csv.GetCurrentProfile(accountId);
        if (profile == null) return NotFound();
        return Ok(profile);
    }

    [HttpPost("{accountId}/profile")]
    public async Task<IActionResult> SaveProfile(
        string accountId, [FromBody] CsvMappingProfile profile)
    {
        var saved = await _csv.SaveProfile(accountId, profile);
        return Ok(saved);
    }

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

    [HttpPost("{accountId}/pdf-preview")]
    public async Task<IActionResult> PdfPreview(
        string accountId,
        [FromForm] IFormFile file,
        [FromQuery] string? pages = null,
        [FromQuery] decimal? dateColEnd = null,
        [FromQuery] decimal? amountColStart = null,
        [FromQuery] bool joinCont = true,
        [FromQuery] string? rowRegex = null)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded." });

        try
        {
            using var ms = new System.IO.MemoryStream();
            await file.CopyToAsync(ms);
            var pdfBytes = ms.ToArray();

            int[]? pageList = ParsePages(pages);

            var (rawText, csvContent, skippedLines) = _pdf.ExtractCsvDebug(
                pageList, pdfBytes, dateColEnd, amountColStart, joinCont, rowRegex);

            if (string.IsNullOrWhiteSpace(csvContent.Replace("Date,Description,Amount", "").Trim()))
                return BadRequest(new { error = "No transactions could be extracted from this PDF. The file may be a scanned image or use an unsupported format." });

            var preview = _csv.Preview(csvContent);
            return Ok(new { csvContent, preview, debug = new { rawText, skippedLines } });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = "Failed to extract transactions from PDF: " + ex.Message });
        }
    }

    [HttpPost("pdf-debug")]
    public async Task<IActionResult> PdfDebug(
        [FromForm] IFormFile file,
        [FromQuery] string? pages = null,
        [FromQuery] decimal? dateColEnd = null,
        [FromQuery] decimal? amountColStart = null,
        [FromQuery] bool joinCont = true,
        [FromQuery] string? rowRegex = null)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded." });

        try
        {
            using var ms = new System.IO.MemoryStream();
            await file.CopyToAsync(ms);
            var pdfBytes = ms.ToArray();

            int[]? pageList = ParsePages(pages);

            var (rawText, csvContent, skippedLines) = _pdf.ExtractCsvDebug(
                pageList, pdfBytes, dateColEnd, amountColStart, joinCont, rowRegex);
            return Ok(new { rawText, csvContent, skippedLines });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = "Failed to extract from PDF: " + ex.Message });
        }
    }

    private static int[]? ParsePages(string? pages)
    {
        if (string.IsNullOrWhiteSpace(pages)) return null;

        var result = new List<int>();
        foreach (var part in pages.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var seg = part.Trim();
            var dash = seg.IndexOf('-');
            if (dash > 0 && int.TryParse(seg[..dash].Trim(), out var start) && int.TryParse(seg[(dash + 1)..].Trim(), out var end))
            {
                for (var n = start; n <= end; n++) result.Add(n);
            }
            else if (int.TryParse(seg, out var n))
            {
                result.Add(n);
            }
        }

        return result.Count > 0 ? result.ToArray() : null;
    }

    [HttpPost("{accountId}/import")]
    public async Task<IActionResult> Import(
        string accountId,
        [FromForm] IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded." });

        var profile = await _csv.GetCurrentProfile(accountId);
        if (profile == null)
            return BadRequest(new { error = "No mapping profile found for this account. Please map columns first." });

        try
        {
            using var ms = new System.IO.MemoryStream();
            await file.CopyToAsync(ms);
            var bytes = ms.ToArray();

            string content;
            if (file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                var (_, csvContent, _) = _pdf.ExtractCsvDebug(pdfPages: null, bytes);
                content = csvContent;
            }
            else
            {
                content = System.Text.Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
            }

            var result = await _csv.Import(accountId, content, profile);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = "Import failed: " + ex.Message });
        }
    }

    [HttpPost("{accountId}/skipped-export")]
    public IActionResult ExportSkipped([FromBody] List<SkippedRow> skippedRows)
    {
        var sb = new System.Text.StringBuilder("Row,RawData,Reason\n");
        foreach (var row in skippedRows)
            sb.AppendLine($"{row.RowNumber},{EscCsv(row.RawData)},{EscCsv(row.Reason)}");
        return File(System.Text.Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "skipped-rows.csv");
    }

    private static string EscCsv(string s) =>
        s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r')
            ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
}
