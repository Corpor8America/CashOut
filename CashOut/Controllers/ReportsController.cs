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
        [FromQuery] int? year, [FromQuery] int? month, [FromQuery] string? format)
    {
        if (format == "csv")
            return File(await _reports.CategoryCsv(year, month), "text/csv", "category.csv");
        return Ok(await _reports.GetByCategory(year, month));
    }

    [HttpGet("income")]
    public async Task<IActionResult> Income(
        [FromQuery] int? year, [FromQuery] int? month, [FromQuery] string? format)
    {
        if (format == "csv")
            return File(await _reports.IncomeCsv(year, month), "text/csv", "income.csv");
        return Ok(await _reports.GetIncome(year, month));
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
        [FromQuery] int? month = null,
        [FromQuery] string? format = null)
    {
        if (format == "csv")
            return File(await _reports.MerchantsCsv(topN, year, month), "text/csv", "merchants.csv");
        return Ok(await _reports.GetTopMerchants(topN, year, month));
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

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(
        [FromQuery] int? year, [FromQuery] int? month, [FromQuery] string? format)
    {
        if (format == "csv")
            return File(await _reports.ExecutiveSummaryCsv(year, month), "text/csv", "executive-summary.csv");
        return Ok(await _reports.GetExecutiveSummary(year, month));
    }

    [HttpGet("cashflow")]
    public async Task<IActionResult> CashFlow(
        [FromQuery] int? year, [FromQuery] string? format)
    {
        if (format == "csv")
            return File(await _reports.CashFlowCsv(year), "text/csv", "cashflow.csv");
        return Ok(await _reports.GetCashFlow(year));
    }

    [HttpGet("category-summary")]
    public async Task<IActionResult> CategorySummary(
        [FromQuery] int year, [FromQuery] int month,
        [FromQuery] string? accountId)
    {
        return Ok(await _reports.GetCategorySummary(year, month, accountId));
    }
}
