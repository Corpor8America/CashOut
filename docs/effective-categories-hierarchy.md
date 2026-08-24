# Effective Categories — Hierarchy Extension

This is a **companion doc** to [effective-categories.md](effective-categories.md). The base doc stands alone: implement it as written if you want flat effective categories, and ignore everything here.

If you want drill-down categories ("Income / Payroll / Company 1"), implement the **base design first**, then apply the deltas in this document on top. Sections below map 1:1 onto the base doc's sections and describe only what changes.

## Relationship to the Base Design

| Base section | Status in this doc |
|---|---|
| §1 `Category.cs` | **Amended** — adds `ParentCategoryId`, `Parent`, `Children` |
| §2 `CategoryRule.cs` | Unchanged |
| §3 `Transaction.cs` | Unchanged |
| §4 `AppDbContext.cs` | **Amended** — global unique index on `Name` dropped, self-FK added |
| §5 Migration | **Additional migration** — `AddCategoryHierarchy` |
| §6 `CategoryService` | **Replaced** — parent-aware CRUD, path helpers, cycle/delete guards |
| §7 `CategoryRuleService` | Logic unchanged; display uses full path |
| §8–§9 `TransactionService` / `CsvImportService` | Unchanged |
| §10 `CategoriesController` | **Amended** — `ParentCategoryId` in requests, path-aware list, conflict responses |
| §11 `CategoryRulesController` | Optional tweak — expose category path |
| §12 `TransactionsController` | Response adds category path (optional) |
| §13 `Transactions.razor` | **Amended** — assignment dialog dropdown becomes path/tree select |
| §14 `CategoryRules.razor` | **Amended** — categories shown as indented tree, "add subcategory" action |
| §15 `ReportEffectiveCategory.razor` | **Replaced grouping** — rollup tree with expandable rows |
| §16 Wiring | Unchanged |
| §17–§18 Tests | Additional cases listed in §10 here |
| §19 Verification | Extended smoke-test steps |

## Goal

Extend effective categories with an optional parent/child hierarchy so reports can drill down:

```
Income                        ← level 1 (rolled up)
  Payroll                     ← level 2 (rolled up)
    Company 1                 ← level 3 (leaf, holds transactions)
    Company 2                 ← level 3 (leaf, holds transactions)
  Refunds                     ← level 2 (may hold transactions directly)
Expenses                      ← level 1
  Groceries
```

- Any number of levels; nesting is optional per branch.
- Transactions may be assigned to **any level**, not only leaves.
- Reports show rolled-up totals at every level plus the leaf detail underneath.

## Non-Goals

- No drag-and-drop reordering — position in reports is always amount-based
- No stored materialized path column — full paths are computed at read time
- No per-node colors/icons/templates
- No changes to the rule engine — a rule still maps one pattern → one category node
- No forced uniform depth — one branch can be 1 level deep, another 3+

## Design Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Hierarchy model | Adjacency list: nullable self-FK `Category.ParentCategoryId` | Minimal schema change; arbitrary depth; EF Core handles it natively |
| Assignment level | Any level | Keeps simple categories usable without forcing dummy subcategories; parents are still meaningful in reports |
| Rollup | Computed in memory during report build (DFS over the tree) | Data volumes are tiny (personal finance CSV imports); no recursive SQL needed |
| Name uniqueness | Unique among **siblings**, enforced in `CategoryService` | Postgres unique indexes treat NULLs as distinct (roots would bypass a `(parent_id, name)` index), so the service layer is the reliable enforcement point. Same name under *different* parents is allowed |
| Cycle prevention | Reject moves where the new parent is the category itself or one of its descendants | Prevents unreachable subtrees |
| Deleting a parent | Blocked while child categories exist | Deterministic and zero-surprise; matches base doc's "no retroactive overwriting" philosophy. User deletes/moves children first |
| Full-path display | Computed `" / "` join of ancestor names (e.g. `Income / Payroll / Company 1`) | Always accurate after renames/moves; nothing to sync |
| Report grouping key | `CategoryId`, never name/path strings | Two leaves named "Payroll" under different parents must not merge |

---

## 1. Amended Entity — `CashOut/Models/Category.cs`

Replace the base §1 entity with:

