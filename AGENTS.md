# Agent Instructions

## EF Core Migrations

When modifying entity models, DbContext configuration, or adding/changing/removing properties on entities, you MUST generate an EF Core migration after the code changes.

### Triggers

- Adding, removing, or renaming properties on entity classes in `CashOut/Models/`
- Changing `HasDefaultValueSql`, `HasConversion`, or other fluent configuration in `CashOut/Data/AppDbContext.cs`
- Adding new entity classes or `DbSet<T>` properties

### Steps

1. Make the code changes to models and `AppDbContext`
2. Ensure the dev database is running:
   ```bash
   docker-compose -f docker-compose.dev.yml up db -d
   ```
3. Generate the migration from the `CashOut` directory:
   ```bash
   dotnet ef migrations add <DescriptiveMigrationName> --project CashOut
   ```
4. Verify the build compiles:
   ```bash
   dotnet build CashOut/CashOut.csproj
   ```

### Connection

The design-time factory (`CashOut/Data/AppDbContextFactory.cs`) reads `ConnectionStrings__Default` from the `.env` file at the project root. The `.env` file must contain:

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
| `CashOut/Controllers/` | REST API endpoints under `/api/*` |
| `CashOut/Services/` | Business logic layer (all services) |
| `CashOut/Models/` | EF Core entity classes and DTOs |
| `CashOut/Data/` | `AppDbContext`, design-time factory, migrations |
| `CashOut/Pages/` | Blazor Server UI pages |
| `CashOut/Shared/` | Layout components (`MainLayout`, `ReportShell`) |
| `CashOut.Tests/` | MSTest unit tests and Playwright UI tests |
| `docs/` | Design specs and feature documentation |

---

## Build & Test Commands

```bash
# Build
dotnet build

# Run unit tests (excludes Playwright UI tests)
dotnet test --filter "TestCategory!=UI"

# Run UI tests (requires Docker stack running)
dotnet test --filter "TestCategory=UI"

# Run all tests
dotnet test

# EF Core migration
dotnet ef migrations add <Name> --project CashOut

# Docker dev (PostgreSQL only)
docker-compose -f docker-compose.dev.yml up db -d

# Docker dev (full stack)
docker-compose -f docker-compose.dev.yml up -d --build
```

---

## Code Conventions

- **Namespaces:** No namespaces in main project (global namespace). File-scoped namespaces in tests (`namespace CashOut.Tests;`)
- **Nullable:** Enabled project-wide. Use `string?` for optional fields, `int?` for optional FKs
- **Strings:** Always initialized to `""`, never null
- **Private fields:** `_camelCase` prefix (`_db`, `_plaid`)
- **DB tables:** snake_case (`linked_accounts`, `transactions`)
- **Controller routes:** kebab-case (`api/csv-import`, `api/normalization`)
- **Records:** Used for DTOs and request types
- **Enums:** Stored as strings in DB via `HasConversion<string>()`
- **Auth/Security:** No auth on API. Plaid tokens encrypted with AES-256-GCM. Debug controller gated to Development environment only.

---

## Architecture

- **Stack:** ASP.NET Core 9.0 Blazor Server + MudBlazor + PostgreSQL (Npgsql)
- **Pattern:** Controllers → Services → EF Core DbContext (thin controllers, logic in services)
- **DI:** Services registered as Scoped, `EncryptionService` as Singleton, `PlaidService` via `AddHttpClient<PlaidService>` (typed HTTP client)
- **Data:** All entity config via Fluent API in `AppDbContext.OnModelCreating` (no data annotations)
- **Auto-migration:** Runs on startup (`db.Database.Migrate()`)
- **Sign convention:** Positive Amount = expense/outflow, Negative Amount = income/inflow

---

## Testing

- **Framework:** MSTest with in-memory EF Core (`Microsoft.EntityFrameworkCore.InMemory`)
- **Naming:** `MethodName_Scenario_ExpectedBehavior` (e.g., `GetMonthly_GroupsByMonth_AndSumsCorrectly`)
- **Test DBs:** Use `nameof(MethodName)` for unique database names
- **UI tests:** Playwright, require full Docker stack on port 8080
- **No mocking library:** Services instantiated directly with in-memory DB
