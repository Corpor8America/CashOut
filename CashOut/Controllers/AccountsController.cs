using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/accounts")]
public class AccountsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly PlaidService _plaid;

    public AccountsController(AppDbContext db, PlaidService plaid)
    {
        _db = db;
        _plaid = plaid;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var accounts = await _db.LinkedAccounts
            .OrderBy(a => a.Institution)
            .ThenBy(a => a.Name)
            .Select(a => new
            {
                a.Id,
                a.AccountId,
                a.Mask,
                a.Name,
                a.Subtype,
                a.Institution,
                a.CreatedAt
                // AccessToken and ItemId intentionally excluded from response
            })
            .ToListAsync();

        return Ok(accounts);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Remove(Guid id)
    {
        var account = await _db.LinkedAccounts.FindAsync(id);
        if (account == null) return NotFound();

        // Pass ItemId so RemoveItem can group-delete by stable Plaid identifier.
        // RemoveItem handles DB deletion internally and will still delete locally
        // even if the Plaid revocation call fails.
        await _plaid.RemoveItem(account.AccessToken, account.ItemId);
        return NoContent();
    }
}