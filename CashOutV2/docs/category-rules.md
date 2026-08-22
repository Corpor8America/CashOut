# Category Rules (Query-Time Category Normalization)

## Goal

Let the user define simple "contains" patterns that normalize messy transaction names into clean categories **at report time**, without ever writing back to the `transactions` table.

Example: one rule `Food Lion` → `Groceries` makes `Food Lion #0070`, `Food Lion #4409`, and `FOOD LION 1234` all report as `Groceries` everywhere this feature applies.

The feature ships as a new page (`/category-rules`) that combines rules management with a categorized preview report showing raw vs. effective category side-by-side.

## Non-Goals

- No changes to the `transactions` table, entity, or any import pipeline.
- No retroactive write-back of categories. Original Plaid/CSV categories are preserved forever.
- Existing reports (`ReportCategory`, `ReportCashFlow`) are unchanged in this phase (see Future Phases).
- No regex, no aliases, no merchant entities — just substring → category.

## Design Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Match type | Case-insensitive contains on `Transaction.Name` | Handles store-number suffixes and casing drift |
| Multiple rules match | Longest pattern wins; ties broken by oldest rule (lowest `Id`) | Specificity beats generality, fully deterministic |
| Rule vs. existing raw category | Rule always wins | Deterministic regardless of edit history |
| No rule matches | Fall back to raw `t.Category`; blank becomes `(uncategorized)` | Same convention as `ReportService.CategoryKey` |
| Where resolved | In memory, after materializing transactions | Personal-finance scale; keeps logic unit-testable with in-memory EF |
| Writes | Only to `category_rules` | One new table, nothing else touched |

## 1. New Entity — `CashOut/Models/CategoryRule.cs`

