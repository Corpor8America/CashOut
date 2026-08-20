# Step 01 — Project Scaffolding & Database Setup

**Goal:** From an empty directory, create a buildable .NET Blazor Server project with a running PostgreSQL database and the full v2 schema applied via EF Core migrations.

---

## 1.1 Create the .NET project

```bash
dotnet new blazorserver -n CashOut --no-https --framework net9.0
cd CashOut
```

## 1.2 Add NuGet packages

```bash
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL --version "9.*"
dotnet add package Microsoft.EntityFrameworkCore.Design --version "9.*"
dotnet add package MudBlazor --version "9.5.0"
dotnet add package DotNetEnv --version "3.2.0"
dotnet add package PdfPig --version "0.1.15"
dotnet add package System.Net.Http --version "4.3.4"
dotnet add package System.Text.RegularExpressions --version "4.3.1"
dotnet add package Microsoft.AspNetCore.Components.Web --version "9.*"
dotnet add package Microsoft.Extensions.Configuration --version "10.0.11"
```

## 1.3 Create Docker Compose

**File:** `docker-compose.dev.yml` (at repo root, one level above `CashOut/`)

```yaml
version: "3.9"

services:
  db:
    image: postgres:16-alpine
    restart: unless-stopped
    environment:
      POSTGRES_DB: cashout
      POSTGRES_USER: cashout
      POSTGRES_PASSWORD: ${DB_PASSWORD}
    volumes:
      - pgdata_dev:/var/lib/postgresql/data
    ports:
      - "5432:5432"
    healthcheck:
      test: [ "CMD-SHELL", "pg_isready -U cashout" ]
      interval: 5s
      timeout: 5s
      retries: 5

  pgadmin:
    image: dpage/pgadmin4:latest
    restart: unless-stopped
    depends_on:
      db:
        condition: service_healthy
    environment:
      PGADMIN_DEFAULT_EMAIL: admin@admin.com
      PGADMIN_DEFAULT_PASSWORD: ${DB_PASSWORD}
      PGADMIN_CONFIG_SERVER_MODE: "False"
    ports:
      - "5050:80"
    volumes:
      - pgadmin_data:/var/lib/pgadmin

  app:
    build:
      context: .
      dockerfile: CashOut/Dockerfile
    restart: unless-stopped
    depends_on:
      db:
        condition: service_healthy
    environment:
      - PLAID_CLIENT_ID=${PLAID_CLIENT_ID}
      - PLAID_SANDBOX_SECRET=${PLAID_SANDBOX_SECRET}
      - PLAID_PRODUCTION_SECRET=${PLAID_PRODUCTION_SECRET}
      - PLAID_ENV=${PLAID_ENV}
      - ENCRYPTION_KEY=${ENCRYPTION_KEY}
      - ConnectionStrings__Default=Host=db;Database=cashout;Username=cashout;Password=${DB_PASSWORD}
      - ASPNETCORE_ENVIRONMENT=Development
    ports:
      - "8080:8080"

volumes:
  pgdata_dev:
  pgadmin_data:
```

## 1.4 Create .env

**File:** `.env` (at repo root)

```
PLAID_CLIENT_ID=your_client_id_here
PLAID_SANDBOX_SECRET=your_sandbox_secret_here
PLAID_PRODUCTION_SECRET=your_production_secret_here
PLAID_ENV=sandbox

# 32-byte base64-encoded key for AES-256 access token encryption
# Generate with: openssl rand -base64 32
ENCRYPTION_KEY=your_base64_32_byte_key_here

DB_PASSWORD=your_db_password_here

ConnectionStrings__Default=Host=localhost;Database=cashout;Username=cashout;Password=your_db_password_here
```

## 1.5 Create Dockerfile

**File:** `CashOut/Dockerfile`

```dockerfile
# ── Stage 1: Build ────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy VERSION file and project file first for better caching
COPY VERSION ./
COPY CashOut/CashOut.csproj ./CashOut/
RUN dotnet restore CashOut/CashOut.csproj

# Copy the rest of the source
COPY CashOut/ ./CashOut/

RUN dotnet publish CashOut/CashOut.csproj \
    --configuration Release \
    --output /app/publish

# ── Stage 2: Runtime ──────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

RUN adduser --disabled-password --no-create-home appuser
USER appuser

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "CashOut.dll"]
```

## 1.6 Create VERSION file

**File:** `VERSION` (at repo root)

```
1.0.0-beta.001
```

