# CashOut Code Review

**Generated:** 2026-06-22
**Scope:** Full solution (CashOut + CashOut.Tests)

---

## Overview

CashOut is a well-structured Blazor Server personal finance application with a clean service-layer architecture, REST API controllers, and comprehensive test coverage. The codebase demonstrates consistent conventions, good security practices, and thoughtful design decisions. Below are findings organized by severity.

---

## Architecture & Design

### Positive
- Clean separation: Controllers → Services → EF Core DbContext
- Typed `HttpClient` via `IHttpClientFactory` for Plaid API (proper socket pooling)
- AES-256-GCM encryption for sensitive tokens at rest
- In-memory database for unit tests — fast and isolated
- Record types for DTOs reduce boilerplate
- Batch normalization (`ResolveBulk`) avoids N+1 queries during imports

### Concerns

| # | Issue | Location | Severity |
|---|-------|----------|----------|
| 1 | **Dead code: `BusinessNormalizationService` is never injected** into any controller or consumer. `MerchantNormalizationService` handles all the same functionality. Should be removed or marked obsolete. | `CashOut/Services/BusinessNormalizationService.cs` | Medium |
| 2 | **`RawBusiness` has `UpdatedAt` but `BusinessAlias` does not.** Inconsistent audit trail. | `CashOut/Models/BusinessAlias.cs:19`, `CashOut/Models/RawBusiness.cs:30` | Low |
| 3 | **`CsvMappingProfile` has no `UpdatedAt` timestamp** while other entities do. | `CashOut/Models/CsvMappingProfile.cs:48` | Low |

---

## Security

### Positive
- Plaid access tokens encrypted with AES-256-GCM using random nonces (semantic security)
- `AccessToken` and `ItemId` excluded from account list API responses
- Debug controller gated to Development environment only (Program.cs:78-90)
- No secrets in source code — all via environment variables

### Concerns

| # | Issue | Location | Severity |
|---|-------|----------|----------|
| 4 | **DB default `now()` is not timezone-aware.** `HasDefaultValueSql("now()")` uses the PostgreSQL transaction timestamp in the server's local timezone, while application code consistently uses `DateTime.UtcNow`. This creates a latent inconsistency: when services explicitly set `CreatedAt`, it's UTC, but when the DB default applies, it could be local time. Use `now() at time zone 'utc'` or remove DB defaults and always set from application code. | `CashOut/Data/AppDbContext.cs` (multiple entities) | Medium |
| 5 | **`RemoveItem` fallback uses encrypted token lookup** when `ItemId` is empty. Since encrypted tokens differ per encryption call (random nonce), this lookup will never match after re-encryption. The `ItemId` path should always be used. | `CashOut/Services/PlaidService.cs:197-199` | Medium |

---

## Reliability & Correctness

| # | Issue | Location | Severity |
|---|-------|----------|----------|
| 6 | **`ReportService.GetByCategory` trailing 12-month window is just the current year.** The comment acknowledges this (`// Trailing 12-month window is the full selected year`) but the metric labeled "12 month average" is actually the per-year average, not a true rolling 12-month window from the latest transaction. | `CashOut/Services/ReportService.cs:87-88` | Low |
| 7 | **`DeleteAlias` calls `SaveChangesAsync` 4 times** (lines 308, 321, 323, plus inside `ReprocessUnaliasedTransactions`). This is inefficient and risks partial saves on failure. Consolidate into fewer save points. | `CashOut/Services/MerchantNormalizationService.cs:278-325` | Low |
| 8 | **`CreateAlias` calls `RetroactivelyMap` immediately after creation.** The comment on line 193-194 says "A new alias has no patterns yet so RetroactivelyMap will match nothing" but still executes it — a wasted DB round-trip on every alias creation. | `CashOut/Services/MerchantNormalizationService.cs:194` | Low |
| 9 | **CSV escaping inconsistency:** `Esc()` in `ReportService` doesn't handle newlines embedded in CSV fields, while `EscapeCsv()` in `TransactionService` does. | `CashOut/Services/ReportService.cs:1082-1083` vs `CashOut/Services/TransactionService.cs:261-264` | Low |
| 10 | **`ResolveBulk` sets `RawBusiness.RawName = rawName` and `RawNameNormalized = normalized`** but doesn't check for an existing `RawBusiness` entry first (unlike `Resolve` → `EnsureRawBusiness`). Instead it relies on the pre-loaded `rawByNormalized` dictionary, meaning items not pre-loaded will be duplicated. Callers do pre-load correctly, but the contract is fragile. | `CashOut/Services/MerchantNormalizationService.cs:87-116` | Low |

