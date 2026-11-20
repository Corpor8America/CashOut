# Data Transfer (Full Backup / Restore) — Implementation Plan

**Branch:** `feature/data-export-import` (created from `master` at `7ac87ca`)
**Status:** Implemented — unit, UI and integration tests all passing
**Last updated:** 2026-11-19
**Version:** `VERSION` bumped `1.0.1` → `1.1.0` (new feature, minor bump)

This document is the source of truth for progress. Update the status checklist and the
progress log as work proceeds.

---

## 1. Goal

Let a user download **one file** from a CashOut instance that contains **all persisted
data**, then upload that file into another CashOut instance. Upload is a **destructive,
all-or-nothing replacement**: existing data is wiped and the backup is restored verbatim.

"All persisted data" is exactly the 5 `DbSet`s in `AppDbContext` — there is no
`AppSetting` table, and raw uploaded CSV/PDF files are not stored.

---

## 2. Decisions (confirmed with user)

| Decision | Choice |
|---|---|
| File format | **ZIP** containing `manifest.json` + `data.json` (JSON payload, human-readable inside the zip) |
| Scope | **All 5 tables**: `accounts`, `transactions`, `csv_mapping_profiles` (every version), `categories`, `category_rules` |
| Restore semantics | **Atomic full replace**: one DB transaction; delete all rows, reinsert verbatim (preserve IDs, dates, FKs); rollback on any failure |
| Surface | **New Blazor page + REST API** |
| Destructive confirmation | **Explicit UI checkbox** required before upload will POST; API itself stays directly callable |
| Validation | **Strict**: reject malformed zips/JSON, unknown or newer schema versions, duplicate PKs, broken real FK references — before touching the DB |
| Upload size | **Raise limits** to 100 MB (multipart limit, Kestrel request limit, UI file size) |

Implementation notes:

- The restore result type is `BackupImportResult` (the name `ImportResult` is already used by CSV import).
- EF created quoted PascalCase columns (`"Id"`), so the Postgres sequence-reset SQL quotes all identifiers.
- Validation errors are surfaced as a `DataTransferException`, which the controller maps to HTTP 400.

Non-decisions / deliberate exclusions:

- No changes to the EF model, so **no EF migration is required**.
- No re-running of `CategoryRuleService.ReprocessUncategorized()` on restore — the goal is
  byte-for-byte fidelity, not re-derivation. Users can reprocess manually on the Category Rules
  page if desired.
- No merge/upsert mode, no partial restore, no scheduling.
- `Transaction.AccountId` has **no DB foreign key**, so it is treated as an opaque string and
  is not FK-validated. Genuine FKs (`CategoryId`, `CategoryRuleId`, `CategoryRule.CategoryId`)
  *are* validated.
- Do not copy the existing `skipped-export` pattern (a POST endpoint used as a plain `Href` link).

---

## 3. Design

### 3.1 Backup file

Single `.zip`, filename `cashout-backup-{yyyyMMdd-HHmmss}.zip`, content type `application/zip`.

**`manifest.json`** (small, validated first):

```json
{
  "format": "cashout-backup",
  "schemaVersion": 1,
  "appVersion": "1.0.1",
  "exportedAtUtc": "2026-11-19T12:34:56Z",
  "counts": {
    "accounts": 3,
    "transactions": 1420,
    "csvMappingProfiles": 7,
    "categories": 12,
    "categoryRules": 30
  }
}
```

**`data.json`**:

```json
{
  "accounts": [ ... ],
  "transactions": [ ... ],
  "csvMappingProfiles": [ ... ],
  "categories": [ ... ],
  "categoryRules": [ ... ]
}
```

Serialization uses **explicit DTO records**, not EF entities: `Transaction.Amount` is a
computed, unmapped property and both navigation properties (`EffectiveCategory`,
`AssignedByRule`) would create cycles. Credit/Debit, `DateOnly`, `Guid`, and decimals are
serialized directly with `System.Text.Json` defaults (invariant, ISO-8601).

Entries are ordered by primary key on export for deterministic output.