```csharp
public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int? ParentCategoryId { get; set; }
    public Category? Parent { get; set; }
    public List<Category> Children { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

## 2. Amended DbContext — `CashOut/Data/AppDbContext.cs`

Changes to the base §4 `Category` block:

```csharp
modelBuilder.Entity<Category>(e =>
{
    e.ToTable("categories");
    e.HasKey(x => x.Id);
    e.Property(x => x.Id).ValueGeneratedOnAdd();
    e.Property(x => x.Name).IsRequired();
    // NOTE: the base design's HasIndex(x => x.Name).IsUnique() is REMOVED.
    // Names are now unique among siblings only, enforced in CategoryService.
    e.HasIndex(x => x.ParentCategoryId);
    e.Property(x => x.ParentCategoryId).IsRequired(false);
    e.HasOne(x => x.Parent)
        .WithMany(x => x.Children)
        .HasForeignKey(x => x.ParentCategoryId)
        .OnDelete(DeleteBehavior.Restrict);   // DB backs the "no delete with children" rule
    e.Property(x => x.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");
    e.Property(x => x.UpdatedAt).HasDefaultValueSql("now() at time zone 'utc'");
});
```

Everything else in base §4 (`category_rules`, `transactions` config) is unchanged.

## 3. Migration

Per AGENTS.md workflow (run from the repo root):

```bash
docker-compose -f docker-compose.dev.yml up db -d
dotnet ef migrations add AddCategoryHierarchy --project CashOut
dotnet build CashOut/CashOut.csproj
```

Verify the migration drops the unique index on `categories.name`, adds the nullable `parent_category_id` column with an index, and does **not** cascade-delete from parent to child.

## 4. Replaced Service — `CashOut/Services/CategoryService.cs`

Full replacement for base §6. Uncategorization logic in `Delete` is identical to the base design and abbreviated here.

```csharp
public class CategoryService
{
    private readonly AppDbContext _db;

    public CategoryService(AppDbContext db) { _db = db; }

    public record CategoryRow(
        int Id,
        string Name,
        string Path,
        int Depth,
        int? ParentCategoryId);

    public async Task<List<CategoryRow>> GetAllRows()
    {
        var categories = await _db.Categories.AsNoTracking().ToListAsync();
        return OrderByPath(categories);
    }

    private List<CategoryRow> OrderByPath(List<Category> categories)
    {
        var byId = categories.ToDictionary(c => c.Id);

        string PathOf(Category c)
        {
            var parts = new List<string>();
            var cur = c;
            while (cur != null)
            {
                parts.Insert(0, cur.Name);
                cur = cur.ParentCategoryId.HasValue
                    ? byId.GetValueOrDefault(cur.ParentCategoryId.Value)
                    : null;
            }
            return string.Join(" / ", parts);
        }

        int DepthOf(Category c)
        {
            var d = 0;
            var cur = c;
            while (cur?.ParentCategoryId != null)
            {
                d++;
                cur = byId.GetValueOrDefault(cur.ParentCategoryId.Value);
            }
            return d;
        }

        return categories
            .Select(c => new CategoryRow(
                c.Id, c.Name, PathOf(c), DepthOf(c), c.ParentCategoryId))
            .OrderBy(r => r.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<(Category? Category, string? Error)> Create(
        string name, int? parentCategoryId = null)
    {
        var trimmed = name.Trim();
        if (await SiblingNameTaken(trimmed, parentCategoryId, excludeId: null))
            return (null, $"A category named \"{trimmed}\" already exists at this level.");

        var category = new Category
        {
            Name = trimmed,
            ParentCategoryId = parentCategoryId,
            UpdatedAt = DateTime.UtcNow,
        };
        _db.Categories.Add(category);
        await _db.SaveChangesAsync();
        return (category, null);
    }

    public async Task<(Category? Category, string? Error)> Update(
        int id, string name, int? parentCategoryId)
    {
        var category = await _db.Categories.FindAsync(id);
        if (category == null) return (null, null);

        var trimmed = name.Trim();

        if (parentCategoryId == id)
            return (null, "A category cannot be its own parent.");

        if (parentCategoryId.HasValue && await WouldCreateCycle(id, parentCategoryId.Value))
            return (null, "Cannot move a category under one of its own subcategories.");

        if (await SiblingNameTaken(trimmed, parentCategoryId, excludeId: id))
            return (null, $"A category named \"{trimmed}\" already exists at this level.");

        category.Name = trimmed;
        category.ParentCategoryId = parentCategoryId;
        category.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return (category, null);
    }

    public async Task<(bool Deleted, string? Error)> Delete(int id)
    {
        var category = await _db.Categories.FindAsync(id);
        if (category == null) return (false, null);

        if (await _db.Categories.AnyAsync(c => c.ParentCategoryId == id))
            return (false, "Delete or move this category's subcategories first.");

        // ... uncategorize manual/rule-assigned transactions and remove
        //     pointing rules exactly as in base design §6 Delete() ...

        _db.Categories.Remove(category);
        await _db.SaveChangesAsync();
        return (true, null);
    }

    private async Task<bool> SiblingNameTaken(
        string name, int? parentCategoryId, int? excludeId)
    {
        return await _db.Categories.AnyAsync(c =>
            c.ParentCategoryId == parentCategoryId
            && (excludeId == null || c.Id != excludeId)
            && c.Name == name);
    }

    private async Task<bool> WouldCreateCycle(int categoryId, int newParentId)
    {
        var all = await _db.Categories.AsNoTracking().ToListAsync();
        var byId = all.ToDictionary(c => c.Id);
        var cur = byId.GetValueOrDefault(newParentId);
        while (cur != null)
        {
            if (cur.Id == categoryId) return true;
            cur = cur.ParentCategoryId.HasValue
                ? byId.GetValueOrDefault(cur.ParentCategoryId.Value)
                : null;
        }
        return false;
    }
}
```

Notes:

- Method signatures differ from the base design (`(result, error)` tuples instead of bare results) so controllers can return meaningful conflict responses. Update callers accordingly.
- `CategoryRow.Path` is the canonical display form everywhere (dropdowns, filters, reports, CSV).
- Ordering by `Path` means a parent always sorts adjacent to its subtree, giving a natural indented listing from a flat query.

## 5. Unchanged Service — `CashOut/Services/CategoryRuleService.cs`

No logic changes. Rules continue to point at a single category node regardless of depth. Only cosmetic: wherever the base design surfaces `r.Category.Name` (rules list, match preview), prefer the row's `Path` from `CategoryService.GetAllRows()` looked up by `CategoryId`, so users can tell two identically-named nodes apart.

## 6. Amended API — `CashOut/Controllers/CategoriesController.cs`

```csharp
[HttpGet]
public async Task<IActionResult> List()
    => Ok(await _categories.GetAllRows());

[HttpPost]
public async Task<IActionResult> Create([FromBody] UpsertRequest req)
{
    if (string.IsNullOrWhiteSpace(req.Name))
        return BadRequest("Name is required.");
    var (category, error) = await _categories.Create(req.Name, req.ParentCategoryId);
    return error != null ? Conflict(error) : Ok(category);
}

[HttpPatch("{id}")]
public async Task<IActionResult> Update(int id, [FromBody] UpsertRequest req)
{
    if (string.IsNullOrWhiteSpace(req.Name))
        return BadRequest("Name is required.");
    var (category, error) = await _categories.Update(id, req.Name, req.ParentCategoryId);
    if (category == null && error == null) return NotFound();
    return error != null ? Conflict(error) : Ok(category);
}

[HttpDelete("{id}")]
public async Task<IActionResult> Delete(int id)
{
    var (deleted, error) = await _categories.Delete(id);
    if (!deleted && error == null) return NotFound();
    return error != null ? Conflict(error) : Ok();
}

public record UpsertRequest(string Name, int? ParentCategoryId);
```

The list response is now `[{ id, name, path, depth, parentCategoryId }, ...]` sorted by path.

## 7. Amended Page — `CashOut/Pages/Transactions.razor`

Delta on top of base §13:

- The assignment dialog's category dropdown sources `GET api/categories` rows and displays `Path` (the slashes make the hierarchy readable without a tree widget):

```razor
<MudSelect T="int?" Label="Category" @bind-Value="_selectedCategoryId" Clearable="true">
    <MudSelectItem T="int?" Value="@((int?)null)">@(null)</MudSelectItem>
    @foreach (var row in _categoryRows)
    {
        <MudSelectItem T="int?" Value="@row.Id">@row.Path</MudSelectItem>
    }
</MudSelect>
```

- The effective-category filter popover uses the same rows; filter values are `CategoryId`s (not path strings), and the badge/list shows `Path`.
- Everything else in base §13 (columns, assignment flow, clear-assignment option) is unchanged.

## 8. Amended Page — `CashOut/Pages/CategoryRules.razor`

Delta on top of base §14:

### Section 1 — Categories (tree view)

- Render `_categoryRows` in a flat MudTable (already path-sorted) and indent the Name cell by `row.Depth * 20px`; visually this reads as a tree:

```razor
<Style="@($"padding-left:{row.Depth * 20}px")">@row.Name</Style>
```

- Each row gets an extra **"Add subcategory"** action that prefills the add-form's parent with `row.Id`.
- The add/edit form gains a parent dropdown (rows minus the edited category itself — the server enforces the deeper descendant/cycle checks anyway).
- Delete shows the server's conflict message (e.g. "Delete or move this category's subcategories first.") as a snackbar.

### Section 2 — Rules

Unchanged except the Category column shows the full path for clarity.

## 9. Replaced Report — `CashOut/Pages/ReportEffectiveCategory.razor`

This replaces the grouping strategy in base §15. Header stats (total income/expenses/net, averages, counts) come from the same pipeline as `GetCategoryDetail` and are unchanged.

### Result Shape — `CashOut/Models/ReportDtos.cs`

```csharp
public record EffectiveCategoryDetailRow(
    int CategoryId,                       // 0 for the "(uncategorized)" bucket
    string Path,                          // "(uncategorized)" for the bucket
    int Depth,
    bool HasChildren,
    decimal DirectTotal,                  // transactions assigned to this exact node
    decimal Total,                        // DirectTotal rolled up over all descendants
    decimal AvgPerMonth,
    int Count,                            // rolled up transaction count
    List<EffectiveCategoryDetailRow> Children,
    List<CategoryDetailTransactionRow> Transactions);

public record EffectiveCategoryDetailReportResult(
    int FromYear, int FromMonth, int ToYear, int ToMonth,
    decimal TotalIncome, decimal TotalExpenses, decimal NetCashFlow,
    decimal AvgMonthlyIncome, decimal AvgMonthlyExpenses, decimal AvgMonthlyNet,
    int TransactionCount,
    List<EffectiveCategoryDetailRow> Categories);
```

### Service — `ReportService.GetEffectiveCategoryDetail()`

```csharp
public async Task<EffectiveCategoryDetailReportResult> GetEffectiveCategoryDetail(
    int? fromYear = null, int? fromMonth = null,
    int? toYear = null, int? toMonth = null,
    string? accountId = null)
{
    // Date-range + account filtering identical to GetCategoryDetail (reuse it),
    // but Include(t => t.EffectiveCategory):
    var txns = await currentQuery.Include(t => t.EffectiveCategory).ToListAsync();

    var categories = await _db.Categories.AsNoTracking().ToListAsync();
    var byId = categories.ToDictionary(c => c.Id);

    var directTotals = new Dictionary<int, decimal>();
    var directCounts = new Dictionary<int, int>();
    var txnGroups = new Dictionary<int, List<Transaction>>();
    var uncategorized = new List<Transaction>();

    foreach (var t in txns)
    {
        if (!t.CategoryId.HasValue) { uncategorized.Add(t); continue; }
        var id = t.CategoryId.Value;
        directTotals[id] = directTotals.GetValueOrDefault(id) + t.Amount;
        directCounts[id] = directCounts.GetValueOrDefault(id) + 1;
        if (!txnGroups.TryGetValue(id, out var list)) txnGroups[id] = list = new();
        list.Add(t);
    }

    // Ancestors of assigned nodes appear even with zero direct transactions,
    // otherwise the tree breaks mid-branch.
    var nodeIds = new HashSet<int>();
    foreach (var id in directTotals.Keys)
    {
        var cur = byId.GetValueOrDefault(id);
        while (cur != null)
        {
            nodeIds.Add(cur.Id);
            cur = cur.ParentCategoryId.HasValue
                ? byId.GetValueOrDefault(cur.ParentCategoryId.Value)
                : null;
        }
    }

    EffectiveCategoryDetailRow Build(int id)
    {
        var cat = byId[id];
        var childRows = categories
            .Where(c => c.ParentCategoryId == id && nodeIds.Contains(c.Id))
            .Select(c => Build(c.Id))
            .OrderByDescending(r => r.Total)
            .ToList();
        var directTotal = directTotals.GetValueOrDefault(id);
        var total = directTotal + childRows.Sum(r => r.Total);
        var count = directCounts.GetValueOrDefault(id) + childRows.Sum(r => r.Count);
        return new EffectiveCategoryDetailRow(
            id, CategoryServicePathOf(cat), DepthOf(cat),
            childRows.Count > 0,
            directTotal, total,
            Math.Round(total / monthsInRange, 2),
            count,
            childRows,
            (txnGroups.GetValueOrDefault(id) ?? new List<Transaction>())
                .OrderByDescending(t => t.Date)
                .ThenByDescending(t => Math.Abs(t.Amount))
                .Select(ToDetailTxnRow).ToList());
    }

    var roots = categories
        .Where(c => c.ParentCategoryId == null && nodeIds.Contains(c.Id))
        .Select(Build)
        .OrderByDescending(r => r.Total)
        .ToList();

    if (uncategorized.Count > 0)
        roots.Add(UncategorizedRowFrom(uncategorized));   // CategoryId 0, Depth 0

    return new EffectiveCategoryDetailReportResult(/* header stats ..., */ roots);
}
```

(`CategoryServicePathOf`/`DepthOf` mirror the helpers in `CategoryService` — factor them into shared private statics or a small helper class rather than duplicating.)

Key behaviors:

- Grouping key is `CategoryId`; identical names under different parents stay separate.
- Parent totals = own direct transactions + all descendants, so `Income` equals `Payroll + Refunds + …`.
- Nodes are sorted by rolled-up `Total`, children within parents likewise.
- Sign convention preserved: income branches carry positive totals, expense branches negative (same as base reports).

### UI — Expandable Rows

Route `/reports/effective-category`, inside `ReportShell` as in base §15, but the category table renders the tree:

- Use MudBlazor's hierarchical table mode (`MudTable` with `HierarchicalRows="true"` + `ChildrenSelector="r => r.Children"`), or a small recursive Razor component if more control is needed.
- Parent rows are expandable; expanded state reveals child rows, then that row's transaction detail (existing collapsible transaction-table pattern).
- Columns: Path (indented by `Depth`), DirectTotal (only shown when non-zero, so pure-rollup parents read cleanly), Total, AvgPerMonth, Count.
- Collapse-all / expand-all buttons in the toolbar; default view = level-1 rows collapsed.

### CSV Export

Flatten depth-first; every row carries `Path`, `Type` (`rollup` or `direct`), `DirectTotal`, `Total`, `Count`. Rolled-up rows let spreadsheet pivot tools reproduce the drill-down.

### Controller

Same as base §15 endpoint; it now serializes `EffectiveCategoryDetailReportResult`. Query parameters unchanged.

---

## 10. Additional Tests

On top of base §17–§18:

### `CategoryServiceTests.cs`

- `Create_WithParent_SetsParentCategoryId`
- `Create_DuplicateNameUnderSameParent_RejectedWithError`
- `Create_SameNameUnderDifferentParents_Allowed`
- `Update_MoveUnderSelf_Rejected`
- `Update_MoveUnderOwnDescendant_Rejected`
- `Update_RenameToExistingSiblingName_Rejected`
- `Delete_WithChildren_BlockedWithError`
- `Delete_LeafCategory_UncategorizesAndRemoves`
- `GetAllRows_BuildsPathsDepthsAndSortsByPath`

### `ReportServiceTests.cs` (new file)

- `GetEffectiveCategoryDetail_RollsUpDescendantTotalsIntoParents`
- `GetEffectiveCategoryDetail_CreatesAncestorNodesWithoutDirectTransactions`
- `GetEffectiveCategoryDetail_KeepsSameNamedLeavesSeparate`
- `GetEffectiveCategoryDetail_GroupsUnassignedUnderUncategorizedBucket`
- `GetEffectiveCategoryDetail_DirectVsRolledUpTotalsAreDistinct`

## 11. Verification

After completing base §19 verification:

```bash
dotnet build CashOut/CashOut.csproj
dotnet test CashOut.Tests/CashOut.Tests.csproj
```

Manual smoke test against the full Docker stack:

1. Open `/category-rules`, build the chain Income → Payroll → Company 1 (use "Add subcategory")
2. Create rule `ACME CORP` → Company 1; confirm affected transactions resolve to the full path
3. Assign another transaction directly to **Income** (any-level assignment)
4. Open `/reports/effective-category`: Income total = Payroll rollup + the direct Income transaction; expanding Income → Payroll → Company 1 shows the rule-assigned transactions
5. Rename `Company 1` → `Acme` and confirm paths update everywhere (rules list, filters, report) with no data changes
6. Attempt to move Payroll under Company 1 → rejected with cycle error
7. Attempt to create a second `Acme` under Payroll → rejected; creating `Acme` under a different parent → allowed
8. Attempt to delete Payroll while Company 1 exists → blocked; delete Company 1 (uncategorizes its transactions) then Payroll succeeds
9. Export the report CSV and confirm rollup rows + direct rows flatten correctly
