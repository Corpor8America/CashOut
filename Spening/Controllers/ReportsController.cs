using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly ReportService _reports;
    private readonly SettingsService _settings;

    public ReportsController(ReportService reports, SettingsService settings)
    {
        _reports = reports;
        _settings = settings;
    }

    [HttpGet("monthly")]
    public async Task<IActionResult> Monthly(
        [FromQuery] int? year, [FromQuery] string? format)
    {
        if (format == "csv")
            return File(await _reports.MonthlyCsv(year), "text/csv", "monthly.csv");
        return Ok(await _reports.GetMonthly(year));
    }

    [HttpGet("category")]
    public async Task<IActionResult> Category(
        [FromQuery] int? year, [FromQuery] string? format)
    {
        if (format == "csv")
            return File(await _reports.CategoryCsv(year), "text/csv", "category.csv");
        return Ok(await _reports.GetByCategory(year));
    }

    [HttpGet("pivot")]
    public async Task<IActionResult> Pivot(
        [FromQuery] int? year, [FromQuery] string? format)
    {
        // Pivot CSV is complex — skip for now, return JSON only
        if (format == "csv")
            return BadRequest(new { error = "Pivot CSV export not supported" });
        return Ok(await _reports.GetPivot(year));
    }

    [HttpGet("merchants")]
    public async Task<IActionResult> Merchants(
        [FromQuery] int topN = 10, [FromQuery] int? year = null,
        [FromQuery] string? format = null)
    {
        if (format == "csv")
            return File(await _reports.MerchantsCsv(topN, year), "text/csv", "merchants.csv");
        return Ok(await _reports.GetTopMerchants(topN, year));
    }

    [HttpGet("largest")]
    public async Task<IActionResult> Largest(
        [FromQuery] int topN = 10, [FromQuery] int? year = null,
        [FromQuery] string? format = null)
    {
        if (format == "csv")
            return File(await _reports.LargestCsv(topN, year), "text/csv", "largest.csv");
        return Ok(await _reports.GetLargest(topN, year));
    }
}
