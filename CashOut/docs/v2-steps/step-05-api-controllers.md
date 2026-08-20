# Step 05 — API Controllers

**Goal:** Create all API controllers for v2. Remove `BusinessNormalizationController` and `AccountReportsController`. Simplify `ReportsController` to only Inflow vs Outflow + Spending by Category. Add `UpdateCategory` to `TransactionService`.

**Prerequisites:** Steps 01–04 complete.

---

## 5.1 Add UpdateCategory to TransactionService

Add this method to `CashOut/Services/TransactionService.cs` (inside the class, after the `Query` method):

```csharp
public async Task<Transaction?> UpdateCategory(string transactionId, string category)
{
    var txn = await _db.Transactions.FindAsync(transactionId);
    if (txn == null) return null;

    txn.Category = category;
    txn.UpdatedAt = DateTime.UtcNow;
    await _db.SaveChangesAsync();
    return txn;
}
```

## 5.2 ReportsController (stripped)

**File:** `CashOut/Controllers/ReportsController.cs`

```csharp
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly ReportService _reports;

    public ReportsController(ReportService reports)
    {
        _reports = reports;
    }

    [HttpGet("monthly")]
    public async Task<IActionResult> Monthly(
        [FromQuery] int? year, [FromQuery] string? format)
    {
        if (format == "csv")
            return File(await _reports.MonthlyCsv(year), "text/csv", "monthly.csv");
        return Ok(await _reports.GetMonthly(year));
    }

    [HttpGet("category")]
    public async Task<IActionResult> Category(
        [FromQuery] int? year, [FromQuery] int? month, [FromQuery] string? format)
    {
        if (format == "csv")
            return File(await _reports.CategoryCsv(year, month), "text/csv", "category.csv");
        return Ok(await _reports.GetByCategory(year, month));
    }

    [HttpGet("cashflow")]
    public async Task<IActionResult> CashFlow(
        [FromQuery] int? year, [FromQuery] string? format)
    {
        if (format == "csv")
            return File(await _reports.CashFlowCsv(year), "text/csv", "cashflow.csv");
        return Ok(await _reports.GetCashFlow(year));
    }
}
```

## 5.3 AccountsController

**File:** `CashOut/Controllers/AccountsController.cs`

```csharp
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
```

## 5.4 ManualAccountsController

**File:** `CashOut/Controllers/ManualAccountsController.cs`

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/manual-accounts")]
public class ManualAccountsController : ControllerBase
{
    private readonly AppDbContext _db;

