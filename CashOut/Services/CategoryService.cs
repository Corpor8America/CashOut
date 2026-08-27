using Microsoft.EntityFrameworkCore;

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

        var manualTxns = await _db.Transactions
            .Where(t => t.CategoryId == id && t.CategoryRuleId == null)
            .ToListAsync();
        foreach (var t in manualTxns)
        {
            t.CategoryId = null;
            t.CategoryRuleId = null;
            t.UpdatedAt = DateTime.UtcNow;
        }

        var rules = await _db.CategoryRules
            .Where(r => r.CategoryId == id)
            .ToListAsync();
        _db.CategoryRules.RemoveRange(rules);

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
