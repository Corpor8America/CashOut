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
        var linked = await _db.LinkedAccounts
            .ToDictionaryAsync(a => a.AccountId, a => a.Name);

        var manual = await _db.ManualAccounts
            .ToDictionaryAsync(a => a.Id.ToString(), a => a.Name);

        var accounts = await _db.Transactions
            .Select(t => t.AccountId)
            .Distinct()
            .OrderBy(id => id)
            .Select(id => new
            {
                Id = id,
                Name = linked.ContainsKey(id) ? linked[id]
                     : manual.ContainsKey(id) ? manual[id]
                     : $"Account {id.Substring(0, 8)}"
            })
            .ToListAsync();

        return Ok(accounts);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Remove(Guid id)
    {
        var account = await _db.LinkedAccounts.FindAsync(id);
        if (account == null) return NotFound();

        await _plaid.RemoveItem(account.AccessToken, account.ItemId);
        return NoContent();
    }
}
