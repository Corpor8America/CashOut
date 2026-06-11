using Microsoft.EntityFrameworkCore;

/// <summary>
/// Resolves raw merchant names to canonical aliases and determines the effective category.
///
/// Category priority (highest to lowest):
/// 1. Alias category (user-defined canonical merchant)
/// 2. Raw business category (initialized from first transaction, never auto-overwritten)
/// 3. Plaid/CSV category (initialization only)
/// 4. Empty string (none)
/// </summary>
public class BusinessNormalizationService
{
    private readonly AppDbContext _db;

    public BusinessNormalizationService(AppDbContext db) => _db = db;

    /// <summary>
    /// Finds or creates a RawBusiness for the given merchant name.
    /// If creating, initializes the category from the provided value.
    /// If the raw business already exists, the category is NOT overwritten.
    /// Returns the raw business ID.
    /// </summary>
    public async Task<int> GetOrCreateRawBusiness(string rawName, string initialCategory = "")
    {
        var normalized = NormalizeName(rawName);

        var existing = await _db.RawBusinesses
            .FirstOrDefaultAsync(b => b.RawName == normalized);

        if (existing != null)
            return existing.Id;

        var newBusiness = new RawBusiness
        {
            RawName = normalized,
            Category = initialCategory,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.RawBusinesses.Add(newBusiness);
        await _db.SaveChangesAsync();
        return newBusiness.Id;
    }

    /// <summary>
    /// Resolves the effective display name and category for a transaction,
    /// applying alias overrides where configured.
    /// Returns (displayName, effectiveCategory).
    /// </summary>
    public async Task<(string displayName, string effectiveCategory)> Resolve(
        int? rawBusinessId, int? aliasId, string fallbackCategory = "")
    {
        if (aliasId.HasValue)
        {
            var alias = await _db.BusinessAliases.FindAsync(aliasId.Value);
            if (alias != null)
                return (alias.AliasName, string.IsNullOrEmpty(alias.Category)
                    ? fallbackCategory : alias.Category);
        }

        if (rawBusinessId.HasValue)
        {
            var raw = await _db.RawBusinesses.FindAsync(rawBusinessId.Value);
            if (raw != null)
                return (raw.RawName, string.IsNullOrEmpty(raw.Category)
                    ? fallbackCategory : raw.Category);
        }

        return ("", fallbackCategory);
    }

    /// <summary>
    /// Looks up the alias mapping for a raw business, if any.
    /// Returns the alias ID or null.
    /// </summary>
    public async Task<int?> GetAliasId(int rawBusinessId)
    {
        var map = await _db.RawBusinessAliasMaps
            .FirstOrDefaultAsync(m => m.RawBusinessId == rawBusinessId);
        return map?.AliasId;
    }

    /// <summary>
    /// Normalizes a raw merchant name for consistent matching.
    /// Trims whitespace and collapses internal whitespace.
    /// Case stored as-is but compared case-insensitively via DB index.
    /// </summary>
    public static string NormalizeName(string name) =>
        string.Join(" ", name.Trim().Split(' ',
            StringSplitOptions.RemoveEmptyEntries));

    // ── Admin operations ──────────────────────────────────────────────────

    public async Task<List<RawBusiness>> GetAllRawBusinesses() =>
        await _db.RawBusinesses.OrderBy(b => b.RawName).ToListAsync();

    public async Task<List<BusinessAlias>> GetAllAliases() =>
        await _db.BusinessAliases.OrderBy(a => a.AliasName).ToListAsync();

    public async Task<List<RawBusinessAliasMap>> GetAllMappings() =>
        await _db.RawBusinessAliasMaps
            .Include(m => m.RawBusiness)
            .Include(m => m.Alias)
            .ToListAsync();

    public async Task UpdateRawBusinessCategory(int id, string category)
    {
        var raw = await _db.RawBusinesses.FindAsync(id)
            ?? throw new KeyNotFoundException($"RawBusiness {id} not found.");
        raw.Category = category;
        raw.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task<BusinessAlias> CreateAlias(string aliasName, string category)
    {
        var alias = new BusinessAlias
        {
            AliasName = aliasName.Trim(),
            Category = category,
            CreatedAt = DateTime.UtcNow
        };
        _db.BusinessAliases.Add(alias);
        await _db.SaveChangesAsync();
        return alias;
    }

    public async Task MapRawToAlias(int rawBusinessId, int aliasId)
    {
        // Remove existing mapping if any
        var existing = await _db.RawBusinessAliasMaps
            .FirstOrDefaultAsync(m => m.RawBusinessId == rawBusinessId);
        if (existing != null)
            _db.RawBusinessAliasMaps.Remove(existing);

        _db.RawBusinessAliasMaps.Add(new RawBusinessAliasMap
        {
            RawBusinessId = rawBusinessId,
            AliasId = aliasId
        });
        await _db.SaveChangesAsync();
    }

    public async Task RemoveMapping(int rawBusinessId)
    {
        var map = await _db.RawBusinessAliasMaps
            .FirstOrDefaultAsync(m => m.RawBusinessId == rawBusinessId);
        if (map != null)
        {
            _db.RawBusinessAliasMaps.Remove(map);
            await _db.SaveChangesAsync();
        }
    }
}