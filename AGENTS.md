# Agent Instructions

## Project

Single ASP.NET Core 9.0 Blazor Server app (formerly V2, now the sole application).

| | CashOut |
|---|---|
| Scope | CSV-only import, single Account entity, 3 report types |
| DB entities | 3 DbSets (Account, Transaction, CsvMappingProfile) |
| Tests | Unit tests (MSTest) + Playwright integration tests |
| Docker | DB + pgadmin only |
| VERSION | `1.0.0-beta.001` |

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

### Connection

The design-time factory (`Data/AppDbContextFactory.cs`) reads `ConnectionStrings__Default` from the `.env` file one directory up from the project (`../.env`).

```
ConnectionStrings__Default=Host=localhost;Database=cashout;Username=cashout;Password=<DB_PASSWORD>
```

This is only needed for local `dotnet ef` CLI commands. The app gets its connection string from Docker environment variables at runtime.

### Naming conventions

Use descriptive PascalCase names: `AddUpdatedAtField`, `RenameCategoryColumn`, `AddTransactionIndex`, etc.

---

## Build & Test Commands

```bash
dotnet build CashOut/CashOut.csproj                      # build
dotnet test CashOut.Tests/CashOut.Tests.csproj            # unit tests
dotnet test CashOut.Tests/CashOut.Tests.csproj --filter "TestCategory=UI"  # Playwright UI tests

# Docker
docker-compose -f docker-compose.dev.yml up db -d         # Postgres only
docker-compose -f docker-compose.dev.yml up -d --build    # full dev stack
```

---

## Project Structure

| Directory | Purpose |
|---|---|
| `CashOut/Controllers/` | REST API endpoints (`/api/*`) |
| `CashOut/Services/` | Business logic layer |
| `CashOut/Models/` | EF Core entities |
| `CashOut/Data/` | `AppDbContext`, design-time factory |
| `CashOut/Pages/` | Blazor Server UI pages |
| `CashOut/Shared/` | Layout components |
| `CashOut.Tests/` | MSTest unit tests |
| `docs/` | Design docs |

---

## Code Conventions

- **Namespaces:** No namespaces (global namespace). Tests use file-scoped `namespace CashOut.Tests;`
- **Nullable:** Enabled project-wide. Use `string?` for optional fields, `int?` for optional FKs
- **Strings:** Always initialized to `""`, never null
- **Private fields:** `_camelCase` prefix (`_db`)
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
- **DI:** Services registered as Scoped
- **Auto-migration:** Runs on startup with retry logic (`db.Database.Migrate()`)
- **Sign convention:** Positive Amount = expense/outflow, Negative Amount = income/inflow

---

## Testing

- **Framework:** MSTest with in-memory EF Core (`Microsoft.EntityFrameworkCore.InMemory`)
- **Naming:** `MethodName_Scenario_ExpectedBehavior` (e.g., `GetMonthly_GroupsByMonth_AndSumsCorrectly`)
- **Test DBs:** Use `nameof(MethodName)` for unique database names
- **No mocking library:** Services instantiated directly with in-memory DB

---

## CI

CI (`.github/workflows/ci.yml`):
1. `dotnet build CashOut/CashOut.csproj --configuration Release`
2. `dotnet test CashOut.Tests/CashOut.Tests.csproj --configuration Release`

---

## Gotchas

- The `AppDbContextFactory` loads `../.env` — always run `dotnet ef` from the **repo root**, not from inside `CashOut/`
