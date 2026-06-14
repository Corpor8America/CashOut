using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/settings")]
public class SettingsController : ControllerBase
{
    private readonly SettingsService _settings;

    public SettingsController(SettingsService settings) => _settings = settings;

    /// <summary>
    /// Returns current settings.
    /// - plaid_environment: read-only, from PLAID_ENV environment variable
    /// - output_year: read-only, derived from most recent transaction
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await _settings.GetAll());

    /// <summary>Returns the list of years available for filtering (up to 7 years of data).</summary>
    [HttpGet("years")]
    public async Task<IActionResult> AvailableYears() =>
        Ok(await _settings.GetAvailableYears());

    [HttpPut]
    public IActionResult Update()
    {
        // All settings are now read-only via the API.
        // plaid_environment: set via PLAID_ENV environment variable
        // output_year: derived from most recent transaction
        return BadRequest(new
        {
            error = "Settings are managed via environment variables. " +
                    "Set PLAID_ENV to configure the Plaid environment (sandbox/development/production)."
        });
    }
}