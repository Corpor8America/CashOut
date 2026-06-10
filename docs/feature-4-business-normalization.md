# Business Normalization  
## Raw Businesses, Aliases, and Mapping

## Overview
Real‑world merchant names are messy and inconsistent across banks, CSV exports, and Plaid.  
This system normalizes those names into clean canonical merchants while preserving raw data and giving users full control over categorization.

The model uses **three tables**:

1. RawBusinesses  
2. Aliases  
3. RawBusinessAliasMap (pivot table)

This structure ensures deterministic categorization, clean reporting, and user‑driven overrides.

---

## RawBusinesses Table (Raw Merchant Names)

Stores the exact merchant names as they appear in CSV or Plaid.

### Schema

| Column      | Description |
|-------------|-------------|
| id          | Primary key |
| raw_name    | Case‑insensitive unique merchant name from CSV/Plaid |
| category_id | Category assigned from the first transaction seen for this raw name, or `"None"` if no category provided |
| created_at  | Timestamp |
| updated_at  | Timestamp |

### Behavior

When a new transaction arrives:

1. Normalize `raw_name` to a case‑insensitive form.  
2. If the raw name does **not** exist:
   - Create a new RawBusiness.
   - Assign category:
     - If CSV/Plaid provided a category → use it.
     - Otherwise → set category to `"None"`.
3. If the raw name **does** exist:
   - Do **not** overwrite the category unless the user manually edits it.

RawBusinesses preserve the original merchant names exactly as they appear in the data source.

---

## Aliases Table (Canonical Merchants)

Stores clean, user‑defined canonical merchant names.

### Schema

| Column      | Description |
|-------------|-------------|
| id          | Primary key |
| alias_name  | Canonical merchant name (“Amazon”, “Starbucks”, etc.) |
| category_id | Category for the canonical merchant |
| created_at  | Timestamp |

### Behavior

- Alias category **overrides** raw business category.
- Alias name is used in:
  - UI transaction lists  
  - Reports  
  - Charts  
  - Summaries  

Aliases allow users to unify multiple messy raw names under one clean merchant.

---

## RawBusinessAliasMap (Pivot Table)

Maps raw business names → canonical aliases.

### Schema

| Column          | Description |
|-----------------|-------------|
| id              | Primary key |
| raw_business_id | FK → RawBusinesses.id |
| alias_id        | FK → Aliases.id |

### Behavior

- A raw business can map to **one** alias.
- Many raw businesses can map to the **same** alias.
- If no mapping exists, the raw business stands alone and is displayed as‑is.

This mapping is the core of merchant normalization.

---

## Category Priority

When determining the final category for a transaction, the system applies the following priority:

1. **Alias category** (highest priority)  
2. **Raw business category**  
3. **CSV/Plaid category** (only used to initialize raw business category)  
4. **None** (if nothing else applies)

This ensures categorization is:

- Deterministic  
- User‑controlled  
- Consistent across all data sources  

