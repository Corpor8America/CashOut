using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

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
        name = Regex.Replace(name, @"\s*#\d+\s*$", "");
        name = Regex.Replace(name, @"\s+\d{3,}\s*$", "");
        return name.Trim();
    }
}
