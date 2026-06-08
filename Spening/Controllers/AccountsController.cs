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
                // AccessToken intentionally excluded from response
            })
            .ToListAsync();

        return Ok(accounts);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Remove(Guid id)
    {
        var account = await _db.LinkedAccounts.FindAsync(id);
        if (account == null) return NotFound();

        await _plaid.RemoveItem(account.AccessToken);
        // RemoveItem handles DB deletion internally
        return NoContent();
    }
}
