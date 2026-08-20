# Step 3: Delete Normalization Code

## Goal

Delete the entire merchant normalization system: the service, the controller, the Merchants page, and the category-edit endpoint on `TransactionsController`. This step eliminates all code that depends on the removed entity types from Step 2.

## Files to Delete Entirely

### 1. `CashOut/Services/MerchantNormalizationService.cs` (482 lines)

Delete the entire file. Contains:
- `Normalize()` — string normalization
- `MatchAlias()` / `MatchAliasFromPatterns()` — pattern matching
- `Resolve()` / `ResolveBulk()` — alias resolution during import
- `CreateAlias()`, `UpdateAliasName()`, `UpdateAliasCategory()` — alias CRUD
- `AddPattern()`, `RemovePattern()` — pattern CRUD
- `DeleteAlias()` — with retroactive reprocessing
- `MapRawToAlias()`, `UnmapRawBusiness()` — mapping CRUD
- `RetroactivelyMap()` / `ReprocessUnaliasedTransactions()` — batch reprocessing
- `CleanupRawBusinesses()` — orphan cleanup
- `GetUnmappedBusinesses()`, `GetAllRawBusinesses()`, `GetAllAliases()` — queries
- `TestPattern()` — pattern testing
- `PatternTestResult` record

### 2. `CashOut/Controllers/BusinessNormalizationController.cs` (191 lines)

Delete the entire file. Route: `/api/normalization`. Contains all normalization API endpoints:
- `GET /api/normalization/aliases`
- `POST /api/normalization/aliases`
- `PATCH /api/normalization/aliases/{id}/category`
- `PATCH /api/normalization/aliases/{id}/name`
- `DELETE /api/normalization/aliases/{id}`
- `POST /api/normalization/aliases/{aliasId}/patterns`
- `DELETE /api/normalization/patterns/{patternId}`
- `POST /api/normalization/aliases/test`
- `GET /api/normalization/businesses`
- `GET /api/normalization/mappings`
- `POST /api/normalization/mappings`
- `DELETE /api/normalization/mappings/{rawBusinessId}`
- `POST /api/normalization/retroactive-map`

### 3. `CashOut/Pages/Merchants.razor` (703 lines)

Delete the entire file. Route: `/merchants`. Contains three tabs:
- Unmapped merchants (with inline create-or-map UI)
- Aliases & Patterns (CRUD for aliases, patterns)
- Pattern Tester

## Files to Modify

### 4. `CashOut/Controllers/TransactionsController.cs` — Remove Category Edit Endpoint

**Remove lines 73-82** (the `UpdateCategory` action and its request type):

```csharp
// REMOVE these members entirely:

[HttpPatch("{transactionId}/category")]
public async Task<IActionResult> UpdateCategory(
    string transactionId, [FromBody] UpdateCategoryRequest req)
{
    var updated = await _txns.UpdateCategory(transactionId, req.Category ?? "");
    if (updated == null) return NotFound();
    return Ok(new { updated.TransactionId, updated.Category });
}

public record UpdateCategoryRequest(string? Category);
```

**The controller file should retain these endpoints after edit:**

```csharp
[ApiController]
[Route("api/transactions")]
public class TransactionsController : ControllerBase
{
    private readonly TransactionService _txns;
    private readonly SettingsService _settings;
    private readonly AppDbContext _db;

    public TransactionsController(TransactionService txns, SettingsService settings, AppDbContext db)
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
}
```

### 5. `CashOut/Program.cs` — Remove DI Registration

**Remove line 46:**

```csharp
// REMOVE:
builder.Services.AddScoped<MerchantNormalizationService>();
```

**The services section should now be:**

```csharp
builder.Services.AddSingleton<EncryptionService>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddHttpClient<PlaidService>(client =>
{
    client.DefaultRequestHeaders.Add("Plaid-Version", "2020-09-14");
});
// MerchantNormalizationService REMOVED
builder.Services.AddScoped<CsvImportService>();
builder.Services.AddScoped<PdfImportService>();
builder.Services.AddScoped<TransactionService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<AccountReportService>();
```

### 6. `CashOut/Services/TransactionService.cs` — Remove Normalization Dependency

**Remove the `MerchantNormalizationService` constructor parameter and field.**

Current constructor (lines 15-21):

```csharp
private readonly TransactionService _txns;
private readonly PlaidService _plaid;
private readonly SettingsService _settings;
private readonly MerchantNormalizationService _normalization;  // REMOVE THIS

public TransactionService(
    AppDbContext db,
    PlaidService plaid,
    SettingsService settings,
    MerchantNormalizationService normalization)  // REMOVE THIS PARAMETER
{
    _db = db;
    _plaid = plaid;
    _settings = settings;
    _normalization = normalization;  // REMOVE THIS LINE
}
```

**New constructor:**

```csharp
private readonly AppDbContext _db;
private readonly PlaidService _plaid;
private readonly SettingsService _settings;

public TransactionService(AppDbContext db, PlaidService plaid, SettingsService settings)
{
    _db = db;
    _plaid = plaid;
    _settings = settings;
}
```

**Also remove the `UpdateCategory` method (lines 228-236):**

```csharp
// REMOVE:
public async Task<Transaction?> UpdateCategory(string transactionId, string category)
{
    var txn = await _db.Transactions.FindAsync(transactionId);
    if (txn == null) return null;

    txn.Category = category.Trim();
    txn.UpdatedAt = DateTime.UtcNow;
    await _db.SaveChangesAsync();
    return txn;
}
```

**Keep `MergePlaid` and `Query` methods intact for now** — they will be simplified in Step 4. The build will not compile until Step 4 addresses the normalization references inside `MergePlaid`.

### 7. `CashOut/Services/CsvImportService.cs` — Remove Normalization Dependency

**Remove the `MerchantNormalizationService` constructor parameter and field.**

Current constructor (lines 11-15):

```csharp
private readonly AppDbContext _db;
private readonly MerchantNormalizationService _normalization;  // REMOVE

public CsvImportService(AppDbContext db, MerchantNormalizationService normalization)
{
    _db = db;
    _normalization = normalization;  // REMOVE
}
```

**New constructor:**

```csharp
private readonly AppDbContext _db;

public CsvImportService(AppDbContext db)
{
    _db = db;
}
```

**Keep the `Import` method intact for now** — it will be simplified in Step 4. The build will not compile until Step 4 addresses the normalization references inside `Import`.

### 8. `CashOut.Tests/MerchantNormalizationServiceTests.cs` — Delete Entirely

Delete the entire test file (465 lines, 16 tests). All tests are for the deleted service.

## Verification

```bash
dotnet build CashOut/CashOut.csproj
```

The build **will not compile** at this point because `TransactionService.MergePlaid()` and `CsvImportService.Import()` still reference `_normalization.ResolveBulk()`, `MerchantNormalizationService.Normalize()`, and Transaction properties like `AliasId`, `RawBusinessId`, `NormalizedName` that no longer exist. This is expected — proceed directly to Step 4.

If you want a clean compile between steps, you can defer the CsvImportService/TransactionService constructor changes until Step 4.
