# Spening — Fix Checklist (Complete)

All 20 issues from the code review have been addressed.

## 🔴 Bugs — All Fixed

- [x] **#1** Remove duplicate `PlaidService` DI registration in `Program.cs`
- [x] **#2** Paginate `FetchTransactions` in `PlaidService.cs`
- [x] **#3** `Accounts.razor` `LinkTokenResponse` binding — confirmed safe (uses dict); documented
- [x] **#4** `FetchAll` now returns total transactions processed, not just newly inserted rows
- [x] **#5** Settings schema migrated to typed model; `output_year` removed as stored setting

## 🟡 Risks — All Fixed

- [x] **#6** `HttpClient` base address now respects dev port (5200) vs Docker port (8080)
- [x] **#7** Sync cursor now saved per-account immediately after each successful merge
- [x] **#8** `LinkedAccount` now has `ItemId` column; `RemoveItem` deletes by `ItemId` not ciphertext
- [x] **#9** `DebugController` blocked at middleware level in non-Development environments
- [x] **#10** `RemoveItem` now catches Plaid errors and always proceeds with local DB deletion
- [x] **#11** `MergeAll` now bulk-loads existing transaction IDs in one query (was N+1)

## 🔵 Improvements — All Fixed

- [x] **#12** Year picker now shows dynamic dropdown (up to 7 years) derived from transaction data
- [x] **#13** `App.razor` has explicit `@using` directives
- [x] **#14** `Transaction.Category` documented as intentionally non-nullable empty string
- [x] **#15** `Plaid-Version` header now set centrally on the typed `HttpClient` in `Program.cs`
- [x] **#16** `ExportCsv` now takes `int year` (not nullable) — callers must resolve before calling
- [x] **#17** `ErrorBoundary` added to `MainLayout.razor`
- [x] **#18** Dockerfile comments explain the non-root/port constraint
- [x] **#19** Plaid SDK now lazy-loaded on first `speningPlaid.open()` call, not on every page
- [x] **#20** Hardcoded `output_year` migration seed is moot — removed by #5 settings migration

---

## Files Changed

| File | Changes |
|---|---|
| `Spening/Program.cs` | Remove duplicate `AddScoped<PlaidService>`, fix dev base address, add `Plaid-Version` header, gate debug routes |
| `Spening/Services/PlaidService.cs` | Paginate `FetchTransactions`, add `ItemId` to `FetchAndPersistAccounts`, fix `RemoveItem` to use `ItemId` and swallow Plaid errors |
| `Spening/Services/TransactionService.cs` | Per-account cursor save with error isolation, bulk-load merge, `FetchAll` returns total count, `ExportCsv` takes `int year` |
| `Spening/Services/SettingsService.cs` | Typed model, dynamic `GetOutputYear()` from last transaction, `GetAvailableYears()` endpoint |
| `Spening/Models/LinkedAccount.cs` | Add `ItemId` property |
| `Spening/Models/AppSetting.cs` | Rewritten as typed model (`Id`, `PlaidEnvironment`) |
| `Spening/Models/Transaction.cs` | Document `Category` empty-string convention |
| `Spening/Data/AppDbContext.cs` | Configure `ItemId` index, update `AppSetting` to typed model |
| `Spening/Controllers/AccountsController.cs` | Pass `ItemId` to `RemoveItem` |
| `Spening/Controllers/SettingsController.cs` | Remove `output_year` write path, add `GET /years` endpoint |
| `Spening/Controllers/TransactionsController.cs` | Match new `ExportCsv(int year)` signature |
| `Spening/Pages/Settings.razor` | Remove year input; show dynamic year as read-only |
| `Spening/Pages/Transactions.razor` | Year dropdown from `api/settings/years` |
| `Spening/Pages/Reports.razor` | Year dropdown from `api/settings/years` |
| `Spening/Shared/MainLayout.razor` | Add `ErrorBoundary` wrapper |
| `Spening/App.razor` | Add explicit `@using` directives |
| `Spening/Pages/_Host.cshtml` | Remove global Plaid SDK script tag |
| `Spening/wwwroot/plaidLink.js` | Lazy-load Plaid SDK on first `open()` call |
| `Spening/Dockerfile` | Add comment about non-root user / port constraint |
| `Spening/Data/Migrations/20260609000001_AddItemIdToLinkedAccounts.cs` | **New** — adds `ItemId` column + index |
| `Spening/Data/Migrations/20260609000002_UpdateSettingsSchema.cs` | **New** — drops EAV settings table, creates typed table |

## Migration Notes

Two new migrations must be run (`dotnet ef database update` or auto-applied on startup):

1. `AddItemIdToLinkedAccounts` — adds `item_id text NOT NULL DEFAULT ''` to `linked_accounts`.
   Existing rows get an empty string; they'll be populated correctly on the next re-link.

2. `UpdateSettingsSchema` — drops the old key-value `app_settings` table and creates a typed
   single-row table. **Resets `plaid_environment` to `sandbox`** — re-configure after upgrading
   if you were running in production mode.