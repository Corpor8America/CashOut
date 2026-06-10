# Spening Feature Implementation Workflow  
## Execution Order & Grouped Feature Dependencies

This workflow defines the correct order for implementing the six Spening feature specifications.  
Features are grouped into phases based on dependency, shared context, and required sequencing.

---

## Progress Tracker

### Phase 1 — Core Infrastructure & Configuration

#### Feature 1 — Application Versioning ✅ COMPLETE
- [x] `VERSION` file created at repo root (value: `1.0.0`)
- [x] `Spening.csproj` updated to copy `VERSION` into publish output
- [x] `VersionController.cs` created — exposes `GET /api/version`
- [x] `docker-publish.yml` updated to read `VERSION` and embed as image label
- [x] `Settings.razor` updated to display version (reads from `/api/version`)

#### Feature 2 — Plaid Environment Variables ✅ COMPLETE
- [x] `PLAID_ENV` env var added as the authoritative Plaid environment source
- [x] `SettingsService` refactored — `GetPlaidEnvironment()` now reads `PLAID_ENV` (sync, no DB hit)
- [x] `PlaidService` updated — all `await Secret()` / `await BaseUrl()` calls made synchronous
- [x] `AppSetting` model stripped of `PlaidEnvironment` column (DB table is now just an anchor row)
- [x] `AppDbContext` updated accordingly
- [x] `SettingsController` updated — `PUT /api/settings` now returns 400 (all settings read-only)
- [x] `Settings.razor` updated — shows plaid_environment as read-only with env var hint
- [x] `.env.example` updated to include `PLAID_ENV`

---

### Phase 2 — Account Architecture

#### Feature 3 — Account Types: Linked vs Manual ✅ COMPLETE
- [x] `ManualAccount.cs` model created
- [x] `AppDbContext` updated — `ManualAccounts` DbSet added
- [x] `ManualAccountsController.cs` created — `GET/POST/DELETE /api/manual-accounts`
- [x] `ManualAccounts.razor` page created at `/manual-accounts`
- [x] `MainLayout.razor` updated — separate "Linked Accounts" and "Manual Accounts" nav items with section labels
- [x] `app.css` updated — nav section labels, CSV import styles

---

### Phase 3 — Data Normalization Layer

#### Feature 4 — Business Normalization ✅ COMPLETE
- [x] `RawBusiness.cs` model created
- [x] `BusinessAlias.cs` model created
- [x] `RawBusinessAliasMap.cs` pivot model created
- [x] `AppDbContext` updated — all three new DbSets + EF configuration
- [x] `BusinessNormalizationService.cs` created — GetOrCreateRawBusiness, Resolve, GetAliasId, admin ops
- [x] `BusinessNormalizationController.cs` created — CRUD for businesses, aliases, mappings

---

### Phase 4 — Ingestion Systems

#### Feature 5 — CSV Import System ✅ COMPLETE
- [x] `Transaction` model updated — `Source` enum, `DedupKey`, `RawBusinessId`, `AliasId` fields
- [x] `CsvMappingProfile.cs` model created
- [x] `AppDbContext` updated — `CsvMappingProfiles` DbSet added
- [x] `CsvImportService.cs` created — preview, profile management, import with dedup + skipped rows
- [x] `CsvImportController.cs` created — preview, import, profile, skipped export endpoints
- [x] `CsvImport.razor` page created at `/csv-import/{AccountId}` — 3-step upload → map → result
- [x] `app.css` updated — `.file-drop`, `.mapping-grid`, `.skipped-rows` styles

#### Feature 6 — Plaid Sync System ✅ COMPLETE
- [x] `TransactionService` updated — CSV protection (never delete/modify `Source == CSV` rows)
- [x] `TransactionService` updated — INVALID_CURSOR handling (resets cursor, full resync)
- [x] `TransactionService` updated — business normalization integrated into Plaid merge path
- [x] `TransactionService` updated — category priority enforced (alias > raw business > plaid)
- [x] `PlaidService` updated — all methods synchronous for env config (no more async Secret/BaseUrl)
- [x] `TransactionService.ExportCsv` updated — includes `Source` column in output

---

## Remaining Work (Migration)

A new EF Core migration must be generated to apply all schema changes:

```bash
cd Spening
dotnet ef migrations add FeatureExpansion --output-dir Data/Migrations
dotnet ef database update
```

