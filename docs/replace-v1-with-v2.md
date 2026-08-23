# Proposal: Replace V1 with V2

## Goal

Delete the entire V1 codebase (`CashOut/` directory) and replace it with the V2 app (`CashOutV2/` directory), making V2 the sole application in this repo.

## Current State

| | V1 (`CashOut/`) | V2 (`CashOutV2/`) |
|---|---|---|
| Scope | Full: Plaid, merchant normalization, encryption, accounts, PDF import, 7+ report types | Simplified: CSV-only import, single Account entity, 3 report types |
| DB Entities | 9 DbSets | 3 DbSets (Account, Transaction, CsvMappingProfile) |
| Services | 9 services | 5 services |
| Controllers | 11 controllers | 6 controllers |
| Pages | 14+ Razor pages | 7 Razor pages |
| Tests | ~70+ unit tests + Playwright UI tests | ~61 unit tests |
| Migrations | 11 migrations | 9 migrations |
| Docker | Full stack with Plaid env vars | DB + pgadmin only |
| CI | Build + unit tests + Playwright UI tests | Not wired into CI |

## What Gets Deleted

Everything in `CashOut/` that V2 does not have:

- **Plaid Integration**: `PlaidService.cs`, `PlaidLinkController.cs`, `plaidLink.js`, Plaid env vars in docker-compose
- **Encryption**: `EncryptionService.cs` (Singleton, AES-256-GCM for Plaid tokens)
- **Merchant Normalization**: `MerchantNormalizationService.cs`, `BusinessNormalizationController.cs`, `RawBusiness.cs`, `BusinessAlias.cs`, `AliasPattern.cs`, `RawBusinessAliasMap.cs`, `/merchants` page
- **Linked Accounts**: `LinkedAccount.cs` entity, Plaid-linked account flows
- **Account Reports**: `AccountReportService.cs`, `AccountReportsController.cs`, `/accounts/{id}` detail page
- **Executive Summary**: `ExecutiveSummary` report type
- **Income Report**: `IncomeReport` report type
- **Merchant Report**: `MerchantReport` report type
- **Pivot Table**: `PivotReport` report type
- **Excluded Categories**: `ExcludedCategories` system (UI + API + report filtering)
- **Debug API**: `DebugController.cs` (dev-only `/api/debug/env`)
- **Plaid JS**: `wwwroot/js/plaidLink.js`
- **Playwright Tests**: `UiTests.cs`
- **Root Solution**: `CashOut.sln` (not needed for single project)

## What Survives (from V2)

The entire `CashOutV2/CashOut/` directory moves to `CashOut/`:

- **Models**: Account, Transaction, CsvMappingProfile, ReportDtos
- **Services**: CsvImportService, PdfImportService, TransactionService, ReportService, SettingsService
- **Controllers**: AccountsController, TransactionsController, CsvImportController, ReportsController, SettingsController, VersionController
- **Pages**: Index, Accounts, Transactions, CsvImport, Settings, ReportCategory, ReportCashFlow
- **Shared**: MainLayout, ReportShell, App
- **Data**: AppDbContext, AppDbContextFactory
- **Helpers**: DateHelper
- **Migrations**: All 9 V2 migrations
- **Tests**: All V2 unit tests (no Playwright)

## Target Repo Structure

```
CashOut/                          # repo root
├── .env
├── .env.example
├── VERSION
├── AGENTS.md
├── docker-compose.dev.yml
├── CashOut/                      # app (was CashOutV2/CashOut/)
│   ├── CashOut.csproj
│   ├── Dockerfile
│   ├── Program.cs
│   ├── Data/
│   ├── Models/
│   ├── Services/
│   ├── Controllers/
│   ├── Pages/
│   ├── Shared/
│   ├── Helpers/
│   ├── wwwroot/
│   └── Migrations/
├── CashOut.Tests/                # tests (was CashOutV2/CashOut.Tests/)
│   ├── CashOut.Tests.csproj
│   ├── TestHelper.cs
│   ├── CsvImportServiceTests.cs
│   ├── TransactionServiceTests.cs
│   ├── ReportServiceTests.cs
│   └── PdfImportServiceTests.cs
├── docs/
│   ├── effective-categories.md
│   └── replace-v1-with-v2.md
├── .github/
│   └── workflows/ci.yml
├── .gitignore
├── .dockerignore
├── LICENSE
└── README.md
```

