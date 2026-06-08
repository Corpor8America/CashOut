# Phase 3 — Plaid Link Flow & Account Linking

## Progress Tracker

- [ ] 3.1 Add `CreateLinkToken` to `PlaidService`
- [ ] 3.2 Add `ExchangePublicToken` to `PlaidService`
- [ ] 3.3 Create `PlaidLinkController`
- [ ] 3.4 Create the Plaid Link JS interop helper (`plaidLink.js`)
- [ ] 3.5 Create `Accounts.razor` page (add + list + remove)
- [ ] 3.6 Verify full add-account flow in browser with sandbox credentials

---

## Context

This phase implements the full account-linking flow. The user clicks "Add Account", the browser
opens the Plaid Link modal, the user selects a bank (using sandbox test credentials), and the
resulting `public_token` is exchanged server-side for a persistent `access_token`.

### Plaid Link Flow (step by step)

```
1. Blazor page calls POST /api/plaid/link-token
2. Server calls Plaid /link/token/create → returns link_token (expires in 30 min)
3. Blazor passes link_token to the Plaid Link JS SDK via JS interop
4. Plaid Link modal opens in the browser
5. User selects institution and completes auth (sandbox: use test credentials)
6. Plaid calls onSuccess(public_token, metadata) in JS
7. JS calls DotNet.invokeMethodAsync('Spening', 'OnPlaidSuccess', public_token)
8. Blazor C# method receives public_token, calls POST /api/plaid/exchange
9. Server exchanges public_token → access_token via Plaid /item/public_token/exchange
10. Server calls /accounts/get, encrypts token, persists accounts to DB
11. Blazor refreshes the account list
```

### Sandbox Test Credentials
- Institution: any — search for "Chase", "Bank of America", etc.
- Username: `user_good`
- Password: `pass_good`

---

## Task 3.1 — Add `CreateLinkToken` to PlaidService

Add the following method to `Services/PlaidService.cs`:

```csharp
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
```

---

## Task 3.2 — Add `ExchangePublicToken` to PlaidService

Add the following method to `Services/PlaidService.cs`:

```csharp
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
```

---

## Task 3.3 — PlaidLinkController

Create `Spening/Controllers/PlaidLinkController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/plaid")]
public class PlaidLinkController : ControllerBase
{
    private readonly PlaidService _plaid;

    public PlaidLinkController(PlaidService plaid) => _plaid = plaid;

    /// <summary>Step 1: generate a link_token for the browser to initialise Plaid Link.</summary>
    [HttpPost("link-token")]
    public async Task<IActionResult> CreateLinkToken()
    {
        var token = await _plaid.CreateLinkToken();
        return Ok(new { link_token = token });
    }

    /// <summary>Step 2: exchange the public_token the browser received from Plaid Link.</summary>
    [HttpPost("exchange")]
    public async Task<IActionResult> Exchange([FromBody] ExchangeRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.PublicToken))
            return BadRequest(new { error = "public_token is required" });

        var accounts = await _plaid.ExchangeAndPersist(req.PublicToken);

        return Ok(accounts.Select(a => new
        {
            a.Id,
            a.Name,
            a.Mask,
            a.Subtype,
            a.Institution
        }));
    }

    public record ExchangeRequest(string PublicToken);
}
```

---

## Task 3.4 — Plaid Link JS Interop Helper

Plaid Link is initialised via their CDN JavaScript SDK. Blazor communicates with it through
JS interop.

Create `Spening/wwwroot/plaidLink.js`:

```javascript
// Loaded once on the Accounts page.
// Blazor calls window.speningPlaid.open(linkToken, dotNetRef)
// On success, invokes dotNetRef.invokeMethodAsync('OnPlaidSuccess', publicToken)
// On exit/cancel, invokes dotNetRef.invokeMethodAsync('OnPlaidExit')

window.speningPlaid = {
    handler: null,

    open: function (linkToken, dotNetRef) {
        if (!window.Plaid) {
            console.error('Plaid Link SDK not loaded.');
            return;
        }

        this.handler = window.Plaid.create({
            token: linkToken,
            onSuccess: function (public_token, metadata) {
                dotNetRef.invokeMethodAsync('OnPlaidSuccess', public_token);
            },
            onExit: function (err, metadata) {
                dotNetRef.invokeMethodAsync('OnPlaidExit');
            }
        });

        this.handler.open();
    }
};
```

---

## Task 3.5 — Accounts.razor

Create `Spening/Pages/Accounts.razor`. This page:
- Lists all linked accounts in a table
- Has an "Add Account" button that triggers the Plaid Link flow
- Has a "Remove" button per row with a confirmation step

