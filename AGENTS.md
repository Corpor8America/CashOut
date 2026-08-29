# Agent Instructions

## Project

Single ASP.NET Core 9.0 Blazor Server app (formerly V2, now the sole application).

| | CashOut |
|---|---|
| Scope | CSV + PDF import, Accounts, Transactions, Categories + rules, 4 report types |
| DB entities | 5 DbSets (Account, Transaction, CsvMappingProfile, Category, CategoryRule) |
| Tests | MSTest unit tests (in-memory EF) + Playwright UI tests (Testcontainers Postgres) |
| Docker | dev compose runs db + pgadmin + app; `up db -d` for Postgres alone |
| VERSION | `1.0.1` (file `VERSION` at repo root, served via `api/version`) |

---

## Build & Test Commands

```bash
dotnet build CashOut/CashOut.csproj            # build
dotnet build CashOut.sln                       # full solution (what CI does)

# Unit tests (in-memory EF, no Docker) — MUST filter out UI tests:
dotnet test CashOut.Tests/CashOut.Tests.csproj --filter "TestCategory!=UI"

# Playwright UI tests — needs Docker + installed Playwright browsers:
pwsh CashOut.Tests/bin/Debug/net9.0/playwright.ps1 install chromium   # once (script generated on first build)
dotnet test CashOut.Tests/CashOut.Tests.csproj --filter "TestCategory=UI"

# Docker
docker-compose -f docker-compose.dev.yml up db -d        # Postgres only
docker-compose -f docker-compose.dev.yml up -d --build   # full dev stack (app in Docker)
```

---

## EF Core Migrations

When modifying entity models, DbContext configuration, or adding/changing/removing properties on entities, you MUST generate an EF Core migration after the code changes.

### Triggers

- Adding, removing, or renaming properties on entity classes in `Models/`
- Changing `HasDefaultValueSql`, `HasConversion`, or other fluent configuration in `Data/AppDbContext.cs`
- Adding new entity classes or `DbSet<T>` properties

### Steps

1. Make the code changes to models and `AppDbContext`
2. Ensure the dev database is running:
   ```bash
   docker-compose -f docker-compose.dev.yml up db -d
   ```
3. Generate the migration (from the **repo root**):
   ```bash
   dotnet ef migrations add <DescriptiveMigrationName> --project CashOut
   ```
4. Verify the build compiles:
   ```bash
   dotnet build CashOut/CashOut.csproj
   ```

The app auto-applies migrations on startup (`Program.cs` retries up to 10x), so no manual `database update` step.

### Connection

The design-time factory (`Data/AppDbContextFactory.cs`) reads `ConnectionStrings__Default` from the `.env` file one directory up from the project (`../.env`).

```
ConnectionStrings__Default=Host=localhost;Database=cashout;Username=cashout;Password=<DB_PASSWORD>
```

This is only needed for local `dotnet ef` CLI commands. The app gets its connection string from Docker environment variables at runtime.

### Naming conventions

Use descriptive PascalCase names: `AddUpdatedAtField`, `RenameCategoryColumn`, `AddTransactionIndex`, etc.

---

## Project Structure

| Directory | Purpose |
|---|---|
| `CashOut/Controllers/` | REST API endpoints (`/api/*`, kebab-case routes) |
| `CashOut/Services/` | Business logic layer |
| `CashOut/Models/` | EF Core entities |
| `CashOut/Data/` | `AppDbContext`, design-time factory |
| `CashOut/Migrations/` | EF Core migrations |
| `CashOut/Pages/` | Blazor Server UI pages |
| `CashOut/Shared/` | Layout + `ReportShell` |
| `CashOut.Tests/` | MSTest unit tests |
| `CashOut.Tests/UiTests/` | Playwright UI tests (`[TestCategory("UI")]`) |
| `docs/` | Design docs (incl. effective-categories design) |

---

## Code Conventions

