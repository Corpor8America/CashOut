using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/accounts/{accountId}/reports")]
public class AccountReportsController : ControllerBase
{
    private readonly AccountReportService _reports;

    public AccountReportsController(AccountReportService reports) => _reports = reports;

    [HttpGet("cashflow")]
    public async Task<IActionResult> CashFlow(string accountId, [FromQuery] int? year) =>
        Ok(await _reports.GetCashFlow(accountId, year));

    [HttpGet("category")]
    public async Task<IActionResult> Category(
        string accountId, [FromQuery] int? year, [FromQuery] int? month) =>
        Ok(await _reports.GetByCategory(accountId, year, month));
}
