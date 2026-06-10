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
            client_name = "Spening",
            language = "en",
            country_codes = new[] { "US" },
            user = new { client_user_id = "spening-user" },
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

        IQueryable<LinkedAccount> toRemove = string.IsNullOrEmpty(itemId)
            ? _db.LinkedAccounts.Where(a => a.AccessToken == encryptedAccessToken)
            : _db.LinkedAccounts.Where(a => a.ItemId == itemId);

        _db.LinkedAccounts.RemoveRange(toRemove);
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
        Source = TransactionSource.Plaid,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    public string DecryptToken(string encryptedToken) => _encryption.Decrypt(encryptedToken);
}