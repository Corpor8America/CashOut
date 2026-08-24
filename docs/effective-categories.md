# Effective Categories

## Goal

Let the user define "contains" patterns that map messy transaction names to clean **effective categories**. Effective categories are stored on each transaction via a FK, supporting both automatic rule-based assignment and manual one-off overrides.

Example: rules `Food Lion` → Groceries and `Harris Teeter` → Groceries cause all matching transactions to be categorized as Groceries. A one-off Walmart trip can be manually assigned to Groceries without creating a rule.

## Non-Goals

- No changes to the existing `Category` field on transactions — it stays as-is from CSV import
- No changes to existing reports — the original "By Category" report continues using raw `Category`
- No regex, no aliases, no merchant entities — just substring → category
- No retroactive overwriting of manually assigned categories

## Design Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Match type | Case-insensitive contains on `Transaction.Name` | Handles store-number suffixes and casing drift |
| Multiple rules match | Longest pattern wins; ties broken by oldest rule (lowest `Id`) | Specificity beats generality, fully deterministic |
| Rule vs. manual | Manual assignment wins (stored `CategoryId` is never overwritten by rules) | User intent is always respected |
| No rule matches | Fall back to `CategoryId` if set, else `(uncategorized)` | Preserves manual overrides |
| Where resolved | Stored FK on `Transaction.CategoryId` | Fast queries, no runtime matching needed for display |
| Rule tracking | `Transaction.CategoryRuleId` tracks which rule assigned the category | Enables clean uncategorization on rule deletion |
| Writes | `categories`, `category_rules`, and `transactions.CategoryId`/`CategoryRuleId` | Minimal blast radius |

## 1. New Entity — `CashOut/Models/Category.cs`