---

## Performance

| # | Issue | Location | Severity |
|---|-------|----------|----------|
| 11 | **`MatchAlias` loads ALL patterns into memory on every call.** For the single-resolve path, this is O(n) memory per call. The `ResolveBulk` path correctly pre-loads patterns once. Consider caching patterns in a scoped cache for the single-resolve path. | `CashOut/Services/MerchantNormalizationService.cs:36-40` | Low |
| 12 | **`GetExpenses` and `GetIncomeTransactions` load all matching transactions into memory** via `ToListAsync()`, then do grouping and aggregation in application memory. For large datasets, pushing aggregation to the database via EF would be more efficient. | `CashOut/Services/ReportService.cs:29-57` | Low |

---

## Testing

### Positive
- 70+ tests across 5 test files — excellent coverage
- In-memory EF Core database for isolation
- Good edge case coverage: empty strings, tampered payloads, zero amounts, Unicode
- `ReportServiceTests` thoroughly validates all report types, including excluded categories

### Concerns

| # | Issue | Location | Severity |
|---|-------|----------|----------|
| 13 | **`CsvImportServiceTests` has only 1 test.** The `CsvImportService` has a complex pipeline (parsing, validation, normalization, dedup) with minimal coverage. | `CashOut.Tests/CsvImportServiceTests.cs` | Medium |
| 14 | **`SettingsServiceTests.GetPlaidEnvironment_DefaultsToSandbox_WhenMissing`** uses `return Task.CompletedTask` instead of being `async Task`. Works but inconsistent with the rest of the test suite. | `CashOut.Tests/SettingsServiceTests.cs:40-49` | Low |
| 15 | **`UiTests.cs` hardcodes `http://localhost:8080`.** Should be configurable via environment variable or test setting. | `CashOut.Tests/UiTests.cs:16,26` | Low |
| 16 | **No tests for `TransactionsController`, `PlaidService`, or `BusinessNormalizationController`.** These contain critical business logic with no automated coverage. | N/A | Medium |

---

## Code Style & Conventions

### Positive
- Consistent file-scoped namespaces throughout
- Nullable reference types enabled and used correctly
- `///` XML doc comments on all public methods
- Clear inline documentation with ASCII-art section headers
- Consistent naming conventions (PascalCase, `_camelCase` for fields)

### Minor Issues

| # | Issue | Location |
|---|-------|----------|
| 17 | `Program.cs:29` — `Microsoft.AspNetCore.Http.Features.FormOptions` is fully qualified but `MudBlazor.Services` is imported for `AddMudServices()`. The FormOptions using could be added at the top. |
| 18 | `TransactionService.Query` (line 192-193) has inconsistent indentation (spaces on line 192, then indented on 193+). |
| 19 | Several record declarations use single-line formatting for very long parameter lists (e.g., `ExecutiveMonthlyOverview` with 15 parameters on one line) which hurts readability. |

---

## Summary

**Strengths:**
- Well-architected with clear separation of concerns
- Strong security posture (encryption, no leaked secrets)
- Excellent test coverage for core business logic (ReportService, MerchantNormalizationService, EncryptionService)
- Consistent conventions and thorough documentation
- Thoughtful batch-processing optimizations in import/merge paths

**Key recommendations (ordered by impact):**
1. Remove dead code (`BusinessNormalizationService`)
2. Fix timezone inconsistency in DB defaults (use UTC consistently)
3. Add tests for `CsvImportService` and `PlaidService`/controllers
4. Fix `PlaidService.RemoveItem` fallback path (encrypted token lookup)
5. Consolidate `DeleteAlias` save points into fewer round-trips
6. Remove unnecessary `RetroactivelyMap` call in `CreateAlias`
7. Make UI test URL configurable
