using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly ReportService _reports;

    public ReportsController(ReportService reports)
    {
        _reports = reports;
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
        [FromQuery] int? year, [FromQuery] int? month,
        [FromQuery] int? fromYear, [FromQuery] int? fromMonth,
        [FromQuery] int? toYear, [FromQuery] int? toMonth,
        [FromQuery] string? accountId,
        [FromQuery] string? format)
    {
        if (fromYear.HasValue || fromMonth.HasValue || toYear.HasValue || toMonth.HasValue)
        {
            if (format == "csv")
                return File(await _reports.CategoryDetailCsv(fromYear, fromMonth, toYear, toMonth, accountId),
                    "text/csv", "category.csv");
            return Ok(await _reports.GetCategoryDetail(fromYear, fromMonth, toYear, toMonth, accountId));
        }

        if (format == "csv")
            return File(await _reports.CategoryCsv(year, month), "text/csv", "category.csv");
        return Ok(await _reports.GetByCategory(year, month));
    }

    [HttpGet("cashflow")]
    public async Task<IActionResult> CashFlow(
        [FromQuery] int? year, [FromQuery] string? accountId,
        [FromQuery] int? fromYear, [FromQuery] int? fromMonth,
        [FromQuery] int? toYear, [FromQuery] int? toMonth,
        [FromQuery] string? format)
    {
        if (format == "csv")
            return File(await _reports.CashFlowCsv(year, accountId, fromYear, fromMonth, toYear, toMonth),
                "text/csv", "cashflow.csv");
        return Ok(await _reports.GetCashFlow(year, accountId, fromYear, fromMonth, toYear, toMonth));
    }
}
