# CashOut v2 — Minimal Redesign

## Goal

Strip the app down to its essentials: import transactions, view them, and see two reports — inflow vs outflow and spending by category. Remove all merchant/category normalization machinery. Keep the data raw.

---

## What Stays

### Transaction Importing

- **Plaid linking** — Add/remove linked accounts via Plaid Link. Incremental sync and full fetch.
- **CSV import** — 3-step wizard (upload → map columns → import). Per-account mapping profiles. PDF extraction via PdfPig.
- **Manual accounts** — Create/delete CSV-only accounts. Option to later link via Plaid (merge).

### Transaction Storage

- All transactions stored in their raw format from the source.
- **No normalization pipeline** — no `MerchantNormalizationService`, no `BusinessAlias`, `AliasPattern`, `RawBusiness`, or `RawBusinessAliasMap` tables.
- Transactions retain: `TransactionId`, `AccountId`, `Source`, `Date`, `Name` (raw), `Amount` (signed: positive = expense, negative = income), `Category` (raw from source or "Uncategorized").
- Plaid categories: use `personal_finance_category.primary` directly, or fall back to `category[0]` if unavailable.
- CSV categories: use the mapped CSV column value if provided, else "Uncategorized".
- Categories are immutable after import — no inline editing on the transactions page.

### Accounts

- **Linked Accounts page** — list, add via Plaid Link, sync, backfill (CSV), remove.
- **Manual Accounts page** — list, create, import CSV, link Plaid, delete.

---

## What Changes

### Transactions Page (`/transactions`)

**Remove:** The Month Summary expansion panel (category breakdown + 12-month averages).

**Keep:** Year dropdown + 12 month tabs. Transaction table with Date, Merchant (raw Name), Account, Category (read-only), Amount.

**Add:** Category filter popover (already exists, keep as-is).

This becomes a pure transaction browser — no reporting mixed in.

### Account Detail Page (`/accounts/{id}`)

**Remove:** Cash Flow tab, By Category tab.

**Keep:** Transactions tab with month tabs and transaction table (same as unified page but filtered to account).

This also becomes a pure transaction browser per account.

### Reporting — Two Reports Only

All reporting lives under a `/reports` section with two sub-pages.

#### 1. Inflow vs Outflow (`/reports/cashflow`)

- **Time range selector:** Month picker (single month) or month range (start month → end month).
- **Scope selector:** All accounts (aggregate) or a specific account.
- **Summary cards:**
  - Total Inflow (sum of negative amounts, displayed as positive)
  - Total Outflow (sum of positive amounts)
  - Net (Inflow − Outflow)
  - Transaction count
- **Monthly breakdown table** (one row per month in range):
  - Month
  - Inflow
  - Outflow
  - Net
  - Transaction count
- **Optional:** Simple bar chart (inflow green, outflow red, per month). Use MudBlazor's built-in chart or skip for MVP.

#### 2. Spending by Category (`/reports/category`)

- **Time range selector:** Same as above (month or month range, with "all" option).
- **Scope selector:** All accounts (aggregate) or a specific account.
- **Summary cards:**
  - Total Spending (sum of positive amounts only — expenses)
  - Transaction count
  - Average per category
- **Category breakdown table** (one row per category, sorted by total descending):
  - Category
  - Total (sum of positive amounts)
  - % of total spending
  - Transaction count
  - Average per transaction
- **Optional:** Drill-down — click a category to see its transactions filtered by that category and the selected time range.

---

## What Gets Removed

### Normalization System (entire pipeline)

**Tables to drop (via migration):**
- `business_aliases`
- `alias_patterns`
- `raw_businesses`
- `raw_business_alias_map`

**Services to remove:**
- `MerchantNormalizationService` — the entire file.

**Code to remove/simplify:**
- `TransactionService.MergePlaid()` — remove alias matching + RawBusiness creation steps. Just store the raw Plaid transaction directly.
- `CsvImportService.Import()` — remove normalization resolution from the import loop. Store raw CSV description as `Name`.
- `Transaction` entity — remove `NormalizedName`, `AliasId`, `RawBusinessId` properties.
- `AppDbContext` — remove `DbSet` entries and `OnModelCreating` config for removed entities and removed properties.
- `TransactionService.Query()` — remove category alias resolution from query results.

