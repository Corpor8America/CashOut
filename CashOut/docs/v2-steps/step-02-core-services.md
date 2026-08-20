# Step 02 — Core Services

**Goal:** Create EncryptionService, PlaidService, and SettingsService. These are the foundation services needed by the import pipeline and transaction sync.

**Prerequisites:** Step 01 complete (project builds, DB running, entities defined).

---

## 2.1 EncryptionService

AES-256-GCM encryption for Plaid access tokens. Reads `ENCRYPTION_KEY` from environment (base64-encoded 32-byte key).

**File:** `CashOut/Services/EncryptionService.cs`

```csharp
using System.Security.Cryptography;

public class EncryptionService
{
    private readonly byte[] _key;

    public EncryptionService(IConfiguration config)
    {
        var raw = config["ENCRYPTION_KEY"]
            ?? Environment.GetEnvironmentVariable("ENCRYPTION_KEY")
            ?? throw new InvalidOperationException(
                "ENCRYPTION_KEY environment variable is required.");

        _key = Convert.FromBase64String(raw);

        if (_key.Length != 32)
            throw new InvalidOperationException(
                "ENCRYPTION_KEY must be a base64-encoded 32-byte value.");
    }

    public string Encrypt(string plaintext)
    {
        var nonce = new byte[AesGcm.NonceByteSizes.MaxSize];
        RandomNumberGenerator.Fill(nonce);

        var plaintextBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];

        using var aes = new AesGcm(_key, AesGcm.TagByteSizes.MaxSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        return $"{Convert.ToBase64String(nonce)}.{Convert.ToBase64String(tag)}.{Convert.ToBase64String(ciphertext)}";
    }

    public string Decrypt(string payload)
    {
        var parts = payload.Split('.');
        if (parts.Length != 3)
            throw new FormatException("Invalid encrypted payload format.");

        var nonce = Convert.FromBase64String(parts[0]);
        var tag = Convert.FromBase64String(parts[1]);
        var ciphertext = Convert.FromBase64String(parts[2]);
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(_key, AesGcm.TagByteSizes.MaxSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return System.Text.Encoding.UTF8.GetString(plaintext);
    }
}
```

## 2.2 SettingsService

Provides Plaid environment, output year, available years, and excluded categories. Reads `PLAID_ENV` from environment variable.

**File:** `CashOut/Services/SettingsService.cs`

```csharp
using Microsoft.EntityFrameworkCore;

public class SettingsService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public SettingsService(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public string GetPlaidEnvironment()
    {
        var env = _config["PLAID_ENV"]
            ?? Environment.GetEnvironmentVariable("PLAID_ENV");

        if (!string.IsNullOrWhiteSpace(env))
        {
            env = env.Trim().ToLowerInvariant();
            if (env is "sandbox" or "development" or "production")
                return env;
        }

        return "sandbox";
    }

    public async Task<int> GetOutputYear()
    {
        var maxDate = await _db.Transactions
            .OrderByDescending(t => t.Date)
            .Select(t => (DateOnly?)t.Date)
            .FirstOrDefaultAsync();

        return maxDate?.Year ?? DateTime.UtcNow.Year;
    }

    public async Task<List<int>> GetAvailableYears()
    {
        var currentYear = DateTime.UtcNow.Year;
        var minYear = currentYear - 6;

        var yearsWithData = await _db.Transactions
            .Where(t => t.Date.Year >= minYear)
            .Select(t => t.Date.Year)
            .Distinct()
            .OrderByDescending(y => y)
            .ToListAsync();

        if (!yearsWithData.Contains(currentYear))
            yearsWithData.Insert(0, currentYear);

        return yearsWithData;
    }

    public async Task<List<string>> GetExcludedCategories()
    {
        // In v2, excluded categories are stored as a simple in-memory session
        // or we can re-introduce a lightweight table. For now, return empty.
        // TODO: If needed, add an ExcludedCategories DbSet or JSON config.
        return new List<string>();
    }

    public async Task<Dictionary<string, string>> GetAll()
    {
        var outputYear = await GetOutputYear();
        return new Dictionary<string, string>
        {
            ["plaid_environment"] = GetPlaidEnvironment(),
            ["output_year"] = outputYear.ToString()
        };
    }
}
```

