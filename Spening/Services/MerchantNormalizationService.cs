using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

/// <summary>
/// Implements the merchant normalization and alias pattern matching pipeline.
///
/// Import pipeline:
/// 1. Normalize the raw merchant string.
/// 2. Try to match against AliasPatterns (contains / starts_with / regex).
/// 3. If matched → return alias ID + alias category (or "Unassigned" if alias has no category).
/// 4. If no match  → create/reuse a RawBusiness, return null alias, "Unassigned" category.
///
/// CSV/Plaid categories are NEVER used for categorization — stored only as CategoryRaw on RawBusiness.
/// </summary>
public class MerchantNormalizationService
{
    private readonly AppDbContext _db;

    public const string Unassigned = "Unassigned";

    public MerchantNormalizationService(AppDbContext db) => _db = db;

    // ── Normalization ─────────────────────────────────────────────────────

    /// <summary>
    /// Normalizes a raw merchant string into a stable, comparable form:
    /// 1. Trim whitespace
    /// 2. Collapse multiple spaces
    /// 3. Convert to uppercase
    /// 4. Remove punctuation: - * . / :
    /// 5. Collapse spaces again
    /// 6. Remove long numeric sequences (>6 digits)
    /// 7. Remove parenthetical duplicates e.g. "(Wells Fargo Card Ccpymt)"
    /// 8. Final trim and space collapse
    /// </summary>
    public static string Normalize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        var s = raw.Trim();

        // Remove parenthetical suffixes that repeat the merchant name
        s = Regex.Replace(s, @"\s*\([^)]*\)\s*$", " ");

        // Collapse whitespace
        s = Regex.Replace(s, @"\s+", " ").Trim();

        // Uppercase
        s = s.ToUpperInvariant();

        // Remove punctuation
        s = Regex.Replace(s, @"[-*./:,]", " ");

        // Collapse whitespace again
        s = Regex.Replace(s, @"\s+", " ").Trim();

        // Remove numeric sequences longer than 6 digits
        s = Regex.Replace(s, @"\b\d{7,}\b", " ");

        // Final cleanup
        s = Regex.Replace(s, @"\s+", " ").Trim();

