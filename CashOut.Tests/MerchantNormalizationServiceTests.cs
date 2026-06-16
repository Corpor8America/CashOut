using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CashOut.Tests;

[TestClass]
public class MerchantNormalizationServiceTests
{
    private static AppDbContext BuildDb(string dbName)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(opts);
    }

    private static MerchantNormalizationService BuildSvc(AppDbContext db) =>
        new(db);

    // ── Normalize ─────────────────────────────────────────────────────────

    [TestMethod]
    public void Normalize_TrimsAndCollapseWhitespace()
    {
        var result = MerchantNormalizationService.Normalize("  Wells  Fargo  ");
        Assert.AreEqual("WELLS FARGO", result);
    }

    [TestMethod]
    public void Normalize_ConvertsToUppercase()
    {
        var result = MerchantNormalizationService.Normalize("amazon.com");
        Assert.AreEqual("AMAZON COM", result);
    }

    [TestMethod]
    public void Normalize_RemovesPunctuation()
    {
        var result = MerchantNormalizationService.Normalize("AMZN-MKTP*US/CA:1");
        Assert.AreEqual("AMZN MKTP US CA 1", result);
    }

    [TestMethod]
    public void Normalize_RemovesLongNumericSequences()
    {
        var result = MerchantNormalizationService.Normalize("ACH DEBIT WELLS FARGO 026030004374949");
        Assert.AreEqual("ACH DEBIT WELLS FARGO", result);
    }

    [TestMethod]
    public void Normalize_KeepsShortNumbers()
    {
        // Numbers 6 digits or fewer are retained
        var result = MerchantNormalizationService.Normalize("STORE 12345");
        Assert.AreEqual("STORE 12345", result);
    }

    [TestMethod]
    public void Normalize_RemovesParentheticalSuffix()
    {
        var result = MerchantNormalizationService.Normalize(
            "ACH DEBIT WELLS FARGO CCPYMT 026030004374949 (Wells Fargo Card Ccpymt)");
        Assert.AreEqual("ACH DEBIT WELLS FARGO CCPYMT", result);
    }

    [TestMethod]
    public void Normalize_EmptyString_ReturnsEmpty()
    {
        Assert.AreEqual("", MerchantNormalizationService.Normalize(""));
        Assert.AreEqual("", MerchantNormalizationService.Normalize("   "));
    }

    [TestMethod]
    public void Normalize_IsDeterministic()
    {
        var input = "AMZN MKTP US 987654321 (Amazon Marketplace)";
        Assert.AreEqual(
            MerchantNormalizationService.Normalize(input),
            MerchantNormalizationService.Normalize(input));
    }

    // ── Pattern matching ──────────────────────────────────────────────────

    [TestMethod]
    public void MatchAlias_Contains_Matches()
    {
        var alias = new BusinessAlias { Id = 1, AliasName = "Amazon", Category = "SHOPPING" };
        var patterns = new List<AliasPattern>
        {
            new() { Id = 1, AliasId = 1, Pattern = "AMAZON", MatchType = AliasPatternMatchType.Contains, Alias = alias }
        };

        var result = MerchantNormalizationService.MatchAliasFromPatterns("AMZN AMAZON MKTP", patterns);
        Assert.IsNotNull(result);
        Assert.AreEqual("Amazon", result.AliasName);
    }

    [TestMethod]
    public void MatchAlias_Contains_NoMatch()
    {
        var alias = new BusinessAlias { Id = 1, AliasName = "Amazon", Category = "SHOPPING" };
        var patterns = new List<AliasPattern>
        {
            new() { Id = 1, AliasId = 1, Pattern = "AMAZON", MatchType = AliasPatternMatchType.Contains, Alias = alias }
        };

        var result = MerchantNormalizationService.MatchAliasFromPatterns("WALMART STORE", patterns);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void MatchAlias_StartsWith_Matches()
    {
        var alias = new BusinessAlias { Id = 1, AliasName = "Square", Category = "FOOD_AND_DRINK" };
        var patterns = new List<AliasPattern>
        {
            new() { Id = 1, AliasId = 1, Pattern = "SQ", MatchType = AliasPatternMatchType.StartsWith, Alias = alias }
        };

        var result = MerchantNormalizationService.MatchAliasFromPatterns("SQ JOES COFFEE 12345", patterns);
        Assert.IsNotNull(result);
        Assert.AreEqual("Square", result.AliasName);
    }

    [TestMethod]
    public void MatchAlias_StartsWith_NoMatchMidString()
    {
        var alias = new BusinessAlias { Id = 1, AliasName = "Square", Category = "FOOD_AND_DRINK" };
        var patterns = new List<AliasPattern>
        {
            new() { Id = 1, AliasId = 1, Pattern = "SQ", MatchType = AliasPatternMatchType.StartsWith, Alias = alias }
        };

        // "SQ" appears mid-string, should NOT match StartsWith
        var result = MerchantNormalizationService.MatchAliasFromPatterns("ACH SQ PAYMENT", patterns);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void MatchAlias_Regex_Matches()
    {
        var alias = new BusinessAlias { Id = 1, AliasName = "Amazon", Category = "SHOPPING" };
        var patterns = new List<AliasPattern>
        {
            new() { Id = 1, AliasId = 1, Pattern = "AMZN.*MKTP", MatchType = AliasPatternMatchType.Regex, Alias = alias }
        };

        var result = MerchantNormalizationService.MatchAliasFromPatterns("AMZN MKTP US", patterns);
        Assert.IsNotNull(result);
    }

    [TestMethod]
    public void MatchAlias_MultipleMatches_ReturnsLowestAliasId()
    {
        var alias1 = new BusinessAlias { Id = 1, AliasName = "Alias One", Category = "A" };
        var alias2 = new BusinessAlias { Id = 2, AliasName = "Alias Two", Category = "B" };
        var patterns = new List<AliasPattern>
        {
            new() { Id = 1, AliasId = 2, Pattern = "MERCHANT", MatchType = AliasPatternMatchType.Contains, Alias = alias2 },
            new() { Id = 2, AliasId = 1, Pattern = "MERCHANT", MatchType = AliasPatternMatchType.Contains, Alias = alias1 }
        };

        var result = MerchantNormalizationService.MatchAliasFromPatterns("MERCHANT X", patterns);
        Assert.IsNotNull(result);
        Assert.AreEqual(1, result.Id); // lowest alias ID wins
    }

    [TestMethod]
    public void MatchAlias_CaseInsensitive()
    {
        var alias = new BusinessAlias { Id = 1, AliasName = "Starbucks", Category = "FOOD_AND_DRINK" };
        var patterns = new List<AliasPattern>
        {
            new() { Id = 1, AliasId = 1, Pattern = "starbucks", MatchType = AliasPatternMatchType.Contains, Alias = alias }
        };

        var result = MerchantNormalizationService.MatchAliasFromPatterns("STARBUCKS STORE 1234", patterns);
        Assert.IsNotNull(result);
    }

    // ── Import pipeline (Resolve) ──────────────────────────────────────────

    [TestMethod]
    public async Task Resolve_MatchedAlias_WithCategory_ReturnsAliasCategory()
    {
        var db = BuildDb(nameof(Resolve_MatchedAlias_WithCategory_ReturnsAliasCategory));
        var alias = new BusinessAlias { Id = 1, AliasName = "Amazon", Category = "SHOPPING", CreatedAt = DateTime.UtcNow };
        db.BusinessAliases.Add(alias);
        db.AliasPatterns.Add(new AliasPattern
        {
            Id = 1,
            AliasId = 1,
            Pattern = "AMZN",
            MatchType = AliasPatternMatchType.Contains,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Alias = alias
        });
        await db.SaveChangesAsync();

        var svc = BuildSvc(db);
        var (matchedAlias, rawBusiness, normalizedName, category) =
            await svc.Resolve("AMZN AMAZON MKTP US", "SOME_PLAID_CATEGORY");

        Assert.AreEqual(1, matchedAlias?.Id);
        Assert.IsNull(rawBusiness);
        Assert.AreEqual("AMZN AMAZON MKTP US", normalizedName);
        Assert.AreEqual("SHOPPING", category);
        // Verify CSV/Plaid category was ignored
        Assert.AreNotEqual("SOME_PLAID_CATEGORY", category);
    }

    [TestMethod]
    public async Task Resolve_MatchedAlias_NoCategory_ReturnsUnassigned()
    {
        var db = BuildDb(nameof(Resolve_MatchedAlias_NoCategory_ReturnsUnassigned));
        var alias = new BusinessAlias { Id = 1, AliasName = "Venmo", Category = "", CreatedAt = DateTime.UtcNow };
        db.BusinessAliases.Add(alias);
        db.AliasPatterns.Add(new AliasPattern
        {
            Id = 1,
            AliasId = 1,
            Pattern = "VENMO",
            MatchType = AliasPatternMatchType.Contains,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Alias = alias
        });
        await db.SaveChangesAsync();

        var svc = BuildSvc(db);
        var (matchedAlias, rawBusiness, _, category) =
            await svc.Resolve("VENMO PAYMENT 12345678", "TRANSFER");

        Assert.AreEqual(1, matchedAlias?.Id);
        Assert.IsNull(rawBusiness);
        Assert.AreEqual(MerchantNormalizationService.Unassigned, category);
    }

    [TestMethod]
    public async Task Resolve_NoMatch_ReturnsUnassigned_AndCreatesRawBusiness()
    {
        var db = BuildDb(nameof(Resolve_NoMatch_ReturnsUnassigned_AndCreatesRawBusiness));
        var svc = BuildSvc(db);

        var (matchedAlias, rawBusiness, normalizedName, category) =
            await svc.Resolve("SQ *JOES COFFEE 9876543", "FOOD_AND_DRINK");
        await db.SaveChangesAsync();

        Assert.IsNull(matchedAlias);
        Assert.IsNotNull(rawBusiness);
        Assert.AreEqual("SQ JOES COFFEE", normalizedName);
        Assert.AreEqual(MerchantNormalizationService.Unassigned, category);

        var rawBiz = db.RawBusinesses.SingleOrDefault();
        Assert.IsNotNull(rawBiz);
        Assert.IsFalse(rawBiz.IsMapped);
        Assert.AreEqual("FOOD_AND_DRINK", rawBiz.CategoryRaw);
    }

    [TestMethod]
    public async Task Resolve_NoMatch_DoesNotDuplicateRawBusiness()
    {
        var db = BuildDb(nameof(Resolve_NoMatch_DoesNotDuplicateRawBusiness));
        // Pre-seed a raw business with the normalized name that "SQ JOES COFFEE" would produce
        var normalized = MerchantNormalizationService.Normalize("SQ *JOES COFFEE 9876543");
        db.RawBusinesses.Add(new RawBusiness
        {
            RawName = "SQ *JOES COFFEE 9876543",
            RawNameNormalized = normalized,
            CategoryRaw = "",
            IsMapped = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var svc = BuildSvc(db);
        await svc.Resolve("SQ *JOES COFFEE 9876543", "");
        await db.SaveChangesAsync();

        Assert.AreEqual(1, db.RawBusinesses.Count());
    }

    // ── RetroactivelyMap ──────────────────────────────────────────────────

    [TestMethod]
    public async Task RetroactivelyMap_MapsMatchingTransactionsAndCleansRawBusiness()
    {
        var db = BuildDb(nameof(RetroactivelyMap_MapsMatchingTransactionsAndCleansRawBusiness));

        var alias = new BusinessAlias { Id = 1, AliasName = "Amazon", Category = "SHOPPING", CreatedAt = DateTime.UtcNow };
        db.BusinessAliases.Add(alias);
        db.AliasPatterns.Add(new AliasPattern
        {
            Id = 1,
            AliasId = 1,
            Pattern = "AMZN",
            MatchType = AliasPatternMatchType.Contains,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Alias = alias
        });

        var raw = new RawBusiness
        {
            RawName = "AMZN MKTP US 123456789",
            RawNameNormalized = MerchantNormalizationService.Normalize("AMZN MKTP US 123456789"),
            CategoryRaw = "SHOPPING_SOURCE",
            IsMapped = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.RawBusinesses.Add(raw);
        db.Transactions.Add(new Transaction
        {
            TransactionId = "txn-1",
            AccountId = "acct",
            Date = new DateOnly(2026, 1, 1),
            Name = "AMZN MKTP US 123456789",
            RawName = "AMZN MKTP US 123456789",
            NormalizedName = raw.RawNameNormalized,
            Amount = 10,
            Debit = 10,
            Category = "SHOPPING_SOURCE",
            RawBusinessId = raw.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
        var svc = BuildSvc(db);

        var count = await svc.RetroactivelyMap();

        Assert.AreEqual(1, count);
        var txn = db.Transactions.Single();
        Assert.AreEqual(1, txn.AliasId);
        Assert.IsNull(txn.RawBusinessId);
        Assert.AreEqual("SHOPPING", txn.Category);
        Assert.AreEqual(0, db.RawBusinesses.Count());
    }

    [TestMethod]
    public async Task RetroactivelyMap_IgnoresAlreadyAliasedTransactions()
    {
        var db = BuildDb(nameof(RetroactivelyMap_IgnoresAlreadyAliasedTransactions));

        var alias = new BusinessAlias { Id = 1, AliasName = "Amazon", Category = "SHOPPING", CreatedAt = DateTime.UtcNow };
        db.BusinessAliases.Add(alias);
        db.AliasPatterns.Add(new AliasPattern
        {
            Id = 1,
            AliasId = 1,
            Pattern = "AMAZON",
            MatchType = AliasPatternMatchType.Contains,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Alias = alias
        });

        db.Transactions.Add(new Transaction
        {
            TransactionId = "txn-1",
            AccountId = "acct",
            Date = new DateOnly(2026, 1, 1),
            Name = "AMAZON.COM",
            RawName = "AMAZON.COM",
            NormalizedName = "AMAZON COM",
            Amount = 10,
            Debit = 10,
            Category = "SHOPPING",
            AliasId = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
        var svc = BuildSvc(db);

        var count = await svc.RetroactivelyMap();
        Assert.AreEqual(0, count);
    }

    // ── Pattern management ────────────────────────────────────────────────

    [TestMethod]
    public async Task AddPattern_NormalizesContainsPattern()
    {
        var db = BuildDb(nameof(AddPattern_NormalizesContainsPattern));
        db.BusinessAliases.Add(new BusinessAlias
        {
            Id = 1,
            AliasName = "Amazon",
            Category = "SHOPPING",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var svc = BuildSvc(db);
        // User types "amazon.com" — should be normalized to "AMAZON COM"
        var pattern = await svc.AddPattern(1, "amazon.com", AliasPatternMatchType.Contains);

        Assert.AreEqual("AMAZON COM", pattern.Pattern);
    }

    [TestMethod]
    public async Task AddPattern_RegexNotNormalized()
    {
        var db = BuildDb(nameof(AddPattern_RegexNotNormalized));
        db.BusinessAliases.Add(new BusinessAlias
        {
            Id = 1,
            AliasName = "Amazon",
            Category = "SHOPPING",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var svc = BuildSvc(db);
        // Regex patterns are stored as-is (only trimmed)
        var pattern = await svc.AddPattern(1, "AMZN.*MKTP", AliasPatternMatchType.Regex);

        Assert.AreEqual("AMZN.*MKTP", pattern.Pattern);
    }

    [TestMethod]
    public async Task TestPattern_ReturnsCorrectResult()
    {
        var db = BuildDb(nameof(TestPattern_ReturnsCorrectResult));
        var alias = new BusinessAlias { Id = 1, AliasName = "Starbucks", Category = "FOOD_AND_DRINK", CreatedAt = DateTime.UtcNow };
        db.BusinessAliases.Add(alias);
        db.AliasPatterns.Add(new AliasPattern
        {
            Id = 1,
            AliasId = 1,
            Pattern = "STARBUCKS",
            MatchType = AliasPatternMatchType.Contains,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Alias = alias
        });
        await db.SaveChangesAsync();

        var svc = BuildSvc(db);
        var result = await svc.TestPattern("Starbucks Store #1234");

        Assert.AreEqual(1, result.MatchedAliasId);
        Assert.AreEqual("Starbucks", result.MatchedAliasName);
        Assert.AreEqual("FOOD_AND_DRINK", result.EffectiveCategory);
        Assert.AreEqual("STARBUCKS STORE 1234", result.Normalized);
    }

    [TestMethod]
    public async Task TestPattern_NoMatch_ReturnsUnassigned()
    {
        var db = BuildDb(nameof(TestPattern_NoMatch_ReturnsUnassigned));
        var svc = BuildSvc(db);

        var result = await svc.TestPattern("COMPLETELY UNKNOWN MERCHANT 999999999");

        Assert.IsNull(result.MatchedAliasId);
        Assert.AreEqual(MerchantNormalizationService.Unassigned, result.EffectiveCategory);
    }
}