```razor
@page "/accounts"
@inject HttpClient Http
@inject IJSRuntime JS
@implements IDisposable

<h2>Linked Accounts</h2>

@if (_error != null)
{
    <p style="color:red">@_error</p>
}

@if (_loading)
{
    <p>Loading...</p>
}
else if (_accounts.Count == 0)
{
    <p>No accounts linked yet.</p>
}
else
{
    <table>
        <thead>
            <tr>
                <th>Institution</th>
                <th>Name</th>
                <th>Account #</th>
                <th>Type</th>
                <th></th>
            </tr>
        </thead>
        <tbody>
            @foreach (var acct in _accounts)
            {
                <tr>
                    <td>@acct.Institution</td>
                    <td>@acct.Name</td>
                    <td>••••@acct.Mask</td>
                    <td>@acct.Subtype</td>
                    <td>
                        @if (_confirmRemoveId == acct.Id)
                        {
                            <span>Sure? </span>
                            <button @onclick="() => ConfirmRemove(acct.Id)">Yes, remove</button>
                            <button @onclick="CancelRemove">Cancel</button>
                        }
                        else
                        {
                            <button @onclick="() => RequestRemove(acct.Id)">Remove</button>
                        }
                    </td>
                </tr>
            }
        </tbody>
    </table>
}

<br />
<button @onclick="StartLinkFlow" disabled="@_linking">
    @(_linking ? "Connecting..." : "Add Account")
</button>

@if (_newAccounts.Count > 0)
{
    <p style="color:green">Added @_newAccounts.Count account(s) successfully.</p>
}

@* Plaid Link SDK — loaded only on this page *@
<script src="https://cdn.plaid.com/link/v2/stable/link-initialize.js"></script>
<script src="~/plaidLink.js"></script>

@code {
    private List<AccountDto> _accounts = new();
    private List<object> _newAccounts = new();
    private bool _loading = true;
    private bool _linking = false;
    private string? _error;
    private Guid? _confirmRemoveId;
    private DotNetObjectReference<Accounts>? _dotNetRef;

    protected override async Task OnInitializedAsync() => await LoadAccounts();

    private async Task LoadAccounts()
    {
        _loading = true;
        _error = null;
        try
        {
            _accounts = await Http.GetFromJsonAsync<List<AccountDto>>("api/accounts")
                        ?? new();
        }
        catch (Exception ex)
        {
            _error = $"Failed to load accounts: {ex.Message}";
        }
        finally { _loading = false; }
    }

    private async Task StartLinkFlow()
    {
        _linking = true;
        _error = null;
        _newAccounts.Clear();

        try
        {
            var resp = await Http.PostAsync("api/plaid/link-token", null);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadFromJsonAsync<LinkTokenResponse>();

            _dotNetRef = DotNetObjectReference.Create(this);
            await JS.InvokeVoidAsync("speningPlaid.open", json!.LinkToken, _dotNetRef);
            // Control returns here; actual success/exit handled by JS callbacks below
        }
        catch (Exception ex)
        {
            _error = $"Could not start Plaid Link: {ex.Message}";
            _linking = false;
        }
    }

    [JSInvokable]
    public async Task OnPlaidSuccess(string publicToken)
    {
        try
        {
            var resp = await Http.PostAsJsonAsync("api/plaid/exchange",
                new { PublicToken = publicToken });
            resp.EnsureSuccessStatusCode();
            _newAccounts = await resp.Content.ReadFromJsonAsync<List<object>>() ?? new();
            await LoadAccounts();
        }
        catch (Exception ex)
        {
            _error = $"Account linking failed: {ex.Message}";
        }
        finally
        {
            _linking = false;
            StateHasChanged();
        }
    }

    [JSInvokable]
    public Task OnPlaidExit()
    {
        _linking = false;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private void RequestRemove(Guid id) => _confirmRemoveId = id;
    private void CancelRemove() => _confirmRemoveId = null;

    private async Task ConfirmRemove(Guid id)
    {
        _error = null;
        try
        {
            var resp = await Http.DeleteAsync($"api/accounts/{id}");
            resp.EnsureSuccessStatusCode();
            await LoadAccounts();
        }
        catch (Exception ex)
        {
            _error = $"Remove failed: {ex.Message}";
        }
        finally { _confirmRemoveId = null; }
    }

    public void Dispose() => _dotNetRef?.Dispose();

    // Local DTOs
    private record AccountDto(Guid Id, string Name, string Mask,
        string Subtype, string Institution);
    private record LinkTokenResponse(string LinkToken);
}
```

> **Note on HttpClient in Blazor Server:** Register a pre-configured `HttpClient` in `Program.cs`
> that points to the app's own base address. Add this in Phase 6's `Program.cs` update, or add it
> now:
> ```csharp
> builder.Services.AddScoped(sp =>
>     new HttpClient { BaseAddress = new Uri("http://localhost:8080/") });
> ```
> In production the base address will come from the request. A cleaner approach is to inject
> services directly in Blazor pages instead of going through HTTP — but using `HttpClient` keeps
> the API layer testable.

---

## Task 3.6 — Verification

1. Start the app: `dotnet run` from `Spening/`
2. Navigate to `http://localhost:8080/accounts`
3. Click "Add Account"
4. Plaid Link modal opens
5. Search for any institution (e.g. "Chase")
6. Enter sandbox credentials: username `user_good`, password `pass_good`
7. Select any account and click Continue
8. Page shows "Added N account(s) successfully"
9. Account table populates with the linked account
10. Verify in the DB: `SELECT name, institution, sync_cursor FROM linked_accounts;`

Phase is complete when accounts persist to the DB after the Plaid Link flow.

---

## Proceed to Phase 4

Continue with [PHASE-4.md](./PHASE-4.md).
