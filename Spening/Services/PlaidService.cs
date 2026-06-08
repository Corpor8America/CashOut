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

    private async Task<string> BaseUrl()
    {
        var env = await _settings.GetPlaidEnvironment();
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

    private async Task<string> Secret()
    {
        var env = await _settings.GetPlaidEnvironment();
        var key = env == "production" ? "PLAID_PRODUCTION_SECRET" : "PLAID_SANDBOX_SECRET";
        return _config[key]
            ?? Environment.GetEnvironmentVariable(key)
            ?? throw new InvalidOperationException($"{key} is not set.");
    }

    private async Task<JsonElement> Post(string path, object body)
    {
        var url = await BaseUrl() + path;
        var response = await _http.PostAsJsonAsync(url, body);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Plaid error {(int)response.StatusCode} on {path}: {err}");
        }
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    // ── Accounts ──────────────────────────────────────────────────────────

    /// <summary>
    /// Fetches accounts from Plaid for a given access token and persists them.
    /// Called after token exchange in Phase 3.
    /// </summary>
    public async Task<List<LinkedAccount>> FetchAndPersistAccounts(string plainAccessToken)
    {
        var json = await Post("/accounts/get", new
        {
            client_id = ClientId,
            secret = await Secret(),
            access_token = plainAccessToken
        });

        var institutionId = json.GetProperty("item")
            .TryGetProperty("institution_id", out var inst)
            ? inst.GetString() ?? "" : "";

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
                Mask = a.TryGetProperty("mask", out var m) ? m.GetString() ?? "" : "",
                Name = a.GetProperty("name").GetString()!,
                Subtype = a.TryGetProperty("subtype", out var s) ? s.GetString() ?? "" : "",
                Institution = institutionName,
                CreatedAt = DateTime.UtcNow
            };

            // Upsert — avoid duplicates if re-linking
            var existing = await _db.LinkedAccounts
                .FirstOrDefaultAsync(x => x.AccountId == account.AccountId);

            if (existing == null)
                _db.LinkedAccounts.Add(account);
            else
            {
                existing.AccessToken = encryptedToken;
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
                secret = await Secret(),
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

    /// <summary>
    /// Revokes the Plaid Item server-side and removes all associated accounts from the DB.
    /// </summary>
    public async Task RemoveItem(string encryptedAccessToken)
    {
        var plainToken = _encryption.Decrypt(encryptedAccessToken);

        await Post("/item/remove", new
        {
            client_id = ClientId,
            secret = await Secret(),
            access_token = plainToken
        });

        // Remove all accounts sharing this token
        var toRemove = _db.LinkedAccounts
            .Where(a => a.AccessToken == encryptedAccessToken);
        _db.LinkedAccounts.RemoveRange(toRemove);
        await _db.SaveChangesAsync();
    }

    public async Task<string> CreateLinkToken()
    {
        var json = await Post("/link/token/create", new
        {
            client_id = ClientId,
            secret = await Secret(),
            client_name = "Spening",
            language = "en",
            country_codes = new[] { "US" },
            user = new { client_user_id = "spening-user" },
            products = new[] { "transactions" }
        });

        return json.GetProperty("link_token").GetString()!;
    }

    /// <summary>
    /// Exchanges a public_token (from Plaid Link) for a persistent access_token,
    /// then fetches and persists all accounts associated with the new Item.
    /// Returns the list of newly added LinkedAccount records.
    /// </summary>
    public async Task<List<LinkedAccount>> ExchangeAndPersist(string publicToken)
    {
        var json = await Post("/item/public_token/exchange", new
        {
            client_id = ClientId,
            secret = await Secret(),
            public_token = publicToken
        });

        var plainAccessToken = json.GetProperty("access_token").GetString()!;
        return await FetchAndPersistAccounts(plainAccessToken);
    }

    // ── Transactions ──────────────────────────────────────────────────────

    /// <summary>
    /// Runs a full cursor-based sync for one account's access token.
    /// Loops until has_more is false. Returns all added/modified transactions,
    /// all removed transaction IDs, and the new cursor to persist.
    /// </summary>
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
                secret = await Secret(),
                access_token = plainToken,
                cursor = cursor,
                options = new { include_personal_finance_category = true }
            });

            foreach (var t in json.GetProperty("added").EnumerateArray())
                added.Add(MapTransaction(t));

            foreach (var t in json.GetProperty("modified").EnumerateArray())
                added.Add(MapTransaction(t));  // modified are treated as upserts

            foreach (var t in json.GetProperty("removed").EnumerateArray())
                removedIds.Add(t.GetProperty("transaction_id").GetString()!);

            hasMore = json.GetProperty("has_more").GetBoolean();
            cursor = json.GetProperty("next_cursor").GetString()!;
        }

        return (added, removedIds, cursor);
    }

    /// <summary>
    /// Fetches all transactions for a calendar year using /transactions/get.
    /// Does not update sync cursor.
    /// </summary>
    public async Task<List<Transaction>> FetchTransactions(
        string encryptedAccessToken, int year)
    {
        var plainToken = _encryption.Decrypt(encryptedAccessToken);

        var json = await Post("/transactions/get", new
        {
            client_id = ClientId,
            secret = await Secret(),
            access_token = plainToken,
            start_date = $"{year}-01-01",
            end_date = $"{year}-12-31",
            options = new { include_personal_finance_category = true }
        });

        return json.GetProperty("transactions")
            .EnumerateArray()
            .Select(MapTransaction)
            .ToList();
    }

    private static Transaction MapTransaction(JsonElement t) => new()
    {
        TransactionId = t.GetProperty("transaction_id").GetString()!,
        AccountId = t.GetProperty("account_id").GetString()!,
        Date = DateOnly.Parse(t.GetProperty("date").GetString()!),
        Name = t.GetProperty("name").GetString()!,
        Amount = t.GetProperty("amount").GetDecimal(),
        Category = t.TryGetProperty("personal_finance_category", out var pfc)
                   && pfc.ValueKind == JsonValueKind.Object
                   ? pfc.GetProperty("primary").GetString() ?? ""
                   : t.TryGetProperty("category", out var cat)
                   && cat.ValueKind == JsonValueKind.Array
                   ? string.Join(" > ", cat.EnumerateArray().Select(x => x.GetString()))
                   : "",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    /// <summary>Returns the decrypted access token for a given account. Used by sync/fetch.</summary>
    public string DecryptToken(string encryptedToken) => _encryption.Decrypt(encryptedToken);
}