    public ManualAccountsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var accounts = await _db.ManualAccounts
            .OrderBy(a => a.Name)
            .ToListAsync();
        return Ok(accounts);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateManualAccountRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { error = "Name is required." });

        var account = new ManualAccount
        {
            Id = Guid.NewGuid(),
            Name = req.Name.Trim(),
            Description = req.Description?.Trim() ?? "",
            CreatedAt = DateTime.UtcNow
        };

        _db.ManualAccounts.Add(account);
        await _db.SaveChangesAsync();
        return Ok(account);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var account = await _db.ManualAccounts.FindAsync(id);
        if (account == null) return NotFound();

        var accountIdStr = id.ToString();
        var txns = await _db.Transactions.Where(t => t.AccountId == accountIdStr).ToListAsync();
        _db.Transactions.RemoveRange(txns);

        var profiles = await _db.CsvMappingProfiles.Where(p => p.AccountId == accountIdStr).ToListAsync();
        _db.CsvMappingProfiles.RemoveRange(profiles);

        _db.ManualAccounts.Remove(account);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    public record CreateManualAccountRequest(string Name, string? Description);
}
```

## 5.5 PlaidLinkController

**File:** `CashOut/Controllers/PlaidLinkController.cs`

```csharp
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
```

## 5.6 TransactionsController

**File:** `CashOut/Controllers/TransactionsController.cs`

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/transactions")]
public class TransactionsController : ControllerBase
{
    private readonly TransactionService _txns;
    private readonly SettingsService _settings;
    private readonly AppDbContext _db;

    public TransactionsController(
        TransactionService txns,
        SettingsService settings,
        AppDbContext db)
    {
        _txns = txns;
        _settings = settings;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int? year,
        [FromQuery] int? month,
        [FromQuery] string? accountId,
        [FromQuery] List<string>? category)
    {
        var results = await _txns.Query(year, month, accountId, category);

        var linkedNames = await _db.LinkedAccounts
            .ToDictionaryAsync(a => a.AccountId, a => a.Name);
        var manualNames = await _db.ManualAccounts
            .ToDictionaryAsync(a => a.Id.ToString(), a => a.Name);

        var response = results.Select(t => new
        {
            t.TransactionId,
            t.AccountId,
            AccountName = linkedNames.GetValueOrDefault(t.AccountId)
                          ?? manualNames.GetValueOrDefault(t.AccountId)
                          ?? t.AccountId,
            t.Date,
            t.Name,
            t.Credit,
            t.Debit,
            t.Amount,
            t.Category
        });

        return Ok(response);
    }

    [HttpPost("sync")]
    public async Task<IActionResult> Sync()
    {
        var (added, removed) = await _txns.SyncAll();
        return Ok(new { added, removed });
    }

    [HttpPost("fetch")]
    public async Task<IActionResult> Fetch()
    {
        var count = await _txns.FetchAll();
        return Ok(new { written = count });
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] int? year)
    {
        var resolvedYear = year ?? await _settings.GetOutputYear();
        var csv = await _txns.ExportCsv(resolvedYear);
        return File(csv, "text/csv", $"cashout-{resolvedYear}.csv");
    }

    [HttpPatch("{transactionId}/category")]
    public async Task<IActionResult> UpdateCategory(
        string transactionId, [FromBody] UpdateCategoryRequest req)
    {
        var updated = await _txns.UpdateCategory(transactionId, req.Category ?? "");
        if (updated == null) return NotFound();
        return Ok(new { updated.TransactionId, updated.Category });
    }

    public record UpdateCategoryRequest(string? Category);
}
```

## 5.7 CsvImportController

**File:** `CashOut/Controllers/CsvImportController.cs`

```csharp
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/csv-import")]
public class CsvImportController : ControllerBase
{
    private readonly CsvImportService _csv;
    private readonly PdfImportService _pdf;

    public CsvImportController(CsvImportService csv, PdfImportService pdf)
    {
        _csv = csv;
        _pdf = pdf;
    }

    [HttpGet("{accountId}/profile")]
    public async Task<IActionResult> GetProfile(string accountId)
    {
        var profile = await _csv.GetCurrentProfile(accountId);
        if (profile == null) return NotFound();
        return Ok(profile);
    }

    [HttpPost("{accountId}/profile")]
    public async Task<IActionResult> SaveProfile(
        string accountId, [FromBody] CsvMappingProfile profile)
    {
        var saved = await _csv.SaveProfile(accountId, profile);
        return Ok(saved);
    }

    [HttpPost("{accountId}/preview")]
    public async Task<IActionResult> Preview(
        string accountId,
        [FromForm] IFormFile file,
        [FromQuery] int skipTop = 0,
        [FromQuery] int skipBottom = 0)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded." });

        using var reader = new System.IO.StreamReader(file.OpenReadStream());
        var content = await reader.ReadToEndAsync();
        var preview = _csv.Preview(content, skipTop, skipBottom);
        return Ok(preview);
    }

    [HttpPost("{accountId}/pdf-preview")]
    public async Task<IActionResult> PdfPreview(
        string accountId,
        [FromForm] IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded." });

        try
        {
            using var ms = new System.IO.MemoryStream();
            await file.CopyToAsync(ms);
            var pdfBytes = ms.ToArray();

            var csvContent = _pdf.ExtractCsv(pdfBytes);

            if (string.IsNullOrWhiteSpace(csvContent.Replace("Date,Description,Amount", "").Trim()))
                return BadRequest(new { error = "No transactions could be extracted from this PDF." });

            var preview = _csv.Preview(csvContent);
            return Ok(new { csvContent, preview });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{accountId}/import")]
    public async Task<IActionResult> Import(
        string accountId,
        [FromForm] IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded." });

        var profile = await _csv.GetCurrentProfile(accountId);
        if (profile == null)
            return BadRequest(new { error = "No mapping profile found for this account." });

        using var reader = new System.IO.StreamReader(file.OpenReadStream());
        var content = await reader.ReadToEndAsync();

        try
        {
            var result = await _csv.Import(accountId, content, profile);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{accountId}/skipped-export")]
    public IActionResult ExportSkipped([FromBody] List<SkippedRow> skippedRows)
    {
        var sb = new System.Text.StringBuilder("Row,RawData,Reason\n");
        foreach (var row in skippedRows)
            sb.AppendLine($"{row.RowNumber},{EscCsv(row.RawData)},{EscCsv(row.Reason)}");
        return File(System.Text.Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "skipped-rows.csv");
    }

    private static string EscCsv(string s) =>
        s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r')
            ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
}
```