## 2.3 PlaidService

HTTP client for Plaid API. Handles link token creation, token exchange, account fetching, transaction sync/fetch, and item removal. Registered as a typed HttpClient via `AddHttpClient<PlaidService>`.

**File:** `CashOut/Services/PlaidService.cs`

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

public class PlaidService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly SettingsService _settings;
    private readonly EncryptionService _encryption;
    private readonly AppDbContext _db;

    public PlaidService(
        HttpClient http,
        IConfiguration config,
        SettingsService settings,
        EncryptionService encryption,
        AppDbContext db)
    {
        _http = http;
        _config = config;
        _settings = settings;
        _encryption = encryption;
        _db = db;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private string BaseUrl()
    {
        var env = _settings.GetPlaidEnvironment();
        return env switch
        {
            "production" => "https://production.plaid.com",
            "development" => "https://development.plaid.com",
            _ => "https://sandbox.plaid.com"
        };
    }

    private string ClientId =>
        _config["PLAID_CLIENT_ID"]
        ?? Environment.GetEnvironmentVariable("PLAID_CLIENT_ID")
        ?? throw new InvalidOperationException("PLAID_CLIENT_ID is not set.");

    private string Secret()
    {
        var env = _settings.GetPlaidEnvironment();
        var key = env == "production" ? "PLAID_PRODUCTION_SECRET" : "PLAID_SANDBOX_SECRET";
        return _config[key]
            ?? Environment.GetEnvironmentVariable(key)
            ?? throw new InvalidOperationException($"{key} is not set.");
    }

    private async Task<JsonElement> Post(string path, object body)
    {
        var url = BaseUrl() + path;
        var response = await _http.PostAsJsonAsync(url, body);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Plaid error {(int)response.StatusCode} on {path}: {err}");
        }
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    // ── Link Token ────────────────────────────────────────────────────────

    public async Task<string> CreateLinkToken()
    {
        var json = await Post("/link/token/create", new
        {
            client_id = ClientId,
            secret = Secret(),
            client_name = "CashOut",
            language = "en",
            country_codes = new[] { "US" },
            user = new { client_user_id = "cashout-user" },
            products = new[] { "transactions" }
        });

        return json.GetProperty("link_token").GetString()!;
    }

    // ── Token Exchange ────────────────────────────────────────────────────

    public async Task<List<LinkedAccount>> ExchangeAndPersist(string publicToken)
    {
        var json = await Post("/item/public_token/exchange", new
        {
            client_id = ClientId,
            secret = Secret(),
            public_token = publicToken
        });

        var plainAccessToken = json.GetProperty("access_token").GetString()!;
        return await FetchAndPersistAccounts(plainAccessToken);
    }

    // ── Accounts ──────────────────────────────────────────────────────────

    public async Task<List<LinkedAccount>> FetchAndPersistAccounts(string plainAccessToken)
    {
        var json = await Post("/accounts/get", new
        {
            client_id = ClientId,
            secret = Secret(),
            access_token = plainAccessToken
        });

        var item = json.GetProperty("item");
        var institutionId = item.TryGetProperty("institution_id", out var inst)
            ? inst.GetString() ?? "" : "";
        var itemId = item.TryGetProperty("item_id", out var iid)
            ? iid.GetString() ?? "" : "";

        var institutionName = institutionId != ""
            ? await FetchInstitutionName(institutionId)
            : "Unknown";

        var encryptedToken = _encryption.Encrypt(plainAccessToken);
        var accounts = new List<LinkedAccount>();

        foreach (var a in json.GetProperty("accounts").EnumerateArray())
        {
            var account = new LinkedAccount
            {
                Id = Guid.NewGuid(),
                AccessToken = encryptedToken,
                AccountId = a.GetProperty("account_id").GetString()!,
                ItemId = itemId,
                Mask = a.TryGetProperty("mask", out var m) ? m.GetString() ?? "" : "",
                Name = a.GetProperty("name").GetString()!,
                Subtype = a.TryGetProperty("subtype", out var s) ? s.GetString() ?? "" : "",
                Institution = institutionName,
                CreatedAt = DateTime.UtcNow
            };

            var existing = await _db.LinkedAccounts
                .FirstOrDefaultAsync(x => x.AccountId == account.AccountId);

            if (existing == null)
                _db.LinkedAccounts.Add(account);
            else
            {
                existing.AccessToken = encryptedToken;
                existing.ItemId = itemId;
                existing.Name = account.Name;
                existing.Institution = institutionName;
                _db.LinkedAccounts.Update(existing);
            }

            accounts.Add(account);
        }

        await _db.SaveChangesAsync();
        return accounts;
    }

    public async Task<string> FetchInstitutionName(string institutionId)
    {
        try
        {
            var json = await Post("/institutions/get_by_id", new
            {
                client_id = ClientId,
                secret = Secret(),
                institution_id = institutionId,
                country_codes = new[] { "US" }
            });
            return json.GetProperty("institution").GetProperty("name").GetString() ?? "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }

    public async Task RemoveItem(string encryptedAccessToken, string itemId)
    {
        try
        {
            var plainToken = _encryption.Decrypt(encryptedAccessToken);
            await Post("/item/remove", new
            {
                client_id = ClientId,
                secret = Secret(),
                access_token = plainToken
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[PlaidService] RemoveItem: Plaid revocation failed (will still delete locally): {ex.Message}");
        }

        if (!string.IsNullOrEmpty(itemId))
        {
            var accountsToRemove = await _db.LinkedAccounts.Where(a => a.ItemId == itemId).ToListAsync();
            var plaidAccountIds = accountsToRemove.Select(a => a.AccountId).ToHashSet();
            var linkedAccountGuids = accountsToRemove.Select(a => a.Id.ToString()).ToHashSet();

            var txnsToRemove = await _db.Transactions
                .Where(t => plaidAccountIds.Contains(t.AccountId) || linkedAccountGuids.Contains(t.AccountId))
                .ToListAsync();
            _db.Transactions.RemoveRange(txnsToRemove);

            var profilesToRemove = await _db.CsvMappingProfiles
                .Where(p => plaidAccountIds.Contains(p.AccountId) || linkedAccountGuids.Contains(p.AccountId))
                .ToListAsync();
            _db.CsvMappingProfiles.RemoveRange(profilesToRemove);

            _db.LinkedAccounts.RemoveRange(accountsToRemove);
        }
        else
        {
            Console.Error.WriteLine(
                "[PlaidService] RemoveItem: ItemId is empty — cannot reliably identify accounts by encrypted token.");
        }
        await _db.SaveChangesAsync();
    }

    public async Task MergeManualAccount(Guid manualAccountId, string targetLinkedAccountId)
    {
        var manualIdStr = manualAccountId.ToString();

        var txns = await _db.Transactions.Where(t => t.AccountId == manualIdStr).ToListAsync();
        foreach (var t in txns)
        {
            t.AccountId = targetLinkedAccountId;
            t.UpdatedAt = DateTime.UtcNow;
        }

        var profiles = await _db.CsvMappingProfiles.Where(p => p.AccountId == manualIdStr).ToListAsync();
        foreach (var p in profiles)
        {
            p.AccountId = targetLinkedAccountId;
        }

        var manualAcc = await _db.ManualAccounts.FindAsync(manualAccountId);
        if (manualAcc != null)
        {
            _db.ManualAccounts.Remove(manualAcc);
        }

        await _db.SaveChangesAsync();
    }

    // ── Transactions ──────────────────────────────────────────────────────

    public async Task<(List<Transaction> added, List<string> removedIds, string nextCursor)>
        SyncTransactions(string encryptedAccessToken, string? currentCursor)
    {
        var plainToken = _encryption.Decrypt(encryptedAccessToken);
        var added = new List<Transaction>();
        var removedIds = new List<string>();
        var cursor = currentCursor ?? "";
        bool hasMore = true;

        while (hasMore)
        {
            var json = await Post("/transactions/sync", new
            {
                client_id = ClientId,
                secret = Secret(),
                access_token = plainToken,
                cursor = cursor,
                options = new { include_personal_finance_category = true }
            });

            foreach (var t in json.GetProperty("added").EnumerateArray())
                added.Add(MapTransaction(t));

            foreach (var t in json.GetProperty("modified").EnumerateArray())
                added.Add(MapTransaction(t));

            foreach (var t in json.GetProperty("removed").EnumerateArray())
                removedIds.Add(t.GetProperty("transaction_id").GetString()!);

            hasMore = json.GetProperty("has_more").GetBoolean();
            cursor = json.GetProperty("next_cursor").GetString()!;
        }

        return (added, removedIds, cursor);
    }

    public async Task<List<Transaction>> FetchTransactions(
        string encryptedAccessToken, int year)
    {
        var plainToken = _encryption.Decrypt(encryptedAccessToken);
        var allTransactions = new List<Transaction>();
        const int pageSize = 500;
        int offset = 0;
        int totalTransactions;

        do
        {
            var json = await Post("/transactions/get", new
            {
                client_id = ClientId,
                secret = Secret(),
                access_token = plainToken,
                start_date = $"{year}-01-01",
                end_date = $"{year}-12-31",
                options = new
                {
                    include_personal_finance_category = true,
                    count = pageSize,
                    offset = offset
                }
            });

            totalTransactions = json.GetProperty("total_transactions").GetInt32();

            var page = json.GetProperty("transactions")
                .EnumerateArray()
                .Select(MapTransaction)
                .ToList();

            allTransactions.AddRange(page);
            offset += page.Count;

            if (page.Count == 0) break;

        } while (allTransactions.Count < totalTransactions);

        return allTransactions;
    }

    private static Transaction MapTransaction(JsonElement t)
    {
        var externalAmount = t.GetProperty("amount").GetDecimal();
        var (credit, debit, amount) = Transaction.NormalizeSingleAmount(externalAmount);

        return new Transaction
        {
            TransactionId = t.GetProperty("transaction_id").GetString()!,
            AccountId = t.GetProperty("account_id").GetString()!,
            Date = DateOnly.Parse(t.GetProperty("date").GetString()!),
            Name = t.GetProperty("name").GetString()!,
            RawName = t.GetProperty("name").GetString()!,
            Credit = credit,
            Debit = debit,
            Amount = amount,
            Category = t.TryGetProperty("personal_finance_category", out var pfc)
                       && pfc.ValueKind == JsonValueKind.Object
                       ? pfc.GetProperty("primary").GetString() ?? ""
                       : t.TryGetProperty("category", out var cat)
                       && cat.ValueKind == JsonValueKind.Array
                       ? string.Join(" > ", cat.EnumerateArray().Select(x => x.GetString()))
                       : "",
            Source = TransactionSource.Plaid,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public string DecryptToken(string encryptedToken) => _encryption.Decrypt(encryptedToken);
}
```

## 2.4 Register services in Program.cs

Update `CashOut/Program.cs` — replace the service registration section with:

```csharp
// ── Services ──────────────────────────────────────────────────────────────
builder.Services.AddSingleton<EncryptionService>();
builder.Services.AddScoped<SettingsService>();

builder.Services.AddHttpClient<PlaidService>(client =>
{
    client.DefaultRequestHeaders.Add("Plaid-Version", "2020-09-14");
});
```

## 2.5 Verify build

```bash
dotnet build CashOut/CashOut.csproj
```

---

## Verification

1. `EncryptionService` encrypts/decrypts a 32-byte base64 key without errors
2. `SettingsService` reads `PLAID_ENV` from environment and returns "sandbox" by default
3. `PlaidService` compiles and can be resolved by DI
4. `dotnet build` succeeds
