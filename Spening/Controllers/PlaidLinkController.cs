using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/plaid")]
public class PlaidLinkController : ControllerBase
{
    private readonly PlaidService _plaid;

    public PlaidLinkController(PlaidService plaid) => _plaid = plaid;

    /// <summary>Step 1: generate a link_token for the browser to initialise Plaid Link.</summary>
    [HttpPost("link-token")]
    public async Task<IActionResult> CreateLinkToken()
    {
        var token = await _plaid.CreateLinkToken();
        return Ok(new { link_token = token });
    }

    /// <summary>Step 2: exchange the public_token the browser received from Plaid Link.</summary>
    [HttpPost("exchange")]
    public async Task<IActionResult> Exchange([FromBody] ExchangeRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.PublicToken))
            return BadRequest(new { error = "public_token is required" });

        var accounts = await _plaid.ExchangeAndPersist(req.PublicToken);

        return Ok(accounts.Select(a => new
        {
            a.Id,
            a.Name,
            a.Mask,
            a.Subtype,
            a.Institution
        }));
    }

    public record ExchangeRequest(string PublicToken);
}
