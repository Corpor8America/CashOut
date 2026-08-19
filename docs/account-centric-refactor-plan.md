# Account-Centric Refactor Plan

## Goal

Move CashOut from a global-dashboard model to an account-centric model:

- Remove merchant normalization (aliases, patterns, raw businesses) entirely.
- Category comes directly from the source (Plaid `personal_finance_category` /
  CSV `CategoryColumn`) — no alias override, no "Unassigned" bucket.
- Clicking an account goes to `/accounts/{id}`: the existing transaction list
  for that account, plus two account-scoped reports — **Cash Flow**
  (income vs. expense by month) and **By Category**.
- The global Reports section (Executive Summary, Spending by Category,
  Merchant, Income, Cash Flow) and the Merchants & Aliases page go away.

## Why phased

The existing app works today and has real linked/manual accounts with data.
We don't want a half-migrated state where imports break or reports 500 on
missing tables. So:

- **Phase 1** builds the new account-centric pages and simplified reporting
  *alongside* the existing app. Nothing existing is removed or changed.
  Both old and new nav entries are reachable. Category still comes from the
  alias/normalization pipeline during this phase (unchanged import logic) —
  the new report pages just group by whatever `Category` already holds.
- **Phase 2** is the cutover: strip normalization out of the import/sync
  pipeline so `Category` is populated directly from source, wipe all
  transactions, reimport (CSV re-upload + Plaid full re-fetch). This is the
  one intentionally destructive step, done deliberately once Phase 1 is
  verified working end to end.
- **Phase 3** deletes the now-dead code: old report pages, Merchants page,
  `MerchantNormalizationService`, the four normalization tables, and the
  now-unused `Alias`/`RawBusiness` FKs and `NormalizedName`/`RawName` columns
  on `Transaction`.

Each phase ends in a fully working, deployable app.

---

## Phase 1 — Build the new, don't touch the old

**Non-goals:** no deletions, no changes to `TransactionService`,
`CsvImportService`, `MerchantNormalizationService`, or existing migrations.

### 1.1 New route: `/accounts/{id}`

New page `CashOut/Pages/AccountDetail.razor` (`@page "/accounts/{AccountId}"`).

- Header: account name/institution (linked) or name/description (manual),
  reusing the lookup logic already in `TransactionsController.List` for
  `AccountName`.
