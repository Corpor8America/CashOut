# Spening Merchant Normalization & Alias Pattern System (Developer Specification)

This document defines the technical architecture for merchant normalization, alias pattern matching, category assignment, and raw business tracking in Spening.  
It is intended for developers implementing or maintaining the ingestion pipeline.

---

# 1. System Goals

- Normalize inconsistent merchant strings into stable canonical identities.
- Auto‑categorize transactions using alias‑level rules.
- Force ambiguous merchants into `Unassigned`.
- Never use CSV/Plaid categories for categorization.
- Preserve raw merchant data for debugging and retroactive fixes.
- Provide deterministic, testable matching behavior.

---

# 2. Core Concepts

### Alias
Canonical merchant identity.  
May optionally define a category override.

### AliasPattern
Substring or regex rule that maps normalized merchant strings to an Alias.

### RawBusiness
Represents the first occurrence of an unmapped merchant.  
Stored for audit, debugging, and retroactive matching.

### Transaction Category
Final category stored on each transaction.  
Alias category = default.  
Transaction category = truth.

### CSV/Plaid Category
Stored only for reference.  
Never used for categorization.

---

# 3. Database Schema

## 3.1 Aliases
```sql
id INTEGER PRIMARY KEY
alias_name TEXT NOT NULL
category_override_id INTEGER NULL  -- FK → Categories
created_at TIMESTAMP NOT NULL
updated_at TIMESTAMP NOT NULL
```

## 3.2 AliasPatterns
```sql
id INTEGER PRIMARY KEY
alias_id INTEGER NOT NULL          -- FK → Aliases.id
pattern TEXT NOT NULL
match_type TEXT NOT NULL           -- ENUM('contains', 'starts_with', 'regex')
created_at TIMESTAMP NOT NULL
updated_at TIMESTAMP NOT NULL
```

## 3.3 RawBusinesses
```sql
id INTEGER PRIMARY KEY
raw_name_original TEXT NOT NULL
raw_name_normalized TEXT NOT NULL
category_raw TEXT NULL         -- reference only, never used for categorization
category_from_first_transaction_id INTEGER NULL
is_mapped BOOLEAN NOT NULL
created_at TIMESTAMP NOT NULL
updated_at TIMESTAMP NOT NULL
```

## 3.4 RawBusinessAliasMap
```sql
id INTEGER PRIMARY KEY
raw_business_id INTEGER NOT NULL   -- FK → RawBusinesses.id
alias_id INTEGER NOT NULL          -- FK → Aliases.id
created_at TIMESTAMP NOT NULL
```

---

# 4. Normalization Pipeline

Normalization must be deterministic and idempotent.

### Steps
1. Trim whitespace.
2. Collapse multiple spaces → single space.
3. Convert to uppercase.
4. Remove punctuation: `- * . / :`.
5. Collapse spaces again.
6. Remove numeric sequences > 6 digits.
7. Optionally remove parenthetical merchant names.

### Example
Input:
```
"ACH DEBIT  Wells Fargo Card Ccpymt 026030004374949 (Wells Fargo Card Ccpymt)"
```

Output:
```
"ACH DEBIT WELLS FARGO CARD CCPYMT"
```

---

# 5. Matching Algorithm

### Algorithm
```pseudo
function match_alias(normalized_merchant):
    candidates = []

    for pattern in AliasPatterns:
        if pattern.match_type == 'contains' and normalized_merchant.contains(pattern.pattern):
            candidates.append(pattern.alias_id)
        else if pattern.match_type == 'starts_with' and normalized_merchant.starts_with(pattern.pattern):
            candidates.append(pattern.alias_id)
        else if pattern.match_type == 'regex' and regex(pattern.pattern).matches(normalized_merchant):
            candidates.append(pattern.alias_id)

    if candidates.is_empty():
        return null

    return alias_with_lowest_id(candidates)  // deterministic
```

---

# 6. Transaction Categorization Rules

### Rule 1 — Alias with category override
```
transaction.category_id = alias.category_override_id
```

### Rule 2 — Alias exists but has NO category override
```
transaction.category_id = UNASSIGNED_CATEGORY_ID
```

### Rule 3 — No alias exists
```
transaction.category_id = UNASSIGNED_CATEGORY_ID
```

### Rule 4 — User manually assigns category
```
transaction.category_id = user_selected_category_id
```

### Rule 5 — CSV/Plaid category
```
Never used for categorization.
Stored only for reference.
```

---

# 7. Import Pipeline

```pseudo
function import_transaction(raw_name, category_raw):
    normalized = normalize(raw_name)
    alias = match_alias(normalized)

    if alias != null:
        if alias.category_override_id != null:
            category_id = alias.category_override_id
        else:
            category_id = UNASSIGNED_CATEGORY_ID
    else:
        raw_business_id = insert_into_RawBusinesses(
            raw_name_original = raw_name,
            raw_name_normalized = normalized,
            category_raw = category_raw,
            is_mapped = false
        )
        category_id = UNASSIGNED_CATEGORY_ID

    insert_transaction(
        raw_name = raw_name,
        normalized_name = normalized,
        alias_id = alias?.id,
        category_id = category_id,
        category_raw = category_raw
    )
```

---

# 8. RawBusiness Behavior

### Creation
Created only when no alias matches.

### Mapping
- Insert into RawBusinessAliasMap.
- Set `is_mapped = true`.

### Retention
RawBusinesses are never deleted.

### UI
- Only unmapped RawBusinesses appear in the UI.
- Mapped ones remain hidden but stored.

---

# 9. Alias Management

### Pattern Test Tool
```pseudo
function test_pattern(raw_input):
    normalized = normalize(raw_input)
    alias = match_alias(normalized)
    category_id = resolve_category(alias)

    return {
        raw_input,
        normalized,
        matched_alias: alias,
        resulting_category_id: category_id
    }
```

---

# 10. Ambiguous Merchant Handling

Payment rails (Venmo, PayPal, Cash App, Zelle, Square, bank transfers) must:

- Have aliases
- Have **no** category override
- Always produce `Unassigned` until manually categorized

---

# 11. Retroactive Fixing

When new patterns are added:

1. Re-run matching against RawBusinesses.
2. Update RawBusinessAliasMap.
3. Optionally re-categorize historical transactions.

---

# 12. Developer Notes

### Determinism
- Stable normalization.
- Stable alias selection.
- Stable pattern ordering.

### Performance
- Preload AliasPatterns.
- Normalize once per transaction.
- Use compiled regex.

### Testing
- Normalization edge cases.
- Pattern matching.
- Ambiguous merchant behavior.
- Category resolution.
- Retroactive matching.

---

# 13. Summary

This spec defines:

- Canonical merchant identity structures
- Deterministic normalization and matching
- Strict category rules (no CSV/Plaid influence)
- Safe handling of ambiguous merchants
- Full auditability via RawBusinesses

This is the reference implementation guide for Spening’s ingestion engine.