## 1.7 Project file

**File:** `CashOut/CashOut.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="DotNetEnv" Version="3.2.0" />
    <PackageReference Include="Microsoft.AspNetCore.Components.Web" Version="9.*" />
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="9.*" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="9.*">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.Extensions.Configuration" Version="10.0.11" />
    <PackageReference Include="MudBlazor" Version="9.5.0" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="9.*" />
    <PackageReference Include="PdfPig" Version="0.1.15" />
    <PackageReference Include="System.Net.Http" Version="4.3.4" />
    <PackageReference Include="System.Text.RegularExpressions" Version="4.3.1" />
  </ItemGroup>

  <!-- Copy VERSION file from repo root into publish output -->
  <ItemGroup>
    <Content Include="..\VERSION">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
      <CopyToPublishDirectory>Always</CopyToPublishDirectory>
      <Link>VERSION</Link>
    </Content>
  </ItemGroup>

</Project>
```

## 1.8 Launch settings

**File:** `CashOut/Properties/launchSettings.json`

```json
{
  "$schema": "https://json.schemastore.org/launchsettings.json",
  "profiles": {
    "http": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": true,
      "applicationUrl": "http://localhost:5200",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    },
    "https": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": true,
      "applicationUrl": "https://localhost:7013;http://localhost:5200",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

## 1.9 App settings

**File:** `CashOut/appsettings.json`

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

**File:** `CashOut/appsettings.Development.json`

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

## 1.10 Entity Models

**File:** `CashOut/Models/Transaction.cs`

```csharp
public enum TransactionSource { Plaid, CSV }

public class Transaction
{
    public string TransactionId { get; set; } = "";
    public string AccountId { get; set; } = "";
    public TransactionSource Source { get; set; } = TransactionSource.Plaid;
    public DateOnly Date { get; set; }
    public string Name { get; set; } = "";
    public string RawName { get; set; } = "";

    /// <summary>
    /// Money entering the account (e.g. payroll, refund).
    /// Exactly one of Credit or Debit is non-null per transaction.
    /// </summary>
    public decimal? Credit { get; set; }

    /// <summary>
    /// Money leaving the account (e.g. purchase, bill payment).
    /// Exactly one of Credit or Debit is non-null per transaction.
    /// </summary>
    public decimal? Debit { get; set; }

    /// <summary>
    /// Computed: Debit - Credit.
    /// Positive = net outflow (expense). Negative = net inflow (income).
    /// </summary>
    public decimal Amount { get; set; }