**Pages to remove:**
- `Merchants.razor` — the entire page (alias management, pattern testing, unmapped business mapping).

**Controllers to remove:**
- `BusinessNormalizationController` (`/api/normalization`) — the entire file.

**Reports to remove:**
- `ReportMerchant.razor` — merchant spending report.
- `ReportIncome.razor` — income by source/merchant report (income is now just inflow on the cashflow report).
- `Reports.razor` — executive summary (replaced by two focused reports).
- `ReportCashFlow.razor` — replaced by the new simplified cashflow report.

**ReportService methods to remove:**
- `GetTopMerchants()`, `GetIncome()`, `GetExecutiveSummary()`, `GetLargest()`, `GetPivot()`, `GetCategorySummary()` (the old month-summary on the transactions page).
- Simplify `GetByCategory()` to just sum positive amounts by category — no year-over-year, no trailing averages.

**API endpoints to remove:**
- `GET/POST/PATCH/DELETE /api/normalization/*` — all normalization endpoints.
- `GET /api/reports/summary`, `GET /api/reports/merchants`, `GET /api/reports/income`, `GET /api/reports/largest`, `GET /api/reports/pivot`.
- `GET /api/reports/category-summary` — the old month summary endpoint.
- `PATCH /api/transactions/{id}/category` — no longer needed since categories are immutable after import.

### Other Removals

- **Excluded Categories** — remove from `Settings` page, `AppSetting` table, and all filtering logic. If you want to ignore a category, just don't look at it.
- **Settings page** — simplify or remove. Only meaningful setting left is Plaid environment display.
- **ReportShell.razor** — may be simplified or kept as a thin wrapper for the two remaining reports.

---

## Navigation Structure (Simplified)

```
┌─────────────────────┐
│ CashOut             │
├─────────────────────┤
│ Accounts            │
│   Linked Accounts   │
│   Manual Accounts   │
├─────────────────────┤
│ Transactions        │
├─────────────────────┤
│ Reports             │
│   Inflow vs Outflow │
│   By Category       │
└─────────────────────┘
```

---

## Database Changes

### Migration: Remove normalization tables + simplify Transaction

1. Drop tables: `raw_business_alias_map`, `raw_businesses`, `alias_patterns`, `business_aliases`
2. Alter `transactions` table:
   - Drop column `normalized_name`
   - Drop column `alias_id`
   - Drop column `raw_business_id`
3. Drop table `app_settings` (or keep if you want excluded categories — TBD)
4. Remove `ExcludedCategories` from settings if dropped

---

## Implementation Order

1. **Create the migration** — drop normalization tables, alter Transaction entity.
2. **Simplify Transaction entity** — remove `NormalizedName`, `AliasId`, `RawBusinessId`. Remove from `AppDbContext`.
3. **Delete normalization code** — `MerchantNormalizationService`, `BusinessNormalizationController`, `Merchants.razor`. Remove `PATCH /api/transactions/{id}/category` endpoint.
4. **Simplify import pipelines** — `TransactionService.MergePlaid()` and `CsvImportService.Import()` no longer resolve aliases.
5. **Remove old report pages** — `Reports.razor`, `ReportMerchant.razor`, `ReportIncome.razor`, `ReportCashFlow.razor`. Delete corresponding `ReportService` methods.
6. **Simplify Transactions page** — remove Month Summary panel, remove `category-summary` API call.
7. **Simplify Account Detail page** — remove Cash Flow and By Category tabs.
8. **Build new Reports** — two new pages: Inflow vs Outflow, Spending by Category. New API endpoints on `ReportsController`.
9. **Update navigation** — `MainLayout.razor` drawer links.
10. **Clean up settings** — remove excluded categories, simplify or remove Settings page.
11. **Test end-to-end** — import via Plaid, import via CSV, verify raw storage, verify both reports, verify per-account filtering.

---

## Decisions

- **Category editing:** No — remove inline category editing from the transactions page. Categories stay as-is from import source.
- **PDF import:** Keep — already built and useful for manual imports.
- **CSV mapping profiles:** Keep — versioned profiles per account, no reason to remove.
- **Default date range:** Reports default to the current month, with option to change.
