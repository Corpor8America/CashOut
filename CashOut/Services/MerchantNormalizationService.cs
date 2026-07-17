using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

/// <summary>
/// Implements merchant normalization, alias matching, raw-business reconstruction,
/// and retroactive transaction matching.
/// </summary>
public class MerchantNormalizationService
{
    private readonly AppDbContext _db;

    public const string Unassigned = "Unassigned";

    public MerchantNormalizationService(AppDbContext db) => _db = db;

    public static string Normalize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        var s = raw.Trim();
        s = Regex.Replace(s, @"\s*\([^)]*\)\s*$", " ");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        s = s.ToUpperInvariant();
        s = Regex.Replace(s, @"[-*./:,#]", " ");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        s = Regex.Replace(s, @"\b\d{7,}\b", " ");
        s = Regex.Replace(s, @"\s+", " ").Trim();

        return s;
    }

    public async Task<BusinessAlias?> MatchAlias(string normalizedMerchant)
    {
        if (string.IsNullOrEmpty(normalizedMerchant)) return null;

        var patterns = await _db.AliasPatterns
            .Include(p => p.Alias)
            .AsNoTracking()
            .OrderBy(p => p.AliasId)
            .ThenBy(p => p.Id)
            .ToListAsync();

        return MatchAliasFromPatterns(normalizedMerchant, patterns);
    }

    public static BusinessAlias? MatchAliasFromPatterns(
        string normalizedMerchant, IEnumerable<AliasPattern> patterns)
    {
        if (string.IsNullOrEmpty(normalizedMerchant)) return null;

        foreach (var pattern in patterns.OrderBy(p => p.AliasId).ThenBy(p => p.Id))
        {
            if (PatternMatches(pattern, normalizedMerchant))
                return pattern.Alias;
        }

        return null;
    }

    private static bool PatternMatches(AliasPattern pattern, string normalized)
    {
        return pattern.MatchType switch
        {
            AliasPatternMatchType.Contains =>
                normalized.Contains(pattern.Pattern, StringComparison.OrdinalIgnoreCase),
            AliasPatternMatchType.StartsWith =>
                normalized.StartsWith(pattern.Pattern, StringComparison.OrdinalIgnoreCase),
            AliasPatternMatchType.Regex =>
                Regex.IsMatch(normalized, pattern.Pattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            _ => false
        };
    }

    public async Task<(BusinessAlias? alias, RawBusiness? rawBusiness, string normalizedName, string effectiveCategory)> Resolve(
        string rawName, string categoryRaw = "")
    {
        var normalized = Normalize(rawName);
        var alias = await MatchAlias(normalized);

        if (alias != null)
            return (alias, null, normalized, EffectiveCategory(alias));

        var raw = await EnsureRawBusiness(rawName, normalized, categoryRaw);
        return (null, raw, normalized, EffectiveCategory(null));
    }

    public Task<(BusinessAlias? alias, RawBusiness? rawBusiness, string normalizedName, string effectiveCategory)> ResolveBulk(
        string rawName, string categoryRaw,
        IList<AliasPattern> allPatterns,
        Dictionary<string, RawBusiness> rawByNormalized)
    {
        var normalized = Normalize(rawName);
        var alias = MatchAliasFromPatterns(normalized, allPatterns);

        if (alias != null)
            return Task.FromResult<(BusinessAlias?, RawBusiness?, string, string)>(
                (alias, null, normalized, EffectiveCategory(alias)));

        if (!rawByNormalized.TryGetValue(normalized, out var raw))
        {
            raw = new RawBusiness
            {
                RawName = rawName,
                RawNameNormalized = normalized,
                CategoryRaw = categoryRaw,
                IsMapped = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.RawBusinesses.Add(raw);
            rawByNormalized[normalized] = raw;
        }

        return Task.FromResult<(BusinessAlias?, RawBusiness?, string, string)>(
            (null, raw, normalized, EffectiveCategory(null)));
    }

    private async Task<RawBusiness> EnsureRawBusiness(
        string rawName, string normalized, string categoryRaw)
    {
        var existing = await _db.RawBusinesses
            .FirstOrDefaultAsync(b => b.RawNameNormalized == normalized);

        if (existing != null) return existing;

        var raw = new RawBusiness
        {
            RawName = rawName,
            RawNameNormalized = normalized,
            CategoryRaw = categoryRaw,
            IsMapped = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.RawBusinesses.Add(raw);
        return raw;
    }

    /// <summary>
    /// Category is determined solely by alias. If no alias (or alias has no category),
    /// the transaction is always Unassigned — incoming CSV/Plaid categories are discarded.
    /// </summary>
    private static string EffectiveCategory(BusinessAlias? alias)
    {
        if (!string.IsNullOrWhiteSpace(alias?.Category)) return alias.Category;
        return Unassigned;
    }

    public async Task<PatternTestResult> TestPattern(string rawInput)
    {
        var normalized = Normalize(rawInput);
        var alias = await MatchAlias(normalized);
        var effectiveCategory = EffectiveCategory(alias);

        return new PatternTestResult(
            RawInput: rawInput,
            Normalized: normalized,
            MatchedAliasId: alias?.Id,
            MatchedAliasName: alias?.AliasName,
            EffectiveCategory: effectiveCategory);
    }

    public async Task<List<RawBusiness>> GetUnmappedBusinesses() =>
        await _db.RawBusinesses
            .Where(b => !b.IsMapped)
            .OrderBy(b => b.RawNameNormalized)
            .ToListAsync();

    public async Task<List<RawBusiness>> GetAllRawBusinesses() =>
        await _db.RawBusinesses
            .OrderBy(b => b.RawNameNormalized)
            .ToListAsync();

    public async Task<List<BusinessAlias>> GetAllAliases() =>
        await _db.BusinessAliases
            .Include(a => a.Patterns)
            .OrderBy(a => a.AliasName)
            .ToListAsync();

    public async Task<(BusinessAlias alias, int matched)> CreateAlias(string aliasName, string category = "")
    {
        var alias = new BusinessAlias
        {
            AliasName = aliasName.Trim(),
            Category = category,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.BusinessAliases.Add(alias);
        await _db.SaveChangesAsync();
        return (alias, 0);
    }

    public async Task UpdateAliasName(int aliasId, string aliasName)
    {
        var trimmed = aliasName.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new ArgumentException("AliasName is required.");

        var alias = await _db.BusinessAliases.FindAsync(aliasId)
            ?? throw new KeyNotFoundException($"Alias {aliasId} not found.");
        alias.AliasName = trimmed;
        alias.UpdatedAt = DateTime.UtcNow;

        // Propagate new name to all matched transactions
        var transactions = await _db.Transactions
            .Where(t => t.AliasId == aliasId)
            .ToListAsync();
        foreach (var txn in transactions)
        {
            txn.Name = trimmed;
            txn.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
    }

    public async Task UpdateAliasCategory(int aliasId, string category)
    {
        var alias = await _db.BusinessAliases.FindAsync(aliasId)
            ?? throw new KeyNotFoundException($"Alias {aliasId} not found.");
        alias.Category = category;
        alias.UpdatedAt = DateTime.UtcNow;

        // Propagate new effective category to all matched transactions
        var effectiveCategory = EffectiveCategory(alias);
        var transactions = await _db.Transactions
            .Where(t => t.AliasId == aliasId)
            .ToListAsync();
        foreach (var txn in transactions)
        {
            txn.Category = effectiveCategory;
            txn.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
    }

    public async Task<AliasPattern> AddPattern(
        int aliasId, string pattern, AliasPatternMatchType matchType)
    {
        _ = await _db.BusinessAliases.FindAsync(aliasId)
            ?? throw new KeyNotFoundException($"Alias {aliasId} not found.");

        var storedPattern = matchType == AliasPatternMatchType.Regex
            ? pattern.Trim()
            : Normalize(pattern);

        if (string.IsNullOrEmpty(storedPattern))
            throw new ArgumentException("Pattern cannot be empty after normalization.");

        var ap = new AliasPattern
        {
            AliasId = aliasId,
            Pattern = storedPattern,
            MatchType = matchType,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.AliasPatterns.Add(ap);
        await _db.SaveChangesAsync();

        await RetroactivelyMap();
        return ap;
    }

    public async Task RemovePattern(int patternId)
    {
        var pattern = await _db.AliasPatterns.FindAsync(patternId);
        if (pattern == null) return;

        _db.AliasPatterns.Remove(pattern);
        await _db.SaveChangesAsync();
    }

    public async Task<int> DeleteAlias(int aliasId)
    {
        var alias = await _db.BusinessAliases
            .Include(a => a.Patterns)
            .FirstOrDefaultAsync(a => a.Id == aliasId)
            ?? throw new KeyNotFoundException($"Alias {aliasId} not found.");

        // Find RawBusiness entities mapped to this alias and unmap them
        var rawBusinessMapsToUnmap = await _db.RawBusinessAliasMaps
            .Where(m => m.AliasId == aliasId)
            .Include(m => m.RawBusiness)
            .ToListAsync();

        foreach (var map in rawBusinessMapsToUnmap)
        {
            if (map.RawBusiness != null)
            {
                map.RawBusiness.IsMapped = false;
                map.RawBusiness.UpdatedAt = DateTime.UtcNow;
            }
        }
        _db.RawBusinessAliasMaps.RemoveRange(rawBusinessMapsToUnmap);

        var affected = await _db.Transactions
            .Where(t => t.AliasId == aliasId)
            .OrderBy(t => t.Date)
            .ThenBy(t => t.TransactionId)
            .ToListAsync();

        _db.BusinessAliases.Remove(alias);

        foreach (var txn in affected)
        {
            txn.AliasId = null;
            txn.Alias = null;
            txn.Name = txn.RawName;
            txn.UpdatedAt = DateTime.UtcNow;
        }

        await ReprocessUnaliasedTransactions(affected);
        await CleanupRawBusinesses();
        await _db.SaveChangesAsync();
        return affected.Count;
    }

    public async Task MapRawToAlias(int rawBusinessId, int aliasId)
    {
        var raw = await _db.RawBusinesses.FindAsync(rawBusinessId)
            ?? throw new KeyNotFoundException($"RawBusiness {rawBusinessId} not found.");
        var alias = await _db.BusinessAliases.FindAsync(aliasId)
            ?? throw new KeyNotFoundException($"Alias {aliasId} not found.");

        raw.IsMapped = true;
        raw.UpdatedAt = DateTime.UtcNow;

        var transactions = await _db.Transactions
            .Where(t => t.RawBusinessId == rawBusinessId && t.AliasId == null)
            .ToListAsync();

        foreach (var txn in transactions)
        {
            txn.AliasId = aliasId;
            txn.Alias = alias;
            txn.RawBusinessId = null;
            txn.RawBusiness = null;
            txn.Name = alias.AliasName;
            txn.Category = EffectiveCategory(alias);
            txn.UpdatedAt = DateTime.UtcNow;
        }

        var existing = await _db.RawBusinessAliasMaps
            .FirstOrDefaultAsync(m => m.RawBusinessId == rawBusinessId);
        if (existing == null)
        {
            _db.RawBusinessAliasMaps.Add(new RawBusinessAliasMap
            {
                RawBusinessId = rawBusinessId,
                AliasId = aliasId
            });
        }
        else
        {
            existing.AliasId = aliasId;
        }

        await _db.SaveChangesAsync();
        await CleanupRawBusinesses();
        await _db.SaveChangesAsync();
    }

    public async Task UnmapRawBusiness(int rawBusinessId)
    {
        var raw = await _db.RawBusinesses.FindAsync(rawBusinessId);
        if (raw == null) return;

        raw.IsMapped = false;
        raw.UpdatedAt = DateTime.UtcNow;

        var map = await _db.RawBusinessAliasMaps
            .FirstOrDefaultAsync(m => m.RawBusinessId == rawBusinessId);
        if (map != null)
            _db.RawBusinessAliasMaps.Remove(map);

        await _db.SaveChangesAsync();
    }

    public async Task<int> RetroactivelyMap()
    {
        var candidates = await _db.Transactions
            .Where(t => t.AliasId == null)
            .OrderBy(t => t.Date)
            .ThenBy(t => t.TransactionId)
            .ToListAsync();

        var count = await ReprocessUnaliasedTransactions(candidates);
        await _db.SaveChangesAsync();
        await CleanupRawBusinesses();
        await _db.SaveChangesAsync();
        return count;
    }

    private async Task<int> ReprocessUnaliasedTransactions(IList<Transaction> candidates)
    {
        var patterns = await _db.AliasPatterns
            .Include(p => p.Alias)
            .ToListAsync();
        var rawByNormalized = await _db.RawBusinesses
            .ToDictionaryAsync(b => b.RawNameNormalized);

        var count = 0;
        foreach (var txn in candidates)
        {
            var rawName = string.IsNullOrWhiteSpace(txn.RawName) ? txn.Name : txn.RawName;
            var normalized = Normalize(rawName);
            txn.NormalizedName = normalized;

            var alias = MatchAliasFromPatterns(normalized, patterns);
            if (alias != null)
            {
                txn.AliasId = alias.Id;
                txn.Alias = alias;
                txn.RawBusinessId = null;
                txn.RawBusiness = null;
                txn.Name = alias.AliasName;
                txn.Category = EffectiveCategory(alias);
                txn.UpdatedAt = DateTime.UtcNow;
                count++;
                continue;
            }

            if (!rawByNormalized.TryGetValue(normalized, out var raw))
            {
                raw = new RawBusiness
                {
                    RawName = rawName,
                    RawNameNormalized = normalized,
                    CategoryRaw = string.IsNullOrWhiteSpace(txn.Category) ||
                        txn.Category == Unassigned ? "" : txn.Category,
                    IsMapped = false,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _db.RawBusinesses.Add(raw);
                rawByNormalized[normalized] = raw;
            }

            txn.RawBusinessId = raw.Id == 0 ? null : raw.Id;
            txn.RawBusiness = raw;
            txn.AliasId = null;
            txn.Alias = null;
            txn.Category = Unassigned;
            txn.UpdatedAt = DateTime.UtcNow;
        }

        return count;
    }

    private async Task CleanupRawBusinesses()
    {
        var referencedRawIds = await _db.Transactions
            .Where(t => t.RawBusinessId != null)
            .Select(t => t.RawBusinessId!.Value)
            .Distinct()
            .ToListAsync();

        var emptyRawBusinesses = await _db.RawBusinesses
            .Where(b => !referencedRawIds.Contains(b.Id))
            .ToListAsync();

        if (emptyRawBusinesses.Count == 0) return;

        var emptyIds = emptyRawBusinesses.Select(b => b.Id).ToList();
        var maps = await _db.RawBusinessAliasMaps
            .Where(m => emptyIds.Contains(m.RawBusinessId))
            .ToListAsync();
        _db.RawBusinessAliasMaps.RemoveRange(maps);
        _db.RawBusinesses.RemoveRange(emptyRawBusinesses);
    }
}

public record PatternTestResult(
    string RawInput,
    string Normalized,
    int? MatchedAliasId,
    string? MatchedAliasName,
    string EffectiveCategory);