### 3.2 API — `DataTransferController` (`api/data-transfer`)

- `GET /api/data-transfer/export` → `File(zipBytes, "application/zip", fileName)`
- `POST /api/data-transfer/import` — multipart form field `file` (`.zip`)
  → `Ok(ImportResult)` with restored row counts, or `BadRequest(...)` with a clear message.
  Decorate with `[RequestSizeLimit(100 * 1024 * 1024)]`.

### 3.3 Service — `DataTransferService` (Scoped)

`Services/DataTransferService.cs`, injected into the controller and registered in both
`Program.cs` and `CashOut.Tests/UiTests/Helpers/CashOutAppFactory.cs`.

- `Task<byte[]> ExportAsync()` — `AsNoTracking`, ordered loads of all 5 tables, build DTOs,
  write manifest/data into a `MemoryStream`-backed `ZipArchive`.
- `Task<ImportResult> ImportAsync(Stream zip)` —
  1. Open zip read-only; require both expected entries; reject unexpected traversal/absence.
  2. Parse & validate manifest: `format == "cashout-backup"`, `schemaVersion == 1`
     (reject newer with an "upgrade CashOut" message), counts are non-negative.
  3. Parse data JSON strictly (`UnmappedMemberHandling.Disallowed`, case-sensitive property
     names, no null required fields). Validate: no duplicate PKs; enum `Source` parses;
     `Category.Name` unique; every `CategoryRule.CategoryId` exists; every non-null
     `Transaction.CategoryId` / `CategoryRuleId` exists. Do **not** validate `AccountId`.
  4. Begin `IDbContextTransaction`; delete in FK-safe order: transactions → category_rules →
     categories → csv_mapping_profiles → accounts; insert in order: accounts → categories →
     category_rules → csv_mapping_profiles → transactions; single `SaveChangesAsync`; commit.
     Any exception rolls back, leaving the original data intact.
  5. Return counts.

**Identity sequences gotcha:** Categories, CategoryRules and CsvMappingProfiles use
`ValueGeneratedOnAdd`. Inserting explicit IDs leaves Postgres sequences behind, so the next
normal insert would collide. After restore (only when `Database.IsNpgsql()`), advance each
sequence with `setval(pg_get_serial_sequence(...), COALESCE(MAX(id), 0) + 1, false)`.
Skip this in in-memory unit tests.

Use tracked `RemoveRange(await db.<Set>.ToListAsync())` rather than `ExecuteDeleteAsync()`
so in-memory EF tests work.

### 3.4 UI — `Pages/DataTransfer.razor`

Route `/data-transfer`; nav link **"Backup & Restore"** with a suitable Material icon under the
existing **Data** section in `Shared/MainLayout.razor`.

- Card "Export": button `Download Backup (.zip)` → anchor `Href="api/data-transfer/export"`
  with a `download` attribute (the working download pattern).
- Card "Import": `<InputFile accept=".zip">`, show selected filename/size, and a required
  `MudCheckBox`: *"I understand that importing will permanently delete all current data and
  replace it with the contents of this file."* Import button disabled until a file is selected
  and the box is checked. On click, stream the file via `IBrowserFile.OpenReadStream(100 MB)`
  into `MultipartFormDataContent` and POST to `api/data-transfer/import`; show restored counts
  on success or the server error message on failure (`MudSnackbar` / `MudAlert`).
- Keep the existing page style (MudBlazor components, `@inject HttpClient`).

### 3.5 Config changes

