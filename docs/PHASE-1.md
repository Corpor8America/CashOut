# Phase 1 — Project Scaffold, Models, Migrations, Docker Compose

## Progress Tracker

Check off each task as it is completed. On resume, inspect existing files to determine state.

- [ ] 1.1 Create solution and project
- [ ] 1.2 Add NuGet packages
- [ ] 1.3 Create `.env.example` and `appsettings.json`
- [ ] 1.4 Create EF Core models
- [ ] 1.5 Create `AppDbContext`
- [ ] 1.6 Register services in `Program.cs`
- [ ] 1.7 Run EF Core initial migration
- [ ] 1.8 Create `docker-compose.dev.yml`
- [ ] 1.9 Verify: app starts and tables exist in DB

---

## Context

This phase produces a runnable (but featureless) app that connects to PostgreSQL and applies the
database schema. No Plaid calls, no UI, no API logic yet. The goal is a green baseline to build on.

---

## Task 1.1 — Create Solution and Project

Create a new solution with a single ASP.NET Core web project:

```bash
mkdir spening && cd spening
dotnet new sln -n Spening
dotnet new web -n Spening -o Spening --framework net9.0
dotnet sln add Spening/Spening.csproj
```

The `dotnet new web` template gives a minimal `Program.cs`. You will replace its contents entirely
in Task 1.6.

Also initialise a git repository:

```bash
git init
echo ".env" >> .gitignore
echo "bin/" >> .gitignore
echo "obj/" >> .gitignore
echo "*.user" >> .gitignore
```

---

## Task 1.2 — Add NuGet Packages

```bash
cd Spening
dotnet add package Microsoft.EntityFrameworkCore --version 9.*
dotnet add package Microsoft.EntityFrameworkCore.Design --version 9.*
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL --version 9.*
dotnet add package Microsoft.AspNetCore.Components.Web --version 9.*
```

EF Core Design is needed for migration tooling and should be marked as a development dependency
(`PrivateAssets="All"`) in the `.csproj`:

```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="9.*">
  <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
  <PrivateAssets>all</PrivateAssets>
</PackageReference>
```

---

## Task 1.3 — Configuration Files

### `.env.example` (repository root)
```
PLAID_CLIENT_ID=your_client_id_here
PLAID_SANDBOX_SECRET=your_sandbox_secret_here
PLAID_PRODUCTION_SECRET=your_production_secret_here
ENCRYPTION_KEY=base64_encoded_32_byte_key_here
DB_PASSWORD=changeme
```

Generate a valid `ENCRYPTION_KEY` value with:
```bash
openssl rand -base64 32
```

### `Spening/appsettings.json`
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

No secrets go here. The connection string and Plaid credentials are injected via environment
variables at runtime.

---

## Task 1.4 — EF Core Models

Create the following files under `Spening/Models/`.