```csharp
public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

## 2. New Entity — `CashOut/Models/CategoryRule.cs`

```csharp
public class CategoryRule
{
    public int Id { get; set; }
    public string Pattern { get; set; } = "";
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

## 3. Modified Entity — `CashOut/Models/Transaction.cs`

Add two nullable FK properties:

```csharp
public int? CategoryId { get; set; }
public Category? EffectiveCategory { get; set; }

public int? CategoryRuleId { get; set; }
public CategoryRule? AssignedByRule { get; set; }
```

- `CategoryId` + `CategoryRuleId` both null → uncategorized
- `CategoryId` set, `CategoryRuleId` null → manually assigned
- Both set → assigned by a rule

## 4. DbContext — `CashOut/Data/AppDbContext.cs`

Add DbSets:

```csharp
public DbSet<Category> Categories => Set<Category>();
public DbSet<CategoryRule> CategoryRules => Set<CategoryRule>();
```

Add fluent config in `OnModelCreating`:

```csharp
modelBuilder.Entity<Category>(e =>
{
    e.ToTable("categories");
    e.HasKey(x => x.Id);
    e.Property(x => x.Id).ValueGeneratedOnAdd();
    e.Property(x => x.Name).IsRequired();
    e.HasIndex(x => x.Name).IsUnique();
    e.Property(x => x.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");
    e.Property(x => x.UpdatedAt).HasDefaultValueSql("now() at time zone 'utc'");
});

modelBuilder.Entity<CategoryRule>(e =>
{
    e.ToTable("category_rules");
    e.HasKey(x => x.Id);
    e.Property(x => x.Id).ValueGeneratedOnAdd();
    e.Property(x => x.Pattern).IsRequired();
    e.Property(x => x.CategoryId).IsRequired();
    e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId);
    e.HasIndex(x => x.Pattern);
    e.Property(x => x.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");
    e.Property(x => x.UpdatedAt).HasDefaultValueSql("now() at time zone 'utc'");
});

modelBuilder.Entity<Transaction>(e =>
{
    // ... existing config ...
    e.Property(x => x.CategoryId).IsRequired(false);
    e.HasOne(x => x.EffectiveCategory).WithMany().HasForeignKey(x => x.CategoryId);
    e.Property(x => x.CategoryRuleId).IsRequired(false);
    e.HasOne(x => x.AssignedByRule).WithMany().HasForeignKey(x => x.CategoryRuleId);
});
```

## 5. Migration

Per AGENTS.md workflow:

```bash
docker-compose -f docker-compose.dev.yml up db -d
dotnet ef migrations add AddEffectiveCategories --project CashOut
dotnet build CashOut/CashOut.csproj
```

Startup auto-migration (`Program.cs`) applies it automatically. Verify the generated migration creates `categories`, `category_rules`, and adds `CategoryId`/`CategoryRuleId` columns to `transactions`.

## 6. Service — `CashOut/Services/CategoryService.cs`

Scoped service for category CRUD.

```csharp
public class CategoryService
{
    private readonly AppDbContext _db;

    public CategoryService(AppDbContext db) { _db = db; }

    public async Task<List<Category>> GetAll() =>
        await _db.Categories.OrderBy(c => c.Name).ToListAsync();

    public async Task<Category> Create(string name)
    {
        var category = new Category
        {
            Name = name.Trim(),
            UpdatedAt = DateTime.UtcNow,
        };
        _db.Categories.Add(category);
        await _db.SaveChangesAsync();
        return category;
    }

    public async Task<Category?> Update(int id, string name)
    {
        var category = await _db.Categories.FindAsync(id);
        if (category == null) return null;
        category.Name = name.Trim();
        category.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return category;
    }

    public async Task<bool> Delete(int id)
    {
        var category = await _db.Categories.FindAsync(id);
        if (category == null) return false;

        // Uncategorize transactions manually assigned to this category
        var manualTxns = await _db.Transactions
            .Where(t => t.CategoryId == id && t.CategoryRuleId == null)
            .ToListAsync();
        foreach (var t in manualTxns)
        {
            t.CategoryId = null;
            t.CategoryRuleId = null;
            t.UpdatedAt = DateTime.UtcNow;
        }

        // Delete rules pointing to this category
        var rules = await _db.CategoryRules
            .Where(r => r.CategoryId == id)
            .ToListAsync();
        _db.CategoryRules.RemoveRange(rules);

        // Uncategorize transactions assigned by these rules
        var ruleIds = rules.Select(r => r.Id).ToList();
        var ruleTxns = await _db.Transactions
            .Where(t => t.CategoryId == id && t.CategoryRuleId != null
                     && ruleIds.Contains(t.CategoryRuleId!.Value))
            .ToListAsync();
        foreach (var t in ruleTxns)
        {
            t.CategoryId = null;
            t.CategoryRuleId = null;
            t.UpdatedAt = DateTime.UtcNow;
        }

        _db.Categories.Remove(category);
        await _db.SaveChangesAsync();
        return true;
    }
}
```

## 7. Service — `CashOut/Services/CategoryRuleService.cs`

Scoped service for rule CRUD, matching, and reprocessing.

```csharp
public class CategoryRuleService
{
    private readonly AppDbContext _db;

    public CategoryRuleService(AppDbContext db) { _db = db; }

    public async Task<List<CategoryRule>> GetAll() =>
        await _db.CategoryRules
            .Include(r => r.Category)
            .OrderBy(r => r.Id)
            .ToListAsync();

    public async Task<CategoryRule> Create(string pattern, int categoryId)
    {
        var rule = new CategoryRule
        {
            Pattern = pattern.Trim(),
            CategoryId = categoryId,
            UpdatedAt = DateTime.UtcNow,
        };
        _db.CategoryRules.Add(rule);
        await _db.SaveChangesAsync();
        await ReprocessUncategorized();
        return rule;
    }

    public async Task<CategoryRule?> Update(int id, string pattern, int categoryId)
    {
        var rule = await _db.CategoryRules.FindAsync(id);
        if (rule == null) return null;
        rule.Pattern = pattern.Trim();
        rule.CategoryId = categoryId;
        rule.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await ReprocessUncategorized();
        return rule;
    }

    public async Task<bool> Delete(int id)
    {
        var rule = await _db.CategoryRules.FindAsync(id);
        if (rule == null) return false;

        // Uncategorize transactions that were assigned by this rule
        var txns = await _db.Transactions
            .Where(t => t.CategoryRuleId == id)
            .ToListAsync();
        foreach (var t in txns)
        {
            t.CategoryId = null;
            t.CategoryRuleId = null;
            t.UpdatedAt = DateTime.UtcNow;
        }

        _db.CategoryRules.Remove(rule);
        await _db.SaveChangesAsync();
        return true;
    }

    public string? Match(string name, IReadOnlyList<CategoryRule> rules)
    {
        return rules
            .Where(r => name.Contains(r.Pattern, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.Pattern.Length)
            .ThenBy(r => r.Id)
            .FirstOrDefault()?.Category.Name;
    }

    public string EffectiveCategoryName(Transaction t, IReadOnlyList<CategoryRule> rules)
    {
        if (t.CategoryId.HasValue && t.EffectiveCategory != null)
            return t.EffectiveCategory.Name;

        var matched = Match(t.Name, rules);
        if (matched != null) return matched;

        return string.IsNullOrWhiteSpace(t.Category) ? "(uncategorized)" : t.Category;
    }

    public async Task ReprocessUncategorized()
    {
        var rules = await GetAll();
        if (rules.Count == 0) return;

        var uncategorized = await _db.Transactions
            .Where(t => t.CategoryId == null)
            .ToListAsync();

        foreach (var txn in uncategorized)
        {
            var bestRule = rules
                .Where(r => txn.Name.Contains(r.Pattern, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.Pattern.Length)
                .ThenBy(r => r.Id)
                .FirstOrDefault();

            if (bestRule != null)
            {
                txn.CategoryId = bestRule.CategoryId;
                txn.CategoryRuleId = bestRule.Id;
                txn.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync();
    }

    public static string SuggestPattern(string transactionName)
    {
        var name = transactionName.Trim();
        // Strip trailing #NNNN or digit runs
        name = System.Text.RegularExpressions.Regex.Replace(name, @"\s*#\d+\s*$", "");
        name = System.Text.RegularExpressions.Regex.Replace(name, @"\s+\d{3,}\s*$", "");
        return name.Trim();
    }
}
```

## 8. Modified Service — `CashOut/Services/TransactionService.cs`

Update `Query` to include effective category, and add assignment method:

```csharp
public async Task<List<Transaction>> Query(
    int? year = null, int? month = null, string? accountId = null,
    List<string>? categories = null,
    TransactionSource? source = null,
    List<int>? effectiveCategoryIds = null)
{
    var q = _db.Transactions
        .Include(t => t.EffectiveCategory)
        .AsQueryable();

    // ... existing filters ...

    if (effectiveCategoryIds is { Count: > 0 })
        q = q.Where(t => t.CategoryId.HasValue && effectiveCategoryIds.Contains(t.CategoryId.Value));

    return await q.OrderByDescending(t => t.Date).ToListAsync();
}

public async Task<Transaction?> AssignEffectiveCategory(
    string transactionId, int? categoryId, int? categoryRuleId = null)
{
    var txn = await _db.Transactions.FindAsync(transactionId);
    if (txn == null) return null;

    txn.CategoryId = categoryId;
    txn.CategoryRuleId = categoryRuleId;
    txn.UpdatedAt = DateTime.UtcNow;
    await _db.SaveChangesAsync();
    return txn;
}
```

## 9. Modified Service — `CashOut/Services/CsvImportService.cs`

At the end of `Import()`, after `SaveChangesAsync`, call reprocess:

```csharp
// After existing _db.SaveChangesAsync() at line 225:
var ruleService = new CategoryRuleService(_db);
await ruleService.ReprocessUncategorized();
```

Alternatively, inject `CategoryRuleService` and call it directly. The reprocess only touches `CategoryId == null` transactions, so newly imported ones get matched.

## 10. API — `CashOut/Controllers/CategoriesController.cs`

```csharp
[ApiController]
[Route("api/categories")]
public class CategoriesController : ControllerBase
{
    private readonly CategoryService _categories;

    public CategoriesController(CategoryService categories)
    {
        _categories = categories;
    }

    [HttpGet]
    public async Task<IActionResult> List()
        => Ok(await _categories.GetAll());

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UpsertRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest("Name is required.");
        var category = await _categories.Create(req.Name);
        return Ok(category);
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpsertRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest("Name is required.");
        var category = await _categories.Update(id, req.Name);
        return category == null ? NotFound() : Ok(category);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await _categories.Delete(id);
        return deleted ? Ok() : NotFound();
    }

    public record UpsertRequest(string Name);
}
```

## 11. API — `CashOut/Controllers/CategoryRulesController.cs`

```csharp
[ApiController]
[Route("api/category-rules")]
public class CategoryRulesController : ControllerBase
{
    private readonly CategoryRuleService _rules;
    private readonly AppDbContext _db;

    public CategoryRulesController(CategoryRuleService rules, AppDbContext db)
    {
        _rules = rules;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var rules = await _rules.GetAll();
        var names = await _db.Transactions.Select(t => t.Name).Distinct().ToListAsync();

        var response = rules.Select(r => new
        {
            r.Id,
            r.Pattern,
            CategoryName = r.Category.Name,
            r.CategoryId,
            MatchCount = names.Count(n => n.Contains(r.Pattern, StringComparison.OrdinalIgnoreCase)),
        });

        return Ok(response);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UpsertRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Pattern) || req.CategoryId == null)
            return BadRequest("Pattern and CategoryId are required.");
        var rule = await _rules.Create(req.Pattern, req.CategoryId.Value);
        return Ok(rule);
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpsertRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Pattern) || req.CategoryId == null)
            return BadRequest("Pattern and CategoryId are required.");
        var rule = await _rules.Update(id, req.Pattern, req.CategoryId.Value);
        return rule == null ? NotFound() : Ok(rule);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await _rules.Delete(id);
        return deleted ? Ok() : NotFound();
    }

    [HttpGet("suggest-pattern")]
    public IActionResult SuggestPattern([FromQuery] string transactionName)
        => Ok(new { Pattern = CategoryRuleService.SuggestPattern(transactionName) });

    public record UpsertRequest(string Pattern, int? CategoryId);
}
```

## 12. Modified API — `CashOut/Controllers/TransactionsController.cs`

Add effective category to list response and new assignment endpoint:

```csharp
[HttpGet]
public async Task<IActionResult> List(
    [FromQuery] int? year,
    [FromQuery] int? month,
    [FromQuery] string? accountId,
    [FromQuery] List<string>? category,
    [FromQuery] List<int>? effectiveCategoryId)
{
    var results = await _txns.Query(year, month, accountId, category,
        effectiveCategoryIds: effectiveCategoryId);

    var accountNames = await _db.Accounts
        .ToDictionaryAsync(a => a.Id.ToString(), a => a.Name);

    var response = results.Select(t => new
    {
        t.TransactionId,
        t.AccountId,
        AccountName = accountNames.GetValueOrDefault(t.AccountId) ?? t.AccountId,
        t.Date,
        t.Name,
        t.Credit,
        t.Debit,
        t.Amount,
        t.Category,
        EffectiveCategoryId = t.CategoryId,
        EffectiveCategoryName = t.EffectiveCategory?.Name ?? "",
        IsManualAssignment = t.CategoryId.HasValue && t.CategoryRuleId == null,
    });

    return Ok(response);
}

[HttpPatch("{transactionId}/effective-category")]
public async Task<IActionResult> AssignEffectiveCategory(
    string transactionId,
    [FromBody] AssignEffectiveCategoryRequest req)
{
    var updated = await _txns.AssignEffectiveCategory(
        transactionId, req.CategoryId, req.CategoryRuleId);
    if (updated == null) return NotFound();
    return Ok(new
    {
        updated.TransactionId,
        EffectiveCategoryId = updated.CategoryId,
        EffectiveCategoryName = updated.EffectiveCategory?.Name ?? "",
    });
}

public record AssignEffectiveCategoryRequest(int? CategoryId, int? CategoryRuleId);
```

## 13. Modified Page — `CashOut/Pages/Transactions.razor`

### Changes

1. **Update `TransactionDto`** to include `EffectiveCategoryId`, `EffectiveCategoryName`, `IsManualAssignment`
2. **Add "Effective Category" column** to both the search and grouped table views, between Category and Amount
3. **Add effective category filter** — popover similar to the existing category filter, sourced from `api/categories`
4. **Add assignment dialog** — MudDialog triggered by clicking the effective category cell

### Assignment Dialog Flow

When the user clicks a transaction's effective category cell:

1. **Dialog opens** showing:
   - Transaction name (read-only)
   - Current effective category (if any)
   - Category dropdown (all categories from `api/categories`)
   - "Create new category" toggle → reveals category name input
   - Optional "Pattern" field — auto-suggested via `api/category-rules/suggest-pattern?transactionName=...`
   - Info text: "Leave pattern blank to assign only this transaction"

2. **On save:**
   - If "Create new category" → `POST api/categories` first
   - If pattern provided → `POST api/category-rules` (creates rule, reprocess runs server-side)
   - `PATCH api/transactions/{id}/effective-category` with `{ CategoryId, CategoryRuleId }`
   - Reload transaction list

3. **Clear assignment** — option to set `CategoryId = null` (removes effective category)

### Effective Category Filter

Similar to the existing raw-category filter popover:
- Source categories from `GET api/categories`
- Filter client-side on `EffectiveCategoryName`
- Badge shows count of active filters

## 14. New Page — `CashOut/Pages/CategoryRules.razor`

Route: `/category-rules`. Two sections:

### Section 1 — Categories

- MudTable listing categories: Name, actions (edit, delete)
- Add-category form row (text field + save button)
- Edit switches to inline edit mode; delete asks for confirm
- Deleting a category uncategorizes affected transactions (server-side)

### Section 2 — Rules

- MudTable listing rules: Pattern, Category (name), MatchCount, actions (edit, delete)
- Add-rule form row (text field for pattern, dropdown for category, save button)
- Pattern field auto-suggests when a transaction name is pasted (via `suggest-pattern` endpoint)
- Edit switches to inline edit mode; delete asks for confirm
- After every mutation, reload both sections

### Data Flow

The page calls:
- `GET /api/categories` — for category list and rule dropdown
- `GET /api/category-rules` — for rule list with match counts
- `POST /api/categories` — create category
- `POST /api/category-rules` — create rule (triggers reprocess server-side)
- `DELETE /api/categories/{id}` — delete category
- `DELETE /api/category-rules/{id}` — delete rule

## 15. New Page — `CashOut/Pages/ReportEffectiveCategory.razor`

Route: `/reports/effective-category`. Mirrors the existing `ReportCategory.razor` layout using `ReportShell`.

### Differences from ReportCategory

- Groups transactions by `EffectiveCategoryName` instead of raw `Category`
- Uses `EffectiveCategoryId` for filtering
- API endpoint: `GET api/reports/effective-category?fromYear=...&fromMonth=...&toYear=...&toMonth=...&accountId=...`
- Transactions with no effective category appear under `(uncategorized)`

### Report Service Method

Add `GetEffectiveCategoryDetail()` to `ReportService.cs` — same structure as `GetCategoryDetail()` but groups by `t.EffectiveCategory?.Name ?? "(uncategorized)"` instead of `CategoryKey(t)`.

### Controller Endpoint

Add to `ReportsController.cs`:

```csharp
[HttpGet("effective-category")]
public async Task<IActionResult> EffectiveCategory(
    [FromQuery] int? fromYear, [FromQuery] int? fromMonth,
    [FromQuery] int? toYear, [FromQuery] int? toMonth,
    [FromQuery] string? accountId,
    [FromQuery] string? format)
{
    var result = await _reports.GetEffectiveCategoryDetail(
        fromYear, fromMonth, toYear, toMonth, accountId);
    if (format == "csv") return File(_reports.EffectiveCategoryDetailCsv(...), "text/csv");
    return Ok(result);
}
```

## 16. Wiring

### `CashOut/Program.cs` — DI registration

```csharp
builder.Services.AddScoped<CategoryService>();
builder.Services.AddScoped<CategoryRuleService>();
```

### `CashOut/Shared/MainLayout.razor` — nav links

Under Reports, after "By Category":

```razor
<MudNavLink Href="/reports/effective-category"
            Icon="@Icons.Material.Filled.Label">
    By Effective Category
</MudNavLink>
```

Under a new "Manage" section or within Reports:

```razor
<MudNavLink Href="/category-rules"
            Icon="@Icons.Material.Filled.Rule">
    Category Rules
</MudNavLink>
```

## 17. Tests — `CashOut.Tests/CategoryServiceTests.cs`

MSTest, in-memory EF, unique database name per test, `MethodName_Scenario_ExpectedBehavior` naming:

- `Create_TrimsName_ReturnsCategory`
- `Update_ExistingId_UpdatesName`
- `Delete_ExistingId_RemovesCategoryAndRules`
- `Delete_CategoryWithManualAssignments_UncategorizesTransactions`

## 18. Tests — `CashOut.Tests/CategoryRuleServiceTests.cs`

- `Match_ContainsPattern_CaseInsensitive_MatchesStoreVariants`
- `Match_MultipleMatches_LongestPatternWins`
- `Match_MultipleMatches_SameLength_OldestRuleWins`
- `Match_NoMatch_ReturnsNull`
- `EffectiveCategoryName_TransactionWithCategoryId_ReturnsStoredCategory`
- `EffectiveCategoryName_NoCategoryId_MatchesRules`
- `EffectiveCategoryName_NoMatch_BlankCategory_ReturnsUncategorized`
- `Create_SavesRuleAndReprocessesUncategorized`
- `Delete_ExistingId_UncategorizesTransactionsAssignedByRule`
- `ReprocessUncategorized_AssignsMatchingRules`
- `ReprocessUncategorized_SkipsTransactionsWithCategoryId`
- `SuggestPattern_StripsTrailingHashDigits`
- `SuggestPattern_StripsTrailingDigitRuns`
- `SuggestPattern_LeavesCleanNamesUnchanged`

## 19. Verification

```bash
dotnet build CashOut/CashOut.csproj
dotnet test CashOut.Tests/CashOut.Tests.csproj
```

Manual smoke test against the full Docker stack:

1. Open `/category-rules`, create category "Groceries"
2. Create rule `Food Lion` → Groceries
3. Confirm reprocess runs (transactions page shows Food Lion transactions with effective category Groceries)
4. Open `/transactions`, click a non-Food-Lion transaction's effective category cell
5. Assign it to Groceries without a pattern (manual assignment)
6. Open `/reports/effective-category`, confirm Groceries group shows both rule-assigned and manual transactions
7. Delete the Food Lion rule — confirm rule-assigned transactions lose their effective category, manual assignment stays
8. Delete the Groceries category — confirm all assignments are cleared
9. Import a new CSV — confirm new transactions get auto-assigned if rules exist
