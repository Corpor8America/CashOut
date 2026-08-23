# Agent Instructions

## Two Workspaces

This repo contains two independent ASP.NET Core 9.0 Blazor Server apps sharing a git history:

| | V1 (`CashOut/`) | V2 (`CashOutV2/`) |
|---|---|---|
| Solution file | `CashOut.sln` (root) | None (build projects directly) |
| Scope | Full: Plaid, merchant normalization, encryption, accounts | Simplified: CSV-only import, single Account entity |
| DB entities | 9 DbSets (LinkedAccount, ManualAccount, Transaction, AppSetting, RawBusiness, BusinessAlias, AliasPattern, RawBusinessAliasMap, CsvMappingProfile) | 3 DbSets (Account, Transaction, CsvMappingProfile) |
| Tests | Unit + Playwright UI tests | Unit tests only (no Playwright) |
| Docker | Full stack with Plaid env vars | DB + pgadmin only |
| VERSION | `1.0.0-beta.023` | `1.0.0-beta.001` |

The root working directory for this session is `CashOutV2/`.

---

## EF Core Migrations

When modifying entity models, DbContext configuration, or adding/changing/removing properties on entities, you MUST generate an EF Core migration after the code changes.

### Triggers

- Adding, removing, or renaming properties on entity classes in `Models/`
- Changing `HasDefaultValueSql`, `HasConversion`, or other fluent configuration in `Data/AppDbContext.cs`
- Adding new entity classes or `DbSet<T>` properties

### Steps