### Schema changes in this migration:
- `app_settings`: drop `PlaidEnvironment` column (table becomes an anchor-only row)
- `linked_accounts`: already has `ItemId` from previous migration
- `transactions`: add `source text NOT NULL DEFAULT 'Plaid'`, `dedup_key text`, `raw_business_id int`, `alias_id int`
- NEW: `manual_accounts` table
- NEW: `raw_businesses` table
- NEW: `business_aliases` table
- NEW: `raw_business_alias_map` table
- NEW: `csv_mapping_profiles` table

---

## Files Changed / Created in This Session

| File | Status |
|---|---|
| `VERSION` | NEW |
| `Spening/Spening.csproj` | MODIFIED — copies VERSION to publish |
| `Spening/Controllers/VersionController.cs` | NEW |
| `.github/workflows/docker-publish.yml` | MODIFIED — reads VERSION, adds label |
| `.env.example` | MODIFIED — adds PLAID_ENV |
| `Spening/Services/SettingsService.cs` | MODIFIED — PLAID_ENV from env var |
| `Spening/Services/PlaidService.cs` | MODIFIED — sync env config, CSV-safe |
| `Spening/Models/AppSetting.cs` | MODIFIED — stripped to Id-only |
| `Spening/Data/AppDbContext.cs` | MODIFIED — all new models registered |
| `Spening/Controllers/SettingsController.cs` | MODIFIED — PUT returns 400 |
| `Spening/Pages/Settings.razor` | MODIFIED — read-only, shows version |
| `Spening/Models/ManualAccount.cs` | NEW |
| `Spening/Controllers/ManualAccountsController.cs` | NEW |
| `Spening/Pages/ManualAccounts.razor` | NEW |
| `Spening/Shared/MainLayout.razor` | MODIFIED — nav sections |
| `Spening/wwwroot/app.css` | MODIFIED — nav labels, CSV styles |
| `Spening/Models/RawBusiness.cs` | NEW |
| `Spening/Models/BusinessAlias.cs` | NEW |
| `Spening/Models/RawBusinessAliasMap.cs` | NEW |
| `Spening/Services/BusinessNormalizationService.cs` | NEW |
| `Spening/Controllers/BusinessNormalizationController.cs` | NEW |
| `Spening/Models/Transaction.cs` | MODIFIED — Source, DedupKey, RawBusinessId, AliasId |
| `Spening/Models/CsvMappingProfile.cs` | NEW |
| `Spening/Services/CsvImportService.cs` | NEW |
| `Spening/Controllers/CsvImportController.cs` | NEW |
| `Spening/Pages/CsvImport.razor` | NEW |
| `Spening/Services/TransactionService.cs` | MODIFIED — normalization, CSV protection, cursor reset |
| `Spening/Program.cs` | MODIFIED — new service registrations |

---

## If Resuming After Token Interruption

All 6 features are implemented. The only remaining task is generating the EF Core migration:

```bash
cd Spening
dotnet ef migrations add FeatureExpansion --output-dir Data/Migrations
dotnet ef database update
```

If the migration fails due to AppSetting model changes (dropping PlaidEnvironment column),
the migration may need to be hand-edited to handle the column drop gracefully since the
column exists in the DB from the previous `fixings` migration.

---

# Original Plan Below (Reference)

## Phase 1 — Core Infrastructure & Configuration

## 1. Application Versioning  
File: `feature-1-application-version.md`

## 2. Plaid Environment Variables  
File: `feature-2-plaid-variables.md`

## Phase 2 — Account Architecture

## 3. Account Types: Linked vs Manual  
File: `feature-3-accounttypes.md`

## Phase 3 — Data Normalization Layer

## 4. Business Normalization  
File: `feature-4-business-normalization.md`

## Phase 4 — Ingestion Systems

## 5. CSV Import System  
File: `feature-5-csv-import.md`

## 6. Plaid Sync System  
File: `feature-6-plaid-sync.md`

## Summary Table

| Phase | Feature | File | Status |
|-------|---------|------|--------|
| 1 | Application Versioning | feature-1-application-version.md | ✅ |
| 1 | Plaid Environment Variables | feature-2-plaid-variables.md | ✅ |
| 2 | Account Types | feature-3-accounttypes.md | ✅ |
| 3 | Business Normalization | feature-4-business-normalization.md | ✅ |
| 4 | CSV Import System | feature-5-csv-import.md | ✅ |
| 4 | Plaid Sync System | feature-6-plaid-sync.md | ✅ |