- **Namespaces:** App code uses the global namespace (none). Tests use file-scoped namespaces (`CashOut.Tests`, `CashOut.Tests.UiTests`, `CashOut.Tests.UiTests.Helpers`)
- **Nullable:** Enabled project-wide. Use `string?` for optional fields, `int?` for optional FKs
- **Strings:** Always initialized to `""`, never null
- **Private fields:** `_camelCase` prefix (`_db`)
- **DB tables:** snake_case (`accounts`, `transactions`, `category_rules`)
- **Controller routes:** kebab-case (`api/csv-import`)
- **Records:** Used for DTOs and request types
- **Enums:** Stored as strings in DB via `HasConversion<string>()`
- **Auth/Security:** No auth on API. Import is manual CSV/PDF only (no bank linking).
- **Entity config:** Fluent API in `AppDbContext.OnModelCreating` (no data annotations)

---

## Architecture

- **Stack:** ASP.NET Core 9.0 Blazor Server + MudBlazor + PostgreSQL (Npgsql)
- **Pattern:** Controllers → Services → EF Core DbContext (thin controllers, logic in services)
- **DI:** Services registered as Scoped in `Program.cs`
- **Auto-migration:** Runs on startup with retry logic (`db.Database.Migrate()`)

### Money signs (non-obvious, get this right)

- Transactions store **separate `Credit` and `Debit` columns** (exactly one non-null). `Amount => (Credit ?? 0) - (Debit ?? 0)`.
- **Credit = money in (income), Debit = money out (expense).** So `Amount > 0` = income, `Amount < 0` = expense.
- Reports count expenses via `Debit != null` and sum `Math.Abs(t.Amount)`; income via `Credit != null`/`Amount > 0`.
- CSV import sign translation is **profile-driven** via `NegativeIsCredit` (default `true`) — never assume a fixed CSV sign convention when adding import/parse logic.
- `Transaction.NormalizeSingleAmount`/`NormalizeSplitColumns` are the single source of truth for mapping CSV column values to Credit/Debit.

### Two category systems ("effective categories")

There are two parallel category concepts — do not conflate them:

- `Transaction.Category` — legacy **string** written from CSV/PDF import; used by the "By Category" report.
- `Transaction.CategoryId`/`CategoryRuleId` — newer **FK** to `categories`/`category_rules` ("effective category"); used by "By Effective Category" report, the transactions page, and rule matching.

Rules match substring on `Transaction.Name` (case-insensitive); longest pattern wins, ties by lowest rule Id. Manual assignment always beats rules; reprocessing only touches transactions with `CategoryId == null`. Docs: `docs/effective-categories.md`.

---

## Testing

- **Unit tests:** MSTest + in-memory EF (`TestHelper.CreateInMemoryDb`), unique DB name via `nameof(MethodName)`. Services instantiated directly, no mocking library. Naming: `MethodName_Scenario_ExpectedBehavior`.
- **UI tests:** separate `CashOut.Tests/UiTests/` classes extending `PageTest`, tagged `[TestCategory("UI")]`. They boot an in-process app host (`CashOutAppFactory`) against a **real Postgres in a Testcontainers container** — Docker must be running, and Playwright Chromium must be installed. They are slow and are excluded from the default unit-test run by CI-style filters.

---

## CI

`.github/workflows/ci.yml` — two jobs, both on the solution:

1. **test** — `dotnet build CashOut.sln --configuration Release` then `dotnet test CashOut.sln --configuration Release --filter "TestCategory!=UI"`
2. **ui-test** — builds, installs Playwright browsers (`pwsh CashOut.Tests/bin/Release/net9.0/playwright.ps1 install --with-deps chromium`), runs `dotnet test CashOut.Tests/CashOut.Tests.csproj --filter "TestCategory=UI"`

Other workflows: `codeql.yml`, `docker-publish.yml`, `dependabot.yml`.

---

## Gotchas

- `AppDbContextFactory` loads `../.env` — always run `dotnet ef` from the **repo root**, not from inside `CashOut/`
- Version lives in the `VERSION` file (repo root), copied into the build output and read by `VersionController` — bump it there, not in the csproj
- Report "current year" defaults come from `SettingsService.GetOutputYear()` = **latest transaction year in DB** (falls back to `DateTime.UtcNow.Year` when empty), not the real current year
- Culture is pinned to `en-US` in `Program.cs` (Blazor + server default culture)