## 5.8 SettingsController

**File:** `CashOut/Controllers/SettingsController.cs`

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/settings")]
public class SettingsController : ControllerBase
{
    private readonly SettingsService _settings;
    private readonly AppDbContext _db;

    public SettingsController(SettingsService settings, AppDbContext db)
    {
        _settings = settings;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await _settings.GetAll());

    [HttpGet("years")]
    public async Task<IActionResult> AvailableYears() =>
        Ok(await _settings.GetAvailableYears());

    [HttpGet("excluded-categories")]
    public async Task<IActionResult> GetExcludedCategories() =>
        Ok(await _settings.GetExcludedCategories());

    [HttpGet("categories")]
    public async Task<IActionResult> GetAllCategories()
    {
        var categories = await _db.Transactions
            .Where(t => t.Category != null && t.Category != "")
            .Select(t => t.Category)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();
        return Ok(categories);
    }

    [HttpPost("cleanup")]
    public async Task<IActionResult> CleanupOrphans()
    {
        var linked = await _db.LinkedAccounts.ToListAsync();
        var manual = await _db.ManualAccounts.ToListAsync();

        var validIds = new HashSet<string>();
        foreach (var la in linked)
        {
            validIds.Add(la.AccountId);
            validIds.Add(la.Id.ToString());
        }
        foreach (var ma in manual)
            validIds.Add(ma.Id.ToString());

        var orphanTxns = await _db.Transactions
            .Where(t => !validIds.Contains(t.AccountId))
            .ToListAsync();
        var orphanProfiles = await _db.CsvMappingProfiles
            .Where(p => !validIds.Contains(p.AccountId))
            .ToListAsync();

        var txnCount = orphanTxns.Count;
        var profileCount = orphanProfiles.Count;

        _db.Transactions.RemoveRange(orphanTxns);
        _db.CsvMappingProfiles.RemoveRange(orphanProfiles);
        await _db.SaveChangesAsync();

        return Ok(new { transactionsRemoved = txnCount, profilesRemoved = profileCount });
    }
}
```

## 5.9 DebugController

**File:** `CashOut/Controllers/DebugController.cs`

```csharp
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
```

## 5.10 VersionController

**File:** `CashOut/Controllers/VersionController.cs`

```csharp
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/version")]
public class VersionController : ControllerBase
{
    private static readonly string _version = ReadVersion();

    private static string ReadVersion()
    {
        var versionFile = Path.Combine(AppContext.BaseDirectory, "VERSION");
        if (System.IO.File.Exists(versionFile))
            return System.IO.File.ReadAllText(versionFile).Trim();

        var asm = typeof(VersionController).Assembly;
        var attr = asm.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false);
        if (attr.Length > 0)
            return ((System.Reflection.AssemblyInformationalVersionAttribute)attr[0]).InformationalVersion;

        return "unknown";
    }

    [HttpGet]
    public IActionResult Get() => Ok(new { version = _version });
}
```

## 5.11 Verify build

```bash
dotnet build CashOut/CashOut.csproj
```

---

## Verification

1. `BusinessNormalizationController.cs` does NOT exist
2. `AccountReportsController.cs` does NOT exist
3. `ReportsController` has only 3 endpoints: `monthly`, `category`, `cashflow`
4. No `Include(t => t.Alias)` anywhere in controllers
5. `TransactionsController` has `UpdateCategory` endpoint
6. `dotnet build` succeeds