- Tabs (MudTabs): **Transactions**, **Cash Flow**, **By Category**.
  - Transactions tab: same year/month/category-filter UI as
    `Transactions.razor` today, but hardcoded to this `AccountId` (no "Show
    all" escape hatch needed since we're already scoped).
  - Cash Flow tab: new small report, described below.
  - By Category tab: new small report, described below.

Update row-click handlers in `Accounts.razor` and `ManualAccounts.razor` to
navigate to `/accounts/{id}` instead of `/transactions?accountId=...`.
**Leave `/transactions` itself untouched** — still useful as a global view
during Phase 1, and other things may still link to it.

### 1.2 New service methods: `AccountReportService`

New file `CashOut/Services/AccountReportService.cs`, separate from
`ReportService` so we're not touching the existing (soon-to-be-deleted)
report logic at all.

```csharp
public class AccountReportService
{
    // GetCashFlow(accountId, year) -> monthly income/expense/net for one account
    // GetByCategory(accountId, year, month?) -> category totals for one account,
    //     grouped directly on Transaction.Category (no alias/merchant grouping)
}
```

Both queries are simple `Where(t => t.AccountId == accountId)` +
`GroupBy(t => t.Category)` / `GroupBy(t => t.Date.Month)`. No alias lookups,
no `MerchantKey`, no `IsMapped`. Reuse the sign convention
(`Amount > 0` = expense) already established in `Transaction`.

Skip previous-year comparison and 12-month rolling averages for v1 of these
account reports — start simple (current year totals, monthly breakdown,
category breakdown). Can add comparisons later if wanted.

### 1.3 New controller: `AccountReportsController`

`CashOut/Controllers/AccountReportsController.cs`, routes:

- `GET api/accounts/{id}/reports/cashflow?year=`
- `GET api/accounts/{id}/reports/category?year=&month=`

Keep these separate from `ReportsController` so Phase 3 deletion is a clean
file removal, not a surgical edit.

### 1.4 Sidebar nav

Add the two new tabs are *inside* `/accounts/{id}`, so no new sidebar entries
needed beyond what already links to accounts (Linked Accounts, Manual
Accounts). Leave the existing "Reports" and "Merchants & Aliases" sidebar
sections in place for now — they still work, untouched.

### 1.5 Verification

- `dotnet build`
- `dotnet test --filter "TestCategory!=UI"` — should be unaffected since
  nothing existing changed.
- Manually click into a couple of accounts, confirm Cash Flow and By
  Category tabs render sensible numbers against current (alias-influenced)
  category data.
- Old `/reports/*` and `/merchants` pages still work exactly as before.

---

## Phase 2 — Cutover: strip normalization from ingestion, wipe, reimport

Only start this once Phase 1 has been used for a bit and you're confident
the new account pages are what you want.

### 2.1 Strip normalization calls from ingestion (leave tables/service intact for now)

- `CashOut/Services/TransactionService.cs` (`MergePlaid`): remove the
  `_normalization.ResolveBulk` call and the alias/raw-business assignment.
  Instead: `txn.Category = <source category>` directly (see 2.2), no
  `AliasId`, no `RawBusinessId`, no display-name swap.
- `CashOut/Services/CsvImportService.cs` (`Import`): same — remove
  `_normalization.ResolveBulk`, assign `Category` from `categoryRaw`
  directly (or blank/`"(uncategorized)"` if the CSV has no category column).
- Both services can drop their `MerchantNormalizationService` constructor
  dependency at this point, but leave the class itself and its DI
  registration in `Program.cs` alone until Phase 3 — no reason to touch two
  things at once.
- Remove `NormalizedName`/`RawName` writes in these two services too, or
  just stop populating anything alias-related. Decide in Phase 3 whether to
  drop the columns; for now they can sit unused.

### 2.2 Category source of truth

- Plaid: `PlaidService.MapTransaction` already computes `Category` from
  `personal_finance_category.primary` (with a fallback to the legacy
  `category` array joined with `" > "`). This logic is already correct
  and source-driven — no change needed here, since normalization was only
  ever applied *after* this in `TransactionService`/`CsvImportService`.
- CSV: `CsvImportService.Import` should set `Category = categoryRaw` (the
  raw value from the mapped `CategoryColumn`, or `""` if unmapped/absent).

### 2.3 Data wipe

One-time destructive step, staged behind an explicit confirmation:

```sql
DELETE FROM transactions;
```

Do **not** delete `linked_accounts`, `manual_accounts`, or Plaid sync
cursors carelessly — but note that with `SyncCursor` intact, a plain
`SyncAll()` after the wipe will only pull *new* transactions since the last
cursor, not backfill. So for each linked account, either:
- Null out `SyncCursor` on `linked_accounts` before re-syncing (forces a
  full resync from Plaid), or
- Use the existing `FetchAll()` / `POST api/transactions/fetch` path, which
  explicitly re-fetches the full configured year regardless of cursor.

For manual (CSV-only) accounts: re-upload the original CSV files through
the existing `/csv-import/{accountId}` flow — mapping profiles are untouched
by the wipe, so this should be a quick re-run per account.

### 2.4 Verification

- Confirm `transactions` table is empty, `raw_businesses` /
  `business_aliases` still have old data (harmless, unreferenced) — cleanup
  happens in Phase 3.
- Re-run Plaid full fetch per linked account, re-import CSVs per manual
  account.
- Spot-check `/accounts/{id}` Cash Flow and By Category tabs now reflect
  real source categories (e.g. actual Plaid `personal_finance_category`
  values, not `"Unassigned"`).
- Run `dotnet test --filter "TestCategory!=UI"` — expect the
  `MerchantNormalizationServiceTests` and any test asserting
  `Category == Unassigned` / alias-driven category behavior to now fail or
  need updating, since ingestion no longer calls normalization. This is
  expected; those tests get deleted in Phase 3 along with the service.

---

## Phase 3 — Delete the old

Only after Phase 2 is confirmed stable in real use for a while.

### 3.1 Delete files

- `CashOut/Services/MerchantNormalizationService.cs`
- `CashOut/Controllers/BusinessNormalizationController.cs`
- `CashOut/Pages/Merchants.razor`
- `CashOut/Models/BusinessAlias.cs`, `AliasPattern.cs`, `RawBusiness.cs`,
  `RawBusinessAliasMap.cs`
- `CashOut.Tests/MerchantNormalizationServiceTests.cs`
- Old global report pages: `Reports.razor`, `ReportCategory.razor`,
  `ReportMerchant.razor`, `ReportIncome.razor`, `ReportCashFlow.razor`,
  `Shared/ReportShell.razor` (if nothing else uses it)
- `CashOut/Controllers/ReportsController.cs`
- Merchant/alias-related sections of `ReportService.cs` — likely simplest to
  delete the whole file if `AccountReportService` has fully replaced it, or
  strip it down to whatever (if anything) is still shared.
- `CashOut.Tests/ReportServiceTests.cs` — replace with tests targeting
  `AccountReportService` if not already covered.

### 3.2 Sidebar / layout

- `CashOut/Shared/MainLayout.razor`: remove the "Reports" nav section
  (Executive Summary / By Category / By Merchant / Income / Cash Flow links)
  and the "Merchants" section (Merchants & Aliases link).

### 3.3 Model cleanup

- `CashOut/Models/Transaction.cs`: remove `AliasId`/`Alias`,
  `RawBusinessId`/`RawBusiness`. Keep `RawName`/`NormalizedName` —
  they're still used for CSV dedup fingerprinting via `TextNormalizer`.
- `CashOut/Data/AppDbContext.cs`: remove the corresponding `DbSet`s and
  `OnModelCreating` config for the four normalization entities and the FK
  configs on `Transaction`.

### 3.4 Migration

Generate one migration that drops:
- Tables: `alias_patterns`, `business_aliases`, `raw_businesses`,
  `raw_business_alias_map`
- Columns on `transactions`: `AliasId`, `RawBusinessId`, and
  `RawName`/`NormalizedName` if removed from the model.

```bash
dotnet ef migrations add RemoveMerchantNormalization --project CashOut
dotnet build
dotnet test --filter "TestCategory!=UI"
```

### 3.5 Verification

- Full app boot, confirm auto-migration applies cleanly against a fresh DB
  and against a DB that went through Phases 1–2.
- `/accounts/{id}` still works with Cash Flow and By Category tabs.
- No dangling references to deleted types anywhere (`dotnet build` will
  catch this).
- Update `README.md` to remove the Merchant Normalization section and old
  report list, and describe the new per-account report tabs instead.

---

## Open questions (all resolved in Phase 1)

- ~~**Category reports scope**: current-year only, or also expose a year
  picker like the old reports did?~~ Resolved: Phase 1 includes a year
  dropdown backed by `SettingsService.GetAvailableYears()`.
- ~~**Uncategorized handling**: if a CSV row has no category, do we show
  `"(uncategorized)"` like the old `CategoryReportRow` did, or leave it
  blank?~~ Resolved: Phase 1 uses `"(uncategorized)"` fallback.
- ~~**Excluded categories setting**: `SettingsService.GetExcludedCategories()`
  currently filters global reports. Decide whether account-level reports
  should also respect it (probably yes, for consistency) or whether that
  setting becomes account-scoped too (bigger change, probably not worth it
  now).~~ Resolved: Phase 1 `AccountReportService` calls
  `GetExcludedCategories()` for consistency.