1. Make the code changes to models and `AppDbContext`
2. Ensure the dev database is running (from whichever workspace you're in):
   ```bash
   # From V1 root
   docker-compose -f docker-compose.dev.yml up db -d
   # From V2 root
   docker-compose -f CashOutV2/docker-compose.dev.yml up db -d
   ```
3. Generate the migration (from the **repo root**, not the workspace directory):
   ```bash
   # V1
   dotnet ef migrations add <DescriptiveMigrationName> --project CashOut
   # V2
   dotnet ef migrations add <DescriptiveMigrationName> --project CashOutV2/CashOut
   ```
4. Verify the build compiles:
   ```bash
   # V1
   dotnet build CashOut/CashOut.csproj
   # V2
   dotnet build CashOutV2/CashOut/CashOut.csproj
   ```

### Connection

The design-time factory (`Data/AppDbContextFactory.cs`) reads `ConnectionStrings__Default` from the `.env` file one directory up from the project (`../.env`). Each workspace has its own `.env`:

- V1: root `.env`
- V2: `CashOutV2/.env`

Both require:
```
ConnectionStrings__Default=Host=localhost;Database=cashout;Username=cashout;Password=<DB_PASSWORD>
```

This is only needed for local `dotnet ef` CLI commands. The app gets its connection string from Docker environment variables at runtime.

### Naming conventions

Use descriptive PascalCase names: `AddUpdatedAtField`, `RenameCategoryColumn`, `AddTransactionIndex`, etc.

---

## Build & Test Commands

```bash
# ── V1 ──────────────────────────────────────────────────────
dotnet build                                          # builds V1 via CashOut.sln
dotnet test --filter "TestCategory!=UI"                # unit tests (excludes Playwright)
dotnet test --filter "TestCategory=UI"                 # UI tests (requires Docker stack on :8080)
dotnet test                                           # all tests

# ── V2 ──────────────────────────────────────────────────────
dotnet build CashOutV2/CashOut/CashOut.csproj          # build V2 from repo root
dotnet test CashOutV2/CashOut.Tests/CashOut.Tests.csproj  # unit tests only
# Or from CashOutV2/:
cd CashOutV2 && dotnet test

# ── Docker ──────────────────────────────────────────────────
docker-compose -f docker-compose.dev.yml up db -d      # V1: Postgres only
docker-compose -f docker-compose.dev.yml up -d --build # V1: full dev stack
docker-compose -f CashOutV2/docker-compose.dev.yml up -d --build  # V2: full dev stack
```

**Port conflict:** V1 and V2 docker-compose both bind `5432` (Postgres) and `8080` (app). Do not run both simultaneously.

---

## Project Structure (V2 — active workspace)

| Directory | Purpose |
|---|---|
| `CashOutV2/CashOut/Controllers/` | REST API endpoints (`/api/*`) |
| `CashOutV2/CashOut/Services/` | Business logic layer |
| `CashOutV2/CashOut/Models/` | EF Core entities |
| `CashOutV2/CashOut/Data/` | `AppDbContext`, design-time factory |
| `CashOutV2/CashOut/Pages/` | Blazor Server UI pages |
| `CashOutV2/CashOut/Shared/` | Layout components |
| `CashOutV2/CashOut.Tests/` | MSTest unit tests |
| `CashOutV2/docs/` | Design docs |

## Project Structure (V1)

| Directory | Purpose |
|---|---|
| `CashOut/Controllers/` | REST API (includes PlaidLink, Debug, BusinessNormalization, ManualAccounts, AccountReports) |
| `CashOut/Services/` | Business logic (includes PlaidService, EncryptionService, MerchantNormalizationService, AccountReportService) |
| `CashOut/Models/` | EF Core entities + DTOs |
| `CashOut/Data/` | `AppDbContext`, design-time factory, migrations |
| `CashOut/Pages/` | Blazor Server UI |
| `CashOut/Shared/` | Layout components |
| `CashOut.Tests/` | MSTest unit + Playwright UI tests |
| `docs/` | Design specs and refactor plans |

---

## Code Conventions

- **Namespaces:** No namespaces (global namespace). Tests use file-scoped `namespace CashOut.Tests;`
- **Nullable:** Enabled project-wide. Use `string?` for optional fields, `int?` for optional FKs
- **Strings:** Always initialized to `""`, never null
- **Private fields:** `_camelCase` prefix (`_db`, `_plaid`)
- **DB tables:** snake_case (`accounts`, `transactions`)
- **Controller routes:** kebab-case (`api/csv-import`)
- **Records:** Used for DTOs and request types
- **Enums:** Stored as strings in DB via `HasConversion<string>()`
- **Auth/Security:** No auth on API. Manual CSV-import only (no Plaid/bank linking).
- **Entity config:** Fluent API in `AppDbContext.OnModelCreating` (no data annotations)

---

## Architecture

- **Stack:** ASP.NET Core 9.0 Blazor Server + MudBlazor + PostgreSQL (Npgsql)
- **Pattern:** Controllers → Services → EF Core DbContext (thin controllers, logic in services)
- **DI:** Services registered as Scoped (except EncryptionService which is Singleton in V1)
- **Auto-migration:** Runs on startup with retry logic (`db.Database.Migrate()`)
- **Sign convention:** Positive Amount = expense/outflow, Negative Amount = income/inflow

---

## Testing

- **Framework:** MSTest with in-memory EF Core (`Microsoft.EntityFrameworkCore.InMemory`)
- **Naming:** `MethodName_Scenario_ExpectedBehavior` (e.g., `GetMonthly_GroupsByMonth_AndSumsCorrectly`)
- **Test DBs:** Use `nameof(MethodName)` for unique database names
- **No mocking library:** Services instantiated directly with in-memory DB
- **V1 UI tests:** Playwright, require full Docker stack on port 8080
  - Install browsers: `pwsh CashOut.Tests/bin/Release/net9.0/playwright.ps1 install --with-deps chromium`

---

## CI

CI (`.github/workflows/ci.yml`) covers **V1 only**:
1. `dotnet build --configuration Release`
2. `dotnet test --filter "TestCategory!=UI"` (unit tests)
3. Docker build + Playwright UI tests (requires full stack)

---

## Gotchas

- The V2 `AppDbContextFactory` loads `../.env` — always run `dotnet ef` from the **repo root**, not from inside `CashOutV2/CashOut/`
- V2 has no Migrations directory yet — first migration will create it
- The root `CashOut.sln` does **not** include V2 projects — build V2 by pointing at the csproj directly
- V1 Dockerfile builds from the repo root context with `CashOut/Dockerfile` — not from inside CashOut/
