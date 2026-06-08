# Phase 2 — PlaidService, EncryptionService, Settings API, Accounts API

## Progress Tracker

- [ ] 2.1 Create `EncryptionService`
- [ ] 2.2 Create `SettingsService`
- [ ] 2.3 Create `PlaidService` (foundation — accounts + institution lookup + item remove)
- [ ] 2.4 Create `SettingsController`
- [ ] 2.5 Create `AccountsController`
- [ ] 2.6 Register new services in `Program.cs`
- [ ] 2.7 Verify endpoints with curl

---

## Context

This phase wires up the service layer and two API controllers. No Plaid Link flow yet (that's
Phase 3) — just the foundational services and the ability to query/delete accounts and
read/write settings.

The `PlaidService` built here will be extended in Phases 3 and 4. Build only what is listed below;
do not jump ahead.

---

## Task 2.1 — EncryptionService

Plaid `access_token` values are sensitive. They must be encrypted before writing to the DB and
decrypted only when needed for an API call. Use AES-256-GCM.

Create `Spening/Services/EncryptionService.cs`:

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
        var nonce = new byte[AesGcm.NonceByteSizes.MaxSize];   // 12 bytes
        RandomNumberGenerator.Fill(nonce);

        var plaintextBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];       // 16 bytes

        using var aes = new AesGcm(_key, AesGcm.TagByteSizes.MaxSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        // Store as: base64(nonce) + "." + base64(tag) + "." + base64(ciphertext)
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

---

## Task 2.2 — SettingsService

A thin wrapper that reads and writes rows in `app_settings`. Other services use this instead of
querying `AppDbContext` directly for settings.

Create `Spening/Services/SettingsService.cs`:

```csharp
public class SettingsService
{
    private readonly AppDbContext _db;

    public SettingsService(AppDbContext db) => _db = db;

    public async Task<string> Get(string key, string defaultValue = "")
    {
        var row = await _db.AppSettings.FindAsync(key);
        return row?.Value ?? defaultValue;
    }

    public async Task Set(string key, string value)
    {
        var row = await _db.AppSettings.FindAsync(key);
        if (row == null)
        {
            _db.AppSettings.Add(new AppSetting { Key = key, Value = value });
        }
        else
        {
            row.Value = value;
            _db.AppSettings.Update(row);
        }
        await _db.SaveChangesAsync();
    }

    public async Task<Dictionary<string, string>> GetAll()
    {
        return await _db.AppSettings
            .ToDictionaryAsync(s => s.Key, s => s.Value);
    }

    // Convenience helpers
    public async Task<string> GetPlaidEnvironment() =>
        await Get("plaid_environment", "sandbox");

    public async Task<int> GetOutputYear() =>
        int.TryParse(await Get("output_year"), out var y) ? y : DateTime.UtcNow.Year;
}
```

---

## Task 2.3 — PlaidService (foundation)

This service wraps all outbound Plaid HTTP calls. In this phase, implement only the methods needed
for account management. Transaction methods are added in Phase 4.

The Plaid base URL is derived from the `plaid_environment` setting at call time (not cached at
startup) so that changing the environment via the Settings page takes effect immediately.

Create `Spening/Services/PlaidService.cs`:

```csharp
using System.Net.Http.Json;
using System.Text.Json;

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

    /// <summary>Returns the decrypted access token for a given account. Used by sync/fetch.</summary>
    public string DecryptToken(string encryptedToken) => _encryption.Decrypt(encryptedToken);
}
```

---

## Task 2.4 — SettingsController

Create `Spening/Controllers/SettingsController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/settings")]
public class SettingsController : ControllerBase
{
    private readonly SettingsService _settings;

    public SettingsController(SettingsService settings) => _settings = settings;

    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await _settings.GetAll());

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] Dictionary<string, string> updates)
    {
        var allowed = new[] { "plaid_environment", "output_year" };
        foreach (var (key, value) in updates)
        {
            if (!allowed.Contains(key))
                return BadRequest(new { error = $"Unknown setting key: {key}" });

            if (key == "plaid_environment" &&
                !new[] { "sandbox", "development", "production" }.Contains(value))
                return BadRequest(new { error = $"Invalid environment: {value}" });

            if (key == "output_year" && !int.TryParse(value, out _))
                return BadRequest(new { error = "output_year must be a number" });

            await _settings.Set(key, value);
        }
        return Ok(await _settings.GetAll());
    }
}
```

---

## Task 2.5 — AccountsController

Create `Spening/Controllers/AccountsController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/accounts")]
public class AccountsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly PlaidService _plaid;

    public AccountsController(AppDbContext db, PlaidService plaid)
    {
        _db = db;
        _plaid = plaid;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var accounts = await _db.LinkedAccounts
            .OrderBy(a => a.Institution)
            .ThenBy(a => a.Name)
            .Select(a => new
            {
                a.Id,
                a.AccountId,
                a.Mask,
                a.Name,
                a.Subtype,
                a.Institution,
                a.CreatedAt
                // AccessToken intentionally excluded from response
            })
            .ToListAsync();

        return Ok(accounts);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Remove(Guid id)
    {
        var account = await _db.LinkedAccounts.FindAsync(id);
        if (account == null) return NotFound();

        await _plaid.RemoveItem(account.AccessToken);
        // RemoveItem handles DB deletion internally
        return NoContent();
    }
}
```

---

## Task 2.6 — Register Services in Program.cs

Add the following registrations to `Program.cs`, before `builder.Build()`:

```csharp
// ── Services ──────────────────────────────────────────────────────────────
builder.Services.AddSingleton<EncryptionService>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddHttpClient<PlaidService>();
builder.Services.AddScoped<PlaidService>();
```

`EncryptionService` is a singleton because the key is read once from config.
`PlaidService` is scoped and uses `IHttpClientFactory` via `AddHttpClient<T>()`.

---

## Task 2.7 — Verification

Test with `curl` after starting the app (`dotnet run` from `Spening/`):

```bash
# Get all settings
curl http://localhost:8080/api/settings
# Expected: {"plaid_environment":"sandbox","output_year":"2026"}

# Update the year
curl -X PUT http://localhost:8080/api/settings \
  -H "Content-Type: application/json" \
  -d '{"output_year":"2025"}'
# Expected: updated settings object

# List accounts (empty at this stage)
curl http://localhost:8080/api/accounts
# Expected: []
```

Phase is complete when all three curl commands return expected responses.

---

## Proceed to Phase 3

Continue with [PHASE-3.md](./PHASE-3.md).
