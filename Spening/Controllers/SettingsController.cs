using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/settings")]
public class SettingsController : ControllerBase
{
    private readonly SettingsService _settings;

    public SettingsController(SettingsService settings) => _settings = settings;

    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await _settings.GetAll());

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] Dictionary<string, string> updates)
    {
        var allowed = new[] { "plaid_environment", "output_year" };
        foreach (var (key, value) in updates)
        {
            if (!allowed.Contains(key))
                return BadRequest(new { error = $"Unknown setting key: {key}" });

            if (key == "plaid_environment" &&
                !new[] { "sandbox", "development", "production" }.Contains(value))
                return BadRequest(new { error = $"Invalid environment: {value}" });

            if (key == "output_year" && !int.TryParse(value, out _))
                return BadRequest(new { error = "output_year must be a number" });

            await _settings.Set(key, value);
        }
        return Ok(await _settings.GetAll());
    }
}