- `Program.cs`: `FormOptions.MultipartBodyLengthLimit = 100 * 1024 * 1024`;
  `builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 100L * 1024 * 1024)`
  (Kestrel's default ~30 MB would otherwise reject the upload before MVC sees it);
  register `DataTransferService` as Scoped.
- `CashOutAppFactory.cs`: register `DataTransferService` as Scoped in the UI test host.

---

## 4. Implementation steps

- [x] **1.** Add DTO records (`Models/BackupDtos.cs`) + `DataTransferService` export/import logic.
- [x] **2.** Add `DataTransferController` with export/import endpoints and request-size limit.
- [x] **3.** Update `Program.cs` (limits + DI) and `CashOutAppFactory.cs` (limits + DI).
- [x] **4.** Add `Pages/DataTransfer.razor` and nav link in `Shared/MainLayout.razor`.
- [x] **5.** Add unit tests `CashOut.Tests/DataTransferServiceTests.cs`.
- [x] **6.** Add UI tests `CashOut.Tests/UiTests/DataTransferTests.cs`; update `NavigationTests.Sidebar_DisplaysAllNavLinks`.
- [x] **7.** Add integration tests `CashOut.Tests/IntegrationTests/DataTransferIntegrationTests.cs` (real Postgres + HTTP round-trip, tagged `UI` so they run in the Docker-enabled CI job).
- [x] **8.** Bump `VERSION` to `1.1.0`.
- [x] **9.** Build + run unit tests (110 passed); run UI/integration tests with Docker (37 passed).
- [x] **10.** End-to-end verification via the integration test: export → replace data → import → confirm identity sequences still work for new inserts.

---

## 5. Tests to write

**Unit (`DataTransferServiceTests`, in-memory EF, unique DB name per test):**

1. `ExportAsync_WithAllEntities_WritesManifestAndAllTables`
2. `RoundTrip_PreservesIdsDatesCreditDebitProfilesAndCategoryLinks`
3. `ImportAsync_WithExistingData_ReplacesAllRows`
4. `ImportAsync_WithEmptyBackup_ClearsDatabase`
5. `ImportAsync_WithInvalidZip_ThrowsAndLeavesDataUnchanged`
6. `ImportAsync_WithWrongSchemaVersion_ThrowsAndLeavesDataUnchanged`
7. `ImportAsync_WithDuplicateIdsOrBrokenCategoryFk_ThrowsAndLeavesDataUnchanged`
8. `ImportAsync_WithMalformedJson_ThrowsAndLeavesDataUnchanged`

**UI (`DataTransferTests`, `[TestCategory("UI")]`):**

1. Nav link visible and navigates to `/data-transfer`.
2. Export button produces a `.zip` download (Playwright download event).
3. Import button stays disabled until file selected **and** confirmation checked.
4. Uploading a valid backup into a fresh test instance restores accounts/transactions/categories
   (via the API helper, since the UI host uses a real Testcontainers Postgres — this also
   exercises the sequence-reset SQL).

**Commands:**

```bash
dotnet build CashOut.sln
dotnet test CashOut.Tests/CashOut.Tests.csproj --filter "TestCategory!=UI"
docker-compose -f docker-compose.dev.yml up db -d
dotnet test CashOut.Tests/CashOut.Tests.csproj --filter "TestCategory=UI"
```

---

## 6. Risks / gotchas

- Postgres identity sequences after explicit-ID inserts (see §3.3) — must verify with a real DB.
- Never serialize `Transaction.Amount` or the EF navigations.
- Cascade delete from `CategoryRule → Category`; delete rules first.
- `Category.Name` has a unique index — duplicates in a hand-edited backup must be rejected.
- `AccountId` is opaque (no FK); do not "helpfully" validate or regenerate it.
- 100 MB limit must be raised in **three** places (FormOptions, Kestrel, browser file input);
  a reverse proxy may add its own limit.
- Zip entries are never extracted to disk, so path traversal is not applicable; still require
  exact entry names.
- No model changes → no EF migration needed (per `AGENTS.md`).
- New nav link breaks `NavigationTests.Sidebar_DisplaysAllNavLinks` until updated.

---

## 7. Progress log

| Date | Status | Notes |
|---|---|---|
| 2026-11-19 | Planning | Branch `feature/data-export-import` created from `master`; requirements clarified with user; this plan written. Implementation not started. |
| 2026-11-19 | Implemented | Added DTOs, service, controller, page, nav link, DI/limits, version bump, and unit/UI/integration tests. |
| 2026-11-19 | Verified | `dotnet build CashOut.sln` clean; unit tests 110/110 passed; UI + integration tests 37/37 passed against Testcontainers Postgres and Playwright Chromium. |