```csharp
public class CategoryRule
{
    public int Id { get; set; }
    public string Pattern { get; set; } = "";
    public string Category { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

## 2. DbContext — `CashOut/Data/AppDbContext.cs`

Add the `DbSet` alongside the existing four:

```csharp
public DbSet<CategoryRule> CategoryRules => Set<CategoryRule>();
```

Add fluent config in `OnModelCreating` (snake_case table, timestamps consistent with other tables, identity PK because rows are user-created):

```csharp
modelBuilder.Entity<CategoryRule>(e =>
{
    e.ToTable("category_rules");
    e.HasKey(x => x.Id);
    e.Property(x => x.Id).ValueGeneratedOnAdd();
    e.Property(x => x.Pattern).IsRequired();
    e.Property(x => x.Category).IsRequired();
    e.HasIndex(x => x.Pattern);
    e.Property(x => x.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");
    e.Property(x => x.UpdatedAt).HasDefaultValueSql("now() at time zone 'utc'");
});
```

## 3. Migration

Per AGENTS.md workflow:

```bash
docker-compose -f docker-compose.dev.yml up db -d
dotnet ef migrations add AddCategoryRules --project CashOut
dotnet build CashOut/CashOut.csproj
```

Startup auto-migration (`Program.cs`) applies it to the running stack automatically. Verify the generated migration only creates `category_rules` plus its index — no other tables altered.

## 4. Service — `CashOut/Services/CategoryRuleService.cs`

Scoped service owning CRUD, the matcher, and the effective-category report query.

```csharp
public class CategoryRuleService
{
    private readonly AppDbContext _db;

    public CategoryRuleService(AppDbContext db) { _db = db; }

    public async Task<List<CategoryRule>> GetAll() =>
        await _db.CategoryRules.OrderBy(r => r.Id).ToListAsync();

    public async Task<CategoryRule> Create(string pattern, string category)
    {
        var rule = new CategoryRule
        {
            Pattern = pattern.Trim(),
            Category = category.Trim(),
            UpdatedAt = DateTime.UtcNow,
        };
        _db.CategoryRules.Add(rule);
        await _db.SaveChangesAsync();
        return rule;
    }

    public async Task<CategoryRule?> Update(int id, string pattern, string category)
    {
        var rule = await _db.CategoryRules.FindAsync(id);
        if (rule == null) return null;
        rule.Pattern = pattern.Trim();
        rule.Category = category.Trim();
        rule.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return rule;
    }

    public async Task<bool> Delete(int id)
    {
        var rule = await _db.CategoryRules.FindAsync(id);
        if (rule == null) return false;
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
            .FirstOrDefault()?.Category;
    }

    public string EffectiveCategory(Transaction t, IReadOnlyList<CategoryRule> rules)
    {
        var matched = Match(t.Name, rules);
        if (matched != null) return matched;
        return string.IsNullOrWhiteSpace(t.Category) ? "(uncategorized)" : t.Category;
    }

    public async Task<EffectiveReportResult> GetEffectiveReport(
        int? year = null, int? month = null)
    {
        var rules = await GetAll();

        var query = _db.Transactions.AsQueryable();
        if (year.HasValue) query = query.Where(t => t.Date.Year == year.Value);
        if (month.HasValue) query = query.Where(t => t.Date.Month == month.Value);

        var txns = await query.ToListAsync();

        var categories = txns
            .GroupBy(t => EffectiveCategory(t, rules))
            .Select(g =>
            {
                var rows = g
                    .OrderByDescending(t => t.Date)
                    .ThenByDescending(t => Math.Abs(t.Amount))
                    .Select(t => new EffectiveTransactionRow(
                        t.TransactionId,
                        t.Date,
                        t.Name,
                        t.Amount,
                        string.IsNullOrWhiteSpace(t.Category) ? "" : t.Category,
                        g.Key))
                    .ToList();
                return new EffectiveCategoryGroup(
                    g.Key,
                    rows.Sum(r => Math.Abs(r.Amount)),
                    rows.Count,
                    rows);
            })
            .OrderByDescending(g => g.Total)
            .ToList();

        return new EffectiveReportResult(
            year,
            month,
            categories.Sum(c => c.Total),
            categories.Count(c => c.Category == "(uncategorized)") > 0,
            categories);
    }
}

public record EffectiveTransactionRow(
    string TransactionId,
    DateOnly Date,
    string Name,
    decimal Amount,
    string RawCategory,
    string EffectiveCategory);

public record EffectiveCategoryGroup(
    string Category,
    decimal Total,
    int Count,
    List<EffectiveTransactionRow> Transactions);

public record EffectiveReportResult(
    int? Year,
    int? Month,
    decimal GrandTotal,
    bool HasUncategorized,
    List<EffectiveCategoryGroup> Categories);
```

Notes:
- `RawCategory` is surfaced so the UI can show `raw → effective` when they differ.
- Rules are loaded once per request and applied in memory — no SQL translation concerns, trivially testable.

## 5. API — `CashOut/Controllers/CategoryRulesController.cs`

Route follows kebab-case convention. Thin controller, logic in the service.

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

    public record UpsertRequest(string Pattern, string Category);

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var rules = await _rules.GetAll();
        var names = await _db.Transactions.Select(t => t.Name).Distinct().ToListAsync();

        var response = rules.Select(r => new
        {
            r.Id,
            r.Pattern,
            r.Category,
            MatchCount = names.Count(n => n.Contains(r.Pattern, StringComparison.OrdinalIgnoreCase)),
        });

        return Ok(response);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UpsertRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Pattern) || string.IsNullOrWhiteSpace(req.Category))
            return BadRequest("Pattern and Category are required.");
        var rule = await _rules.Create(req.Pattern, req.Category);
        return Ok(rule);
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpsertRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Pattern) || string.IsNullOrWhiteSpace(req.Category))
            return BadRequest("Pattern and Category are required.");
        var rule = await _rules.Update(id, req.Pattern, req.Category);
        return rule == null ? NotFound() : Ok(rule);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await _rules.Delete(id);
        return deleted ? Ok() : NotFound();
    }

    [HttpGet("report")]
    public async Task<IActionResult> Report(
        [FromQuery] int? year, [FromQuery] int? month)
        => Ok(await _rules.GetEffectiveReport(year, month));
}
```

## 6. Page — `CashOut/Pages/CategoryRules.razor`

Route `/category-rules`. Two stacked sections:

**Section 1 — Rules**

- MudTable listing rules: Pattern, Category, MatchCount, actions (edit, delete).
- Add-rule form row at top (two text fields + save button).
- Edit switches the row into inline edit mode; delete asks for confirm.
- After every mutation, reload the list and the preview so both stay in sync.

**Section 2 — Categorized Preview Report**

- Year/month filters (year dropdown sourced from `api/settings/years`, month tabs optional — mirror the Transactions page pattern).
- Summary chips: grand total, uncategorized count.
- Grouped MudTable by effective category: Category, Total, Count, expandable transaction rows.
- Each expanded transaction row shows Name, Date, Amount, and Raw → Effective category pair; highlight rows where `RawCategory != EffectiveCategory` so the effect of each rule is obvious at a glance.

Data flow: the page calls `api/category-rules` and `api/category-rules/report?year=&month=` via the injected `HttpClient`, same as other pages.

## 7. Wiring

### `CashOut/Program.cs` — DI registration

```csharp
builder.Services.AddScoped<CategoryRuleService>();
```

### `CashOut/Shared/MainLayout.razor` — nav link

Under the Reports section, after "By Category":

```razor
<MudNavLink Href="/category-rules"
            Icon="@Icons.Material.Filled.Rule">
    Category Rules
</MudNavLink>
```

## Tests — `CashOut.Tests/CategoryRuleServiceTests.cs`

MSTest, in-memory EF, unique database name per test (`nameof(MethodName)`), direct instantiation, `MethodName_Scenario_ExpectedBehavior` naming:

- `Match_ContainsPattern_CaseInsensitive_MatchesStoreVariants` — rule `Food Lion` matches `Food Lion #0070`, `food lion #4409`
- `Match_MultipleMatches_LongestPatternWins` — `Food Lion Pharmacy` beats `Food Lion`
- `Match_MultipleMatches_SameLength_OldestRuleWins`
- `Match_NoMatch_ReturnsNull`
- `EffectiveCategory_RuleOverridesExistingRawCategory`
- `EffectiveCategory_NoMatch_KeepsRawCategory`
- `EffectiveCategory_NoMatch_BlankCategory_ReturnsUncategorized`
- `GetEffectiveReport_GroupsByEffectiveCategory_AndSumsTotals`
- `Create_TrimsPatternAndCategory`
- `Update_UnknownId_ReturnsNull`
- `Delete_ExistingId_RemovesRule`

CRUD tests double as regression coverage for the trim/validation path enforced again in the controller.

## Verification

```bash
dotnet build CashOut/CashOut.csproj
dotnet test --filter "TestCategory!=UI"
```

Manual smoke test against the full Docker stack:

1. Open `/category-rules`, create rule `Food Lion` → `Groceries`.
2. Confirm MatchCount > 0 and the preview shows affected transactions grouped under `Groceries` with their raw values preserved in the detail rows.
3. Delete the rule and confirm the preview reverts to raw categories immediately (proves query-time resolution).
4. Check `transactions` table contents are byte-identical before/after (no writes occurred).

## Future Phases

1. **Adopt in existing reports** — inject rules into `ReportService` and swap `CategoryKey(t)` for an effective-category resolver behind a toggle on the By Category report.
2. **Transactions page display** — show effective category (read-only badge or column) while keeping the stored value untouched.
3. **Rule suggestions** — scan uncategorized transactions for repeated name prefixes (strip trailing `#NNNN` / digit runs) and offer one-click rule creation with live match previews.
