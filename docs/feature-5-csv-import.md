# CSV Import Specification

This document defines how Spening imports CSV files, maps columns, deduplicates rows, handles credit/debit logic, and surfaces skipped rows to the user.

---

## 1. Objective

Allow users to import CSV files from any institution by mapping CSV columns to Spening’s transaction schema, with robust deduplication to prevent double‑importing the same statement and clear surfacing of skipped rows.

---

## 2. Import Workflow

1. User uploads CSV.
2. App displays CSV preview in a table.
3. User maps CSV columns to transaction fields for that account:
   - Date → `Date`
   - Description → `Name`
   - Credit → `Credit` (optional)
   - Debit → `Debit` (optional)
   - Single Amount column → mapped to both `Credit` and `Debit`
   - Category → `Category` (optional)
4. Mapping profile is saved per account.
5. On subsequent uploads:
   - System attempts to auto‑apply the saved mapping.
   - If any mapped column is missing, user must remap.

---

## 3. Deduplication

Dedup is performed on the raw CSV values of the columns that are mapped and kept, not on normalized or parsed values.

### Dedup Process

For each row:

    Extract raw values of mapped columns (e.g., Date, Description, Credit, Debit, Category).
    Build a dedup key (string or hash) from these raw values.
    If the dedup key already exists for this account, the row is skipped.

### Guarantees

- Uploading the same CSV twice does not create duplicates.
- Changes in unused columns do not affect dedup.
- Dedup is deterministic and scoped per account.

---

## 4. Credit and Debit Columns in the Database

To align Plaid and CSV behavior, Spening stores Credit and Debit as separate columns.

| Column           | Description |
|------------------|-------------|
| date             | Transaction date |
| name             | Merchant/description |
| credit           | Positive inflow amount (nullable) |
| debit            | Positive outflow amount (nullable) |
| category_id      | Category reference (nullable) |
| source           | `Plaid` or `CSV` |
| dedup_key        | Stored key for CSV dedup (nullable) |
| raw_business_id  | FK → RawBusinesses.id |
| alias_id         | FK → Aliases.id (nullable) |

---

## 5. Plaid Mapping

Plaid provides a single signed `amount` field.

If amount > 0:

    credit = amount
    debit = null

If amount < 0:

    credit = null
    debit = abs(amount)

---

## 6. CSV Mapping Rules

### 6.1 Institutions With Separate Credit and Debit Columns

- Map Credit → credit
- Map Debit → debit

Parsing rules:

    Strip $, commas, whitespace
    Convert parentheses to negative
    Store debit as positive (absolute value)

---

### 6.2 Institutions With a Single Amount Column

User maps the same column to both credit and debit.

### Corrected Single‑Amount Logic

    # parsed_value is a signed decimal (e.g., -123.45 or 123.45)

    if parsed_value > 0:
        credit = parsed_value
        debit = null
    else if parsed_value < 0:
        credit = null
        debit = abs(parsed_value)
    else:
        credit = null
        debit = null   # or skip row if zero is not meaningful

---

## 7. Skipped Row Handling

### Objective

Ensure that any CSV rows that cannot be imported are clearly surfaced to the user with actionable explanations.

---

### 7.1 When a Row Is Skipped

A row is skipped when:

- Both Credit and Debit contain values  
- Neither Credit nor Debit can be derived  
- Date parsing fails  
- Required mapped fields are empty  
- Parsed amount is zero (optional rule)  
- Mapping rules produce an invalid transaction  

---

### 7.2 Specific Rule: Both Credit and Debit Filled

If both columns contain non‑empty values:

    Row is invalid and skipped.
    Reason: Both Credit and Debit contain values.

This prevents silent corruption and keeps the importer deterministic.

---

### 7.3 Skipped Rows Summary (UI Behavior)

After processing the CSV, the user sees:

#### A. Summary Banner

    X rows were skipped during import.
    Review skipped rows for details.

#### B. Expandable “Skipped Rows” Panel

| Row # | Raw Data (truncated) | Reason |
|-------|------------------------|--------|
| 14 | "2024‑01‑01, ACH TRANSFER, 100, -100" | Both Credit and Debit contain values |
| 22 | "2024‑01‑03, , , " | Missing required fields |
| 37 | "2024‑01‑05, Starbucks, abc" | Amount could not be parsed |

#### C. Downloadable “Skipped Rows CSV”

Contains:

- Raw row values  
- Reason for skip  
- Row number  

---

### 7.4 Error Severity

All skipped rows are non‑fatal:

- Import continues normally.
- Valid rows are imported.
- Skipped rows are surfaced at the end.

---

## 8. Mapping Profile Versioning & Format Change Detection

### Objective

Ensure that CSV mappings remain valid even when institutions change their CSV export format, while only requiring remapping when a **mapped** column is missing or renamed.

---

### 8.1 Mapping Profile Structure

A mapping profile is stored per account and includes:

- The list of mapped column names (case‑insensitive)
- The user‑selected mapping:
  - Date column
  - Description column
  - Credit column (optional)
  - Debit column (optional)
  - Single Amount column (optional)
  - Category column (optional)
- A version number (integer)

---

### 8.2 Mapping Profile Validation

When a CSV is uploaded:

    For each mapped column:
        Check if the mapped column name exists in the CSV headers (case-insensitive).

The mapping profile is valid **only if all mapped columns exist**.

Unmapped columns may appear or disappear without affecting validity.

---

### 8.3 Format Change Detection

A mapping profile becomes invalid only when:

- A mapped column is missing  
- A mapped column is renamed  
- A mapped column is empty or malformed  

The system does not attempt to guess or auto‑repair mappings.

---

### 8.4 Mapping Profile Versioning

When the user remaps:

    A new mapping profile version is created.
    Version increments (1 → 2 → 3 → …).

Old versions may be retained for reference.

---

### 8.5 UI Behavior

If mapping is invalid:

    One or more mapped columns are missing. Please remap the CSV.

The UI highlights missing mapped columns and requires a new mapping.

---

## 9. Summary

| Feature | Impact |
|---------|--------|
| Mapping profiles | Faster repeat imports |
| Raw‑value dedup | Prevents duplicate statements |
| Credit/Debit separation | Aligns CSV and Plaid |
| Single‑amount logic | Handles common bank formats |
| Skipped row surfacing | Prevents silent corruption |
| Downloadable skipped CSV | User‑friendly debugging |
| Mapped‑column‑only validation | Robust against harmless CSV changes |

