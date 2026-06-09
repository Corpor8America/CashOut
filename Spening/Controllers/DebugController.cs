using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/debug")]
public class DebugController : ControllerBase
{
    private readonly IConfiguration _config;

    public DebugController(IConfiguration config) => _config = config;

    [HttpGet("env")]
    public IActionResult Env()
    {
        var clientId = _config["PLAID_CLIENT_ID"]
            ?? Environment.GetEnvironmentVariable("PLAID_CLIENT_ID") ?? "";
        var secret = _config["PLAID_SANDBOX_SECRET"]
            ?? Environment.GetEnvironmentVariable("PLAID_SANDBOX_SECRET") ?? "";

        static string Mask(string s) => s.Length <= 8 ? new string('*', s.Length)
            : s[..4] + new string('*', s.Length - 8) + s[^4..];

        return Ok(new
        {
            plaid_client_id = Mask(clientId),
            plaid_client_id_length = clientId.Length,
            plaid_client_id_has_whitespace = clientId != clientId.Trim(),
            plaid_sandbox_secret = Mask(secret),
            plaid_sandbox_secret_length = secret.Length,
            plaid_sandbox_secret_has_whitespace = secret != secret.Trim(),
        });
    }
}