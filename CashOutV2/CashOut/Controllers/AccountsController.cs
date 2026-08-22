using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/accounts")]
public class AccountsController : ControllerBase
{
    private readonly AppDbContext _db;

    public AccountsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var accounts = await _db.Accounts
            .OrderBy(a => a.Name)
            .ToListAsync();
        return Ok(accounts);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAccountRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { error = "Name is required." });

        var account = new Account
        {
            Id = Guid.NewGuid(),
            Name = req.Name.Trim(),
            Description = req.Description?.Trim() ?? "",
            CreatedAt = DateTime.UtcNow
        };

        _db.Accounts.Add(account);
        await _db.SaveChangesAsync();
        return Ok(account);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var account = await _db.Accounts.FindAsync(id);
        if (account == null) return NotFound();

        var accountIdStr = id.ToString();
        var txns = await _db.Transactions.Where(t => t.AccountId == accountIdStr).ToListAsync();
        _db.Transactions.RemoveRange(txns);

        var profiles = await _db.CsvMappingProfiles.Where(p => p.AccountId == accountIdStr).ToListAsync();
        _db.CsvMappingProfiles.RemoveRange(profiles);

        _db.Accounts.Remove(account);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    public record CreateAccountRequest(string Name, string? Description);
}