## Migration Strategy

### Step 1: Tag Current State

```bash
git tag pre-v2-cutover
```

### Step 2: Delete V1 Code

```bash
rm -rf CashOut/           # V1 app
rm -rf CashOut.Tests/     # V1 tests (if at root)
rm -f CashOut.sln
rm -f docker-compose.dev.yml   # V1's docker-compose
```

### Step 3: Promote V2 to Root

```bash
mv CashOutV2/CashOut/ CashOut/
mv CashOutV2/CashOut.Tests/ CashOut.Tests/
mv CashOutV2/docker-compose.dev.yml .
mv CashOutV2/.env .
mv CashOutV2/.env.example .
mv CashOutV2/VERSION .
rm -rf CashOutV2/
```

### Step 4: Update Configuration Files

1. **AGENTS.md** — Remove all V1 references, update paths and commands
2. **.github/workflows/ci.yml** — Update to build/test V2 paths
3. **.gitignore** — Remove V1-specific entries if any
4. **.dockerignore** — Update if needed
5. **README.md** — Rewrite for V2

### Step 5: Update Dockerfile Path

The V2 Dockerfile build context is `CashOutV2/`. After moving to root, update `docker-compose.dev.yml`:

```yaml
app:
  build:
    context: .
    dockerfile: CashOut/Dockerfile
```

This is already the correct path since V2's docker-compose references `CashOut/Dockerfile`.

### Step 6: Verify

```bash
dotnet build CashOut/CashOut.csproj
dotnet test CashOut.Tests/CashOut.Tests.csproj
docker-compose -f docker-compose.dev.yml up -d --build
```

## Database Considerations

### If Starting Fresh (Recommended)

Run `docker-compose down -v` to wipe the volume, then let V2's auto-migration create the schema from scratch.

### If Migrating Production Data

1. Export V1 data: accounts (from `manual_accounts`), transactions, csv_mapping_profiles
2. Transform: rename `manual_accounts` to `accounts`, drop Plaid-specific columns
3. Import into V2 schema
4. Run V2 migrations

V1 to V2 transaction schema differences:
- V1 has `RawName`, `NormalizedName`, `AliasId`, `RawBusinessId` — all dropped in V2
- V1 `Source` enum has Plaid + CSV — V2 has CSV only
- V1 has stored `Amount` column — V2 computes it as `(Credit ?? 0) - (Debit ?? 0)`

## CI/CD Changes

Current V1 CI does:
1. `dotnet restore` / `dotnet build` / `dotnet test` (unit tests)
2. Docker build + Playwright UI tests

Updated V2 CI should:
1. `dotnet restore CashOut/CashOut.csproj`
2. `dotnet build CashOut/CashOut.csproj`
3. `dotnet test CashOut.Tests/CashOut.Tests.csproj` (unit tests only)
4. Optionally: Docker build + smoke test

No Playwright tests in V2, so the `api-test` job can be removed or replaced with a simple health check.

## Risks

| Risk | Mitigation |
|---|---|
| Losing V1 code | Git tag before changes; V1 stays in git history |
| Plaid users lose bank sync | Confirm no active Plaid usage before cutover |
| Database schema mismatch | Fresh DB with auto-migration, or data migration script |
| CI breaks | Update ci.yml before merging; test locally first |
| Docker build context | Verify Dockerfile paths after move |

## Rollback

If something goes wrong:

```bash
git reset --hard pre-v2-cutover
git tag -d pre-v2-cutover
```

All V1 code is restored exactly as it was.
