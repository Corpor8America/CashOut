using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/settings")]
public class SettingsController : ControllerBase
{
    private readonly SettingsService _settings;

    public SettingsController(SettingsService settings) => _settings = settings;

    /// <summary>
    /// Returns current settings. output_year is dynamic (derived from last transaction)
    /// and is read-only — it cannot be set via PUT.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await _settings.GetAll());

    /// <summary>Returns the list of years available for filtering (up to 7 years of data).</summary>
    [HttpGet("years")]
    public async Task<IActionResult> AvailableYears() =>
        Ok(await _settings.GetAvailableYears());

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] Dictionary<string, string> updates)
    {
        foreach (var (key, value) in updates)
        {
            switch (key)
            {
                case "plaid_environment":
                    if (!new[] { "sandbox", "development", "production" }.Contains(value))
                        return BadRequest(new { error = $"Invalid environment: {value}" });
                    await _settings.SetPlaidEnvironment(value);
                    break;

                case "output_year":
                    // output_year is now dynamic and cannot be set manually
                    return BadRequest(new
                    {
                        error = "output_year is read-only. It is derived from your most recent transaction."
                    });

                default:
                    return BadRequest(new { error = $"Unknown setting key: {key}" });
            }
        }
        return Ok(await _settings.GetAll());
    }
}