    public string Category { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // ── Amount normalization helpers ─────────────────────────────────────

    public static (decimal? credit, decimal? debit, decimal amount) NormalizeSingleAmount(
        decimal externalAmount)
    {
        if (externalAmount < 0)
        {
            var credit = Math.Abs(externalAmount);
            return (credit, null, -credit);
        }
        else
        {
            return (null, externalAmount, externalAmount);
        }
    }

    public static (decimal? credit, decimal? debit, decimal amount) NormalizeSplitColumns(
        decimal? rawCredit, decimal? rawDebit)
    {
        if (rawCredit.HasValue && !rawDebit.HasValue)
        {
            var c = Math.Abs(rawCredit.Value);
            return (c, null, -c);
        }
        if (rawDebit.HasValue && !rawCredit.HasValue)
        {
            var d = Math.Abs(rawDebit.Value);
            return (null, d, d);
        }
        return (null, null, 0);
    }
}
```

**File:** `CashOut/Models/LinkedAccount.cs`

```csharp
public class LinkedAccount
{
    public Guid Id { get; set; }
    public string AccessToken { get; set; } = "";
    public string AccountId { get; set; } = "";
    public string ItemId { get; set; } = "";
    public string Mask { get; set; } = "";
    public string Name { get; set; } = "";
    public string Subtype { get; set; } = "";
    public string Institution { get; set; } = "";
    public string? SyncCursor { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

**File:** `CashOut/Models/ManualAccount.cs`

```csharp
public class ManualAccount
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
```

**File:** `CashOut/Models/CsvMappingProfile.cs`

```csharp
using System.Text.Json;

public class CsvMappingProfile
{
    public int Id { get; set; }
    public string AccountId { get; set; } = "";
    public int Version { get; set; } = 1;
    public int SkipRowsFromTop { get; set; } = 0;
    public int SkipRowsFromBottom { get; set; } = 0;
    public string DateColumn { get; set; } = "";
    public string DescriptionColumn { get; set; } = "";
    public string? CreditColumn { get; set; }
    public string? DebitColumn { get; set; }
    public string? AmountColumn { get; set; }
    public string? CategoryColumn { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public IEnumerable<string> MappedColumns()
    {
        if (!string.IsNullOrEmpty(DateColumn)) yield return DateColumn.ToLowerInvariant();
        if (!string.IsNullOrEmpty(DescriptionColumn)) yield return DescriptionColumn.ToLowerInvariant();
        if (!string.IsNullOrEmpty(CreditColumn)) yield return CreditColumn.ToLowerInvariant();
        if (!string.IsNullOrEmpty(DebitColumn)) yield return DebitColumn.ToLowerInvariant();
        if (!string.IsNullOrEmpty(AmountColumn)) yield return AmountColumn.ToLowerInvariant();
        if (!string.IsNullOrEmpty(CategoryColumn)) yield return CategoryColumn.ToLowerInvariant();
    }
}
```

## 1.11 DbContext

**File:** `CashOut/Data/AppDbContext.cs`

```csharp
using Microsoft.EntityFrameworkCore;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<LinkedAccount> LinkedAccounts => Set<LinkedAccount>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<ManualAccount> ManualAccounts => Set<ManualAccount>();
    public DbSet<CsvMappingProfile> CsvMappingProfiles => Set<CsvMappingProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ── LinkedAccount ─────────────────────────────────────────────────
        modelBuilder.Entity<LinkedAccount>(e =>
        {
            e.ToTable("linked_accounts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.AccountId).IsRequired();
            e.HasIndex(x => x.AccountId).IsUnique();
            e.Property(x => x.ItemId).IsRequired().HasDefaultValue("");
            e.HasIndex(x => x.ItemId);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");
        });

        // ── ManualAccount ─────────────────────────────────────────────────
        modelBuilder.Entity<ManualAccount>(e =>
        {
            e.ToTable("manual_accounts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Name).IsRequired();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");
        });

        // ── Transaction ───────────────────────────────────────────────────
        modelBuilder.Entity<Transaction>(e =>
        {
            e.ToTable("transactions");
            e.HasKey(x => x.TransactionId);
            e.Property(x => x.TransactionId).ValueGeneratedNever();
            e.Property(x => x.Source).HasConversion<string>().IsRequired();
            e.Property(x => x.Credit).IsRequired(false);
            e.Property(x => x.Debit).IsRequired(false);
            e.Property(x => x.Amount).IsRequired();
            e.Property(x => x.RawName).IsRequired().HasDefaultValue("");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now() at time zone 'utc'");
        });

        // ── CsvMappingProfile ─────────────────────────────────────────────
        modelBuilder.Entity<CsvMappingProfile>(e =>
        {
            e.ToTable("csv_mapping_profiles");
            e.HasKey(x => x.Id);
            e.Property(x => x.AccountId).IsRequired();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now() at time zone 'utc'");
        });
    }
}
```

## 1.12 Design-time DbContext Factory

**File:** `CashOut/Data/AppDbContextFactory.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        DotNetEnv.Env.Load("../.env");

        var config = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        var conn = config["ConnectionStrings:Default"];

        if (string.IsNullOrWhiteSpace(conn))
            throw new InvalidOperationException("ConnectionStrings:Default is required.");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(conn)
            .Options;

        return new AppDbContext(options);
    }
}
```

## 1.13 Program.cs

**File:** `CashOut/Program.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http.Features;
using System.Globalization;
using MudBlazor;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

var culture = new CultureInfo("en-US");
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

// ── Database ──────────────────────────────────────────────────────────────
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Default is required. Set it via environment variable " +
        "ConnectionStrings__Default.");

builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseNpgsql(connectionString));

// ── Blazor + API ──────────────────────────────────────────────────────────
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddControllers();

builder.Services.Configure<FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 11 * 1024 * 1024;
});

// ── Services ──────────────────────────────────────────────────────────────
// NOTE: Services will be registered in Step 02.
// For now, add a placeholder comment. Do NOT register services that don't exist yet.
// builder.Services.AddSingleton<EncryptionService>();
// builder.Services.AddScoped<SettingsService>();
// etc.

builder.Services.AddMudServices();

// ── HttpClient for Blazor pages ───────────────────────────────────────────
builder.Services.AddScoped<HttpClient>(sp =>
{
    var urls = builder.Configuration["ASPNETCORE_URLS"]
               ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS")
               ?? (builder.Environment.IsDevelopment() ? "http://localhost:5200" : "http://localhost:8080");

    var firstUrl = urls.Split(';')[0]
        .Replace("http://+:", "http://localhost:")
        .Replace("https://+:", "https://localhost:")
        .TrimEnd('/');

    return new HttpClient { BaseAddress = new Uri(firstUrl + "/") };
});

var app = builder.Build();

// ── Auto-migrate on startup ──────────────────────────────────────────────
{
    var maxRetries = 10;
    for (var attempt = 1; attempt <= maxRetries; attempt++)
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.Migrate();
            break;
        }
        catch (Exception ex) when (attempt < maxRetries)
        {
            var delay = TimeSpan.FromSeconds(Math.Min(attempt * 2, 10));
            Console.WriteLine($"Migration attempt {attempt}/{maxRetries} failed ({ex.GetType().Name}). Retrying in {delay.TotalSeconds}s...");
            Thread.Sleep(delay);
        }
    }
}

app.UseStaticFiles();
app.UseRouting();

app.MapControllers();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
```

## 1.14 Blazor scaffolding files

**File:** `CashOut/App.razor`

```razor
@using Microsoft.AspNetCore.Components.Routing
@using CashOut.Shared

<Router AppAssembly="@typeof(App).Assembly">
    <Found Context="routeData">
        <RouteView RouteData="@routeData" DefaultLayout="@typeof(MainLayout)" />
    </Found>
    <NotFound>
        <p>Page not found.</p>
    </NotFound>
</Router>
```

**File:** `CashOut/Pages/_Host.cshtml`

```cshtml
@page "/{**catchall}"
@namespace CashOut.Pages
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
<!DOCTYPE html>
<html lang="en">

<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>CashOut</title>
    <link href="https://fonts.googleapis.com/css?family=Roboto:300,400,500,700&display=swap" rel="stylesheet" />
    <link href="_content/MudBlazor/MudBlazor.min.css" rel="stylesheet" />
    <link rel="stylesheet" href="~/app.css" asp-append-version="true" />
    <link rel="icon" type="image/png" sizes="32x32" href="/favicon-32x32.png">
    <link rel="icon" type="image/png" sizes="16x16" href="/favicon-16x16.png">
    <link rel="apple-touch-icon" sizes="180x180" href="/apple-touch-icon.png">
    <link rel="manifest" href="/site.webmanifest">
</head>

<body>
    <component type="typeof(App)" render-mode="Server" />
    <script src="_content/MudBlazor/MudBlazor.min.js"></script>
    <script src="~/plaidLink.js"></script>
    <script src="_framework/blazor.server.js"></script>
</body>

</html>
```

**File:** `CashOut/Pages/_Imports.razor`

```razor
@using System.Net.Http
@using System.Net.Http.Json
@using Microsoft.AspNetCore.Components
@using Microsoft.AspNetCore.Components.Forms
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using Microsoft.AspNetCore.Components.Web.Virtualization
@using Microsoft.JSInterop
@using MudBlazor
@using CashOut
@using CashOut.Shared
```

## 1.15 Static files

**File:** `CashOut/wwwroot/app.css`

```css
*,
*::before,
*::after {
    box-sizing: border-box;
    margin: 0;
    padding: 0;
}

body {
    font-family: 'Roboto', sans-serif;
    font-size: 14px;
    background: #f5f5f5;
    color: #1a1a1a;
}

.text-muted {
    color: #888;
    font-size: 0.85em;
}

.mud-table-container {
    overflow-x: auto;
}

code {
    background-color: #f1f1f1;
    color: #1a1a1a;
    padding: 0.1rem 0.3rem;
    border-radius: 4px;
    font-family: 'Roboto Mono', monospace;
    font-size: 0.9em;
}

.mud-chip code {
    background-color: transparent !important;
    padding: 0 !important;
    color: inherit !important;
    font-size: inherit !important;
    border-radius: 0 !important;
}

.mud-chip.mud-chip-color-tertiary {
    background-color: #e6f7f4 !important;
    color: #0f5142 !important;
    border: 1px solid #b2e7dd !important;
}

.mud-chip.mud-chip-color-tertiary .mud-chip-close-button {
    color: #0f5142 !important;
}

.mud-alert {
    margin-bottom: 1rem;
}
```

**File:** `CashOut/wwwroot/plaidLink.js`

```javascript
window.cashoutPlaid = {
    handler: null,
    _sdkLoaded: false,

    _loadSdk: function (callback) {
        if (window.cashoutPlaid._sdkLoaded && typeof window.Plaid !== 'undefined') {
            callback();
            return;
        }

        var script = document.createElement('script');
        script.src = 'https://cdn.plaid.com/link/v2/stable/link-initialize.js';
        script.onload = function () {
            window.cashoutPlaid._sdkLoaded = true;
            console.log('[Plaid] SDK loaded');
            callback();
        };
        script.onerror = function () {
            console.error('[Plaid] Failed to load SDK script');
        };
        document.head.appendChild(script);
    },

    open: function (linkToken, dotNetRef) {
        console.log('[Plaid] open() called, linkToken prefix:',
            linkToken ? linkToken.substring(0, 20) + '...' : 'NULL');

        window.cashoutPlaid._loadSdk(function () {
            var attempts = 0;
            var maxAttempts = 50;

            var tryOpen = function () {
                attempts++;
                if (typeof window.Plaid === 'undefined') {
                    if (attempts >= maxAttempts) {
                        var msg = 'Plaid SDK failed to initialise after 5s. Check network/CSP.';
                        console.error('[Plaid]', msg);
                        dotNetRef.invokeMethodAsync('OnPlaidError', msg);
                        return;
                    }
                    setTimeout(tryOpen, 100);
                    return;
                }

                console.log('[Plaid] window.Plaid ready, creating handler...');
                try {
                    window.cashoutPlaid.handler = window.Plaid.create({
                        token: linkToken,
                        onSuccess: function (public_token, metadata) {
                            console.log('[Plaid] onSuccess');
                            dotNetRef.invokeMethodAsync('OnPlaidSuccess', public_token);
                        },
                        onExit: function (err, metadata) {
                            console.log('[Plaid] onExit, err:', err);
                            dotNetRef.invokeMethodAsync('OnPlaidExit');
                        },
                        onEvent: function (eventName) {
                            console.log('[Plaid] event:', eventName);
                        }
                    });

                    window.cashoutPlaid.handler.open();
                    console.log('[Plaid] handler.open() called');
                } catch (e) {
                    console.error('[Plaid] exception during create/open:', e);
                    dotNetRef.invokeMethodAsync('OnPlaidError',
                        'Failed to open Plaid Link: ' + e.message);
                }
            };

            tryOpen();
        });
    },

    destroy: function () {
        if (window.cashoutPlaid.handler) {
            try { window.cashoutPlaid.handler.destroy(); } catch (e) { }
            window.cashoutPlaid.handler = null;
        }
    }
};

console.log('[Plaid] plaidLink.js loaded');
```

**File:** `CashOut/wwwroot/site.webmanifest`

```json
{"name":"","short_name":"","icons":[{"src":"/android-chrome-192x192.png","sizes":"192x192","type":"image/png"},{"src":"/android-chrome-512x512.png","sizes":"512x512","type":"image/png"}],"theme_color":"#ffffff","background_color":"#ffffff","display":"standalone"}
```

## 1.16 Helpers

**File:** `CashOut/Helpers/DateHelper.cs`

```csharp
namespace CashOut.Helpers;

public static class DateHelper
{
    public static string MonthName(int month) =>
        new DateOnly(2000, month, 1).ToString("MMMM");
}
```

## 1.17 Minimal placeholder pages

Create these placeholder pages so the app can compile:

**File:** `CashOut/Pages/Index.razor`

```razor
@page "/"
@inject NavigationManager Nav

@code {
    protected override void OnInitialized() => Nav.NavigateTo("/accounts", replace: true);
}
```

## 1.18 Generate initial migration

```bash
# Ensure DB is running
docker-compose -f docker-compose.dev.yml up db -d

# Generate migration
dotnet ef migrations add InitialSchema --project CashOut

# Verify build compiles
dotnet build CashOut/CashOut.csproj
```

---

## Verification

1. `docker-compose -f docker-compose.dev.yml up db -d` starts PostgreSQL
2. `dotnet ef migrations add InitialSchema --project CashOut` creates migration
3. `dotnet build CashOut/CashOut.csproj` succeeds
4. The `transactions` table has columns: `TransactionId`, `AccountId`, `Source`, `Date`, `Name`, `RawName`, `Credit`, `Debit`, `Amount`, `Category`, `CreatedAt`, `UpdatedAt`
5. No `NormalizedName`, `AliasId`, `RawBusinessId`, or normalization tables exist
