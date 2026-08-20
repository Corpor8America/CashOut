using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/plaid")]
public class PlaidLinkController : ControllerBase
{
    private readonly PlaidService _plaid;

    public PlaidLinkController(PlaidService plaid) => _plaid = plaid;

    [HttpPost("link-token")]
    public async Task<IActionResult> CreateLinkToken()
    {
        var token = await _plaid.CreateLinkToken();
        return Ok(new { link_token = token });
    }

    [HttpPost("exchange")]
    public async Task<IActionResult> Exchange([FromBody] ExchangeRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.PublicToken))
            return BadRequest(new { error = "public_token is required" });

        var accounts = await _plaid.ExchangeAndPersist(req.PublicToken);

        if (req.ManualAccountId.HasValue && accounts.Count > 0)
        {
            var targetLinkedAccount = accounts.First();
            await _plaid.MergeManualAccount(req.ManualAccountId.Value, targetLinkedAccount.AccountId);
        }

        return Ok(accounts.Select(a => new
        {
            a.Id,
            a.Name,
            a.Mask,
            a.Subtype,
            a.Institution
        }));
    }

    public record ExchangeRequest(string PublicToken, Guid? ManualAccountId);
}