### `Models/LinkedAccount.cs`
```csharp
public class LinkedAccount
{
    public Guid Id { get; set; }
    public string AccessToken { get; set; } = "";   // stored encrypted
    public string AccountId { get; set; } = "";     // Plaid account_id
    public string Mask { get; set; } = "";
    public string Name { get; set; } = "";
    public string Subtype { get; set; } = "";
    public string Institution { get; set; } = "";
    public string? SyncCursor { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

### `Models/Transaction.cs`
```csharp
public class Transaction
{
    public string TransactionId { get; set; } = ""; // Plaid's stable ID — PK
    public string AccountId { get; set; } = "";
    public DateOnly Date { get; set; }
    public string Name { get; set; } = "";
    public decimal Amount { get; set; }
    public string Category { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

### `Models/AppSetting.cs`
```csharp
public class AppSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
```

---

## Task 1.5 — AppDbContext

Create `Spening/Data/AppDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<LinkedAccount> LinkedAccounts => Set<LinkedAccount>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LinkedAccount>(e =>
        {
            e.ToTable("linked_accounts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.AccountId).IsRequired();
            e.HasIndex(x => x.AccountId).IsUnique();
            e.Property(x => x.CreatedAt)
             .HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<Transaction>(e =>
        {
            e.ToTable("transactions");
            e.HasKey(x => x.TransactionId);
            e.Property(x => x.TransactionId).ValueGeneratedNever();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<AppSetting>(e =>
        {
            e.ToTable("app_settings");
            e.HasKey(x => x.Key);

            // Seed default settings
            e.HasData(
                new AppSetting { Key = "plaid_environment", Value = "sandbox" },
                new AppSetting { Key = "output_year", Value = DateTime.UtcNow.Year.ToString() }
            );
        });
    }
}
```

---

## Task 1.6 — Program.cs

Replace the contents of `Spening/Program.cs` with a minimal but complete startup that will be
extended in later phases. Add services as they are needed — this is the initial version:

```csharp
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

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

var app = builder.Build();

// ── Auto-migrate on startup ───────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.UseStaticFiles();
app.UseRouting();
app.MapControllers();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
```

Create a minimal `Pages/_Host.cshtml` (standard Blazor Server host page):

```html
@page "/"
@namespace Spening.Pages
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>Spening</title>
    <link rel="stylesheet" href="~/app.css" />
    <link rel="stylesheet" href="~/_content/Microsoft.AspNetCore.Components.WebAssembly.Authentication/AuthenticationService.js" />
</head>
<body>
    <component type="typeof(App)" render-mode="ServerPrerendered" />
    <script src="_framework/blazor.server.js"></script>
</body>
</html>
```

Create a minimal `App.razor` in the `Spening/` root:

```razor
<Router AppAssembly="@typeof(App).Assembly">
    <Found Context="routeData">
        <RouteView RouteData="@routeData" DefaultLayout="@typeof(MainLayout)" />
    </Found>
    <NotFound>
        <p>Page not found.</p>
    </NotFound>
</Router>
```

Create `Shared/MainLayout.razor` (placeholder — Phase 6 will flesh this out):

```razor
@inherits LayoutComponentBase
<main>
    @Body
</main>
```

Create a minimal `wwwroot/app.css` (empty for now — Phase 6 will add styles).

---

## Task 1.7 — EF Core Initial Migration

With the DB container running (see Task 1.8 first if needed), generate the migration:

```bash
cd Spening
dotnet ef migrations add InitialCreate --output-dir Data/Migrations
```

If the DB is not yet running, start it first:

```bash
cd ..   # back to repo root
docker compose -f docker-compose.dev.yml up db -d
# wait ~5 seconds for postgres to be ready
cd Spening
dotnet ef database update
```

The migration should create three tables: `linked_accounts`, `transactions`, `app_settings`.
The `app_settings` table should be seeded with two rows.

---

## Task 1.8 — docker-compose.dev.yml

Create at repository root. This file is used for local development only — it builds the app from
source and exposes the DB port for direct access with a DB tool.

```yaml
version: "3.9"

services:
  db:
    image: postgres:16-alpine
    restart: unless-stopped
    environment:
      POSTGRES_DB: spening
      POSTGRES_USER: spening
      POSTGRES_PASSWORD: ${DB_PASSWORD}
    volumes:
      - pgdata_dev:/var/lib/postgresql/data
    ports:
      - "5432:5432"
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U spening"]
      interval: 5s
      timeout: 5s
      retries: 5

  app:
    build:
      context: .
      dockerfile: Spening/Dockerfile
    restart: unless-stopped
    depends_on:
      db:
        condition: service_healthy
    environment:
      - PLAID_CLIENT_ID=${PLAID_CLIENT_ID}
      - PLAID_SANDBOX_SECRET=${PLAID_SANDBOX_SECRET}
      - PLAID_PRODUCTION_SECRET=${PLAID_PRODUCTION_SECRET}
      - ENCRYPTION_KEY=${ENCRYPTION_KEY}
      - ConnectionStrings__Default=Host=db;Database=spening;Username=spening;Password=${DB_PASSWORD}
      - ASPNETCORE_ENVIRONMENT=Development
    ports:
      - "8080:8080"

volumes:
  pgdata_dev:
```

Note: The `Dockerfile` is created in Phase 7. To run the app locally before Phase 7, use
`dotnet run` from the `Spening/` directory with a local `.env` file sourced into the shell,
or set the environment variables manually.

---

## Task 1.9 — Verification

The phase is complete when:

1. `dotnet build` in the `Spening/` directory exits with 0 errors.
2. `docker compose -f docker-compose.dev.yml up db -d` starts the DB container successfully.
3. `dotnet ef database update` (from `Spening/`) applies migrations without error.
4. Connecting to the DB (e.g. `psql -h localhost -U spening -d spening`) shows three tables:
   `linked_accounts`, `transactions`, `app_settings`.
5. `SELECT * FROM app_settings;` returns two rows: `plaid_environment=sandbox` and
   `output_year=<current year>`.

---

## Proceed to Phase 2

Once all checkboxes are ticked and verification passes, continue with [PHASE-2.md](./PHASE-2.md).
