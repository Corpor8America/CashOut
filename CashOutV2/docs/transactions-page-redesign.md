# Transactions Page — Grouped Year View

## Goal

Replace the 12 month tabs with a single year-wide transaction table grouped into collapsible month sections, and put a transaction-name search box where the tabs used to be. The page shifts from a one-month drill-down to a searchable year browser.

---

## Current State

- Year dropdown + 12 `MudTabPanel` month tabs. Switching tabs re-fetches that month only (`GET api/transactions?year=&month=`).
- Category filter popover re-fetches from the server with `&category=` params.
- Account banner via `?accountId=` query param.
- One flat `MudTable` (Date, Name, Account, Category, Amount).

---

## Design

### Toolbar (replaces tabs row)

```
┌──────────────────────────────────────────────────────────────────┐
│ [Year ▾]   [🔍 Search transactions...              ]   [⚙ Filter] │
└──────────────────────────────────────────────────────────────────┘
```

- **Year dropdown** — unchanged.
- **Search box** (`MudTextField`, magnifier icon, clearable) — filters by transaction name.
- **Category filter button** — same popover UI as today.

### Data loading

One fetch per year: `GET api/transactions?year={year}` (+ `&accountId=` when present).

- No backend change required — `month` is already optional in `TransactionService.Query()` (TransactionService.cs:133) and the controller.
- Because the whole year is in memory, **category filtering becomes client-side** (Apply/Clear just recompute the visible set instead of re-fetching). The server's `category` param stays supported for other callers.

### Two view modes

**1. Browse (search box empty)** — one `MudTable` grouped by month:

- `GroupBy` → transaction month; `Expandable = true`; `IsInitiallyExpanded = true`.
- All months render expanded on load — scrolling shows the whole year. Months can be collapsed individually to hide noise.
- `GroupHeaderTemplate` renders: month name, transaction count, outflow and inflow totals (right-aligned):

  ```
  ▸ January   42 transactions        $2,431.10 out   $3,150.00 in
  ```

- Totals use the existing fields: outflow = `Sum(Debit ?? 0)`, inflow = `Sum(Credit ?? 0)` (sign convention: positive Amount = expense).
- Rows within a group keep the existing column layout, sorted date descending (as returned by the API).

**2. Searching (search box non-empty)** — flat, ungrouped table:

- All matching transactions across the entire year, date descending.
- Footer caption: *"N match(es) for 'query' in {year}"*.
- Rationale: MudBlazor's `TableGroupDefinition.IsInitiallyExpanded` applies uniformly to all groups (verified in 9.5.0), so "auto-expand only months with matches" isn't natively supported. A flat result list is simpler and easier to scan anyway.

### Search semantics

- Case-insensitive substring match on `Name` (`Contains(..., OrdinalIgnoreCase)`).
- Composes with the category filter (AND).

### Initial state

All groups start expanded (MudBlazor's `IsInitiallyExpanded` is uniform across groups, so "current month only" isn't natively possible — all-expanded is the chosen behavior). Collapse is available per month for trimming the view.

---

## Kept As-Is

- Account-filter banner and "Show all" button (`?accountId=` handling).
- Error alert + loading spinner states.
- Dense/hover table styling, `Breakpoint.Sm` mobile DataLabels.
- Inline "(uncategorized)" display for empty categories.

## Empty States

- Year has no transactions → existing "No transactions found" message.
- Search yields nothing → *"No transactions match '{query}'."*

---

## Files Changed

| File | Change |
|---|---|
| `CashOut/Pages/Transactions.razor` | Only file modified |

No controller, service, or migration changes.

---

## Performance Notes

- A full year of personal-finance transactions is typically hundreds to low thousands of rows — fine to hold and render client-side.
- Filtering on each keystroke is cheap at this scale; add debouncing only if profiling shows need.
- Escape hatch if accounts prove very chatty later: server-side `&search=` param or Blazor `Virtualize`.

---

## Verification

1. `dotnet build CashOut/CashOut.csproj`
2. Manual:
   - Page loads full year in one request (network tab: no `month=` param).
   - Months render as collapsible headers with correct counts/totals; expand/collapse works.
   - Search narrows across all months instantly; clearing restores group view.
   - Search + category filter compose correctly.
   - Year switch resets search results correctly.
   - `?accountId=` banner still filters and clears.
   - Mobile layout still usable (DataLabels).
3. `dotnet test --filter "TestCategory!=UI"` — should be unaffected (no unit tests touch this page; no Playwright tests target `/transactions`).

---

## Decisions

- **Single grouped table over 12 expansion panels** — consistent column widths across months; less markup; built-in collapse behavior.
- **Flat results while searching** — uniform-only initial group expansion in MudBlazor makes selective auto-expand impractical; flat match list is better UX regardless.
- **Category filter goes client-side** — data for the year is already loaded; filtering feels instant. Server param kept for compatibility.
- **Search scope: Name only** — matches the original request; not extended to Account/Category.
- **Groups expanded by default** — the page opens as a scrollable full-year view; collapse is opt-out per month.