        return s;
    }

    // ── Pattern matching ──────────────────────────────────────────────────

    /// <summary>
    /// Tries to match a normalized merchant string against all AliasPatterns.
    /// Returns the alias with the lowest ID among all matches (deterministic).
    /// Returns null if no pattern matches.
    /// Patterns are loaded from DB each call; call once per transaction batch if performance matters.
    /// </summary>
    public async Task<BusinessAlias?> MatchAlias(string normalizedMerchant)
    {
        if (string.IsNullOrEmpty(normalizedMerchant)) return null;

        var patterns = await _db.AliasPatterns
            .Include(p => p.Alias)
            .OrderBy(p => p.AliasId)
            .ToListAsync();

        int? bestAliasId = null;
        BusinessAlias? bestAlias = null;

        foreach (var pattern in patterns)
        {
            if (PatternMatches(pattern, normalizedMerchant))
            {
                if (bestAliasId == null || pattern.AliasId < bestAliasId)
                {
                    bestAliasId = pattern.AliasId;
                    bestAlias = pattern.Alias;
                }
            }
        }

        return bestAlias;
    }

    /// <summary>
    /// In-memory version for bulk operations — accepts pre-loaded patterns.
    /// </summary>
    public static BusinessAlias? MatchAliasFromPatterns(
        string normalizedMerchant, IEnumerable<AliasPattern> patterns)
    {
        if (string.IsNullOrEmpty(normalizedMerchant)) return null;

        BusinessAlias? best = null;
        int? bestId = null;

        foreach (var p in patterns.OrderBy(p => p.AliasId))
        {
            if (PatternMatches(p, normalizedMerchant) &&
                (bestId == null || p.AliasId < bestId))
            {
                bestId = p.AliasId;
                best = p.Alias;
            }
        }

        return best;
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

    // ── Import pipeline ───────────────────────────────────────────────────

    /// <summary>
    /// Resolves a raw merchant name to an (aliasId?, effectiveCategory) pair.
    /// - Normalizes the raw name.
    /// - Tries to match an alias via patterns.
    /// - If matched: returns alias ID and its category (or "Unassigned" if category is empty).
    /// - If not matched: creates/reuses a RawBusiness and returns (null, "Unassigned").
    /// categoryRaw is stored on RawBusiness for reference only — never used for categorization.
    /// Does NOT call SaveChanges — caller is responsible.
    /// </summary>
    public async Task<(int? aliasId, string effectiveCategory)> Resolve(
        string rawName, string categoryRaw = "")
    {
        var normalized = Normalize(rawName);

        var alias = await MatchAlias(normalized);

        if (alias != null)
        {
            var category = string.IsNullOrEmpty(alias.Category) ? Unassigned : alias.Category;
            return (alias.Id, category);
        }

        // No match — create or reuse RawBusiness
        await EnsureRawBusiness(rawName, normalized, categoryRaw);
        return (null, Unassigned);
    }

    /// <summary>
    /// Bulk version of Resolve — pre-loads patterns and raw businesses for efficiency.
    /// Call for batch imports instead of calling Resolve per transaction.
    /// Does NOT call SaveChanges.
    /// </summary>
    public async Task<(int? aliasId, string effectiveCategory)> ResolveBulk(
        string rawName, string categoryRaw,
        IList<AliasPattern> allPatterns,
        Dictionary<string, RawBusiness> rawByNormalized)
    {
        var normalized = Normalize(rawName);
        var alias = MatchAliasFromPatterns(normalized, allPatterns);

        if (alias != null)
        {
            var category = string.IsNullOrEmpty(alias.Category) ? Unassigned : alias.Category;
            return (alias.Id, category);
        }

        // No match — ensure RawBusiness exists (in-memory upsert)
        if (!rawByNormalized.TryGetValue(normalized, out _))
        {
            var rb = new RawBusiness
            {
                RawName = rawName,
                RawNameNormalized = normalized,
                CategoryRaw = categoryRaw,
                IsMapped = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.RawBusinesses.Add(rb);
            rawByNormalized[normalized] = rb;
        }

        return (null, Unassigned);
    }

    private async Task EnsureRawBusiness(string rawName, string normalized, string categoryRaw)
    {
        var existing = await _db.RawBusinesses
            .FirstOrDefaultAsync(b => b.RawNameNormalized == normalized);

        if (existing != null) return;

        _db.RawBusinesses.Add(new RawBusiness
        {
            RawName = rawName,
            RawNameNormalized = normalized,
            CategoryRaw = categoryRaw,
            IsMapped = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
    }

    // ── Pattern test tool ─────────────────────────────────────────────────

    /// <summary>
    /// Tests a raw merchant string against all patterns and returns the full result.
    /// Used by the UI pattern testing tool.
    /// </summary>
    public async Task<PatternTestResult> TestPattern(string rawInput)
    {
        var normalized = Normalize(rawInput);
        var alias = await MatchAlias(normalized);
        var effectiveCategory = alias == null
            ? Unassigned
            : (string.IsNullOrEmpty(alias.Category) ? Unassigned : alias.Category);

        return new PatternTestResult(
            RawInput: rawInput,
            Normalized: normalized,
            MatchedAliasId: alias?.Id,
            MatchedAliasName: alias?.AliasName,
            EffectiveCategory: effectiveCategory);
    }

    // ── Admin ops ─────────────────────────────────────────────────────────

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

    public async Task<BusinessAlias> CreateAlias(string aliasName, string category = "")
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

    public async Task UpdateAliasCategory(int aliasId, string category)
    {
        var alias = await _db.BusinessAliases.FindAsync(aliasId)
            ?? throw new KeyNotFoundException($"Alias {aliasId} not found.");
        alias.Category = category;
        await _db.SaveChangesAsync();
    }

    public async Task<AliasPattern> AddPattern(
        int aliasId, string pattern, AliasPatternMatchType matchType)
    {
        var alias = await _db.BusinessAliases.FindAsync(aliasId)
            ?? throw new KeyNotFoundException($"Alias {aliasId} not found.");

        // Normalize the pattern string for contains/starts_with to ensure consistent matching
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
        return ap;
    }

    public async Task RemovePattern(int patternId)
    {
        var p = await _db.AliasPatterns.FindAsync(patternId);
        if (p != null)
        {
            _db.AliasPatterns.Remove(p);
            await _db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Maps a raw business to an alias and marks it as mapped.
    /// Also updates RawBusinessAliasMap for backward compatibility.
    /// </summary>
    public async Task MapRawToAlias(int rawBusinessId, int aliasId)
    {
        var raw = await _db.RawBusinesses.FindAsync(rawBusinessId)
            ?? throw new KeyNotFoundException($"RawBusiness {rawBusinessId} not found.");
        _ = await _db.BusinessAliases.FindAsync(aliasId)
            ?? throw new KeyNotFoundException($"Alias {aliasId} not found.");

        raw.IsMapped = true;
        raw.UpdatedAt = DateTime.UtcNow;

        // Upsert RawBusinessAliasMap
        var existing = await _db.RawBusinessAliasMaps
            .FirstOrDefaultAsync(m => m.RawBusinessId == rawBusinessId);
        if (existing == null)
            _db.RawBusinessAliasMaps.Add(new RawBusinessAliasMap
            {
                RawBusinessId = rawBusinessId,
                AliasId = aliasId
            });
        else
            existing.AliasId = aliasId;

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

    /// <summary>
    /// Retroactively re-runs pattern matching against all unmapped raw businesses.
    /// Any that now match an alias are marked as mapped.
    /// Returns the count of newly mapped businesses.
    /// </summary>
    public async Task<int> RetroactivelyMap()
    {
        var unmapped = await _db.RawBusinesses.Where(b => !b.IsMapped).ToListAsync();
        var patterns = await _db.AliasPatterns.Include(p => p.Alias).ToListAsync();
        int count = 0;

        foreach (var raw in unmapped)
        {
            var alias = MatchAliasFromPatterns(raw.RawNameNormalized, patterns);
            if (alias != null)
            {
                raw.IsMapped = true;
                raw.UpdatedAt = DateTime.UtcNow;

                var existing = await _db.RawBusinessAliasMaps
                    .FirstOrDefaultAsync(m => m.RawBusinessId == raw.Id);
                if (existing == null)
                    _db.RawBusinessAliasMaps.Add(new RawBusinessAliasMap
                    {
                        RawBusinessId = raw.Id,
                        AliasId = alias.Id
                    });
                else
                    existing.AliasId = alias.Id;

                count++;
            }
        }

        if (count > 0) await _db.SaveChangesAsync();
        return count;
    }
}

public record PatternTestResult(
    string RawInput,
    string Normalized,
    int? MatchedAliasId,
    string? MatchedAliasName,
    string EffectiveCategory);