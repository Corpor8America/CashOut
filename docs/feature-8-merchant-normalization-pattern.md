# Spening Merchant Normalization & Alias Pattern System

This document defines the architecture, workflow, and rules for Spening’s merchant identity resolution, alias pattern matching, category assignment, and raw business management. It also includes a section explaining how the system worked previously and why the redesign was necessary.

---

# 1. Background: How Spening Worked Before

Originally, Spening attempted to categorize transactions using a direct mapping from raw merchant names to categories. The logic was:

- Each transaction had a `business_name` extracted from CSV/Plaid.
- The system attempted to map that business name directly to a category.
- If the business name was unknown, the user could assign a category.

### Why this failed

Bank merchant strings are extremely inconsistent. The same merchant may appear as:

AMZN MKTP US 12345  
AMAZON.COM*ABCD  
AMZN Mktp US 98765  

Or:

ACH DEBIT WELLS FARGO CARD CCPYMT 026030004374949  
ACH DEBIT WELLS FARGO CARD CCPYMT 026056002545711  
ACH DEBIT WELLS FARGO CARD CCPYMT 026118009181435  

These strings contain:
- Volatile numeric identifiers
- Random formatting differences
- Extra text
- Inconsistent casing

This meant:
- Every month created new, unique merchant strings
- Categories had to be manually assigned repeatedly
- The system could not learn or generalize
- The category list became polluted with noise

### Core problem

The system tried to categorize transactions **before** identifying the merchant.

This is backwards.

---

# 2. Why We Are Changing It

The new system fixes the root cause: merchant identity resolution.

Instead of treating every raw merchant string as a unique business, Spening now:

- Normalizes raw merchant strings
- Maps them to canonical merchants (Aliases)
- Uses substring/regex rules to match future transactions
- Applies category overrides at the alias level
- Forces ambiguous merchants into **Unassigned** for manual review
- Completely ignores CSV/Plaid categories for categorization

This means:
- You only categorize a merchant once
- All future transactions auto‑categorize correctly
- Ambiguous merchants never get lost
- Noise and numeric identifiers no longer matter
- The system becomes scalable and predictable

This redesign transforms Spening from a basic importer into a professional‑grade financial ingestion engine.

---

# 3. New Architecture Overview

Spening now uses a multi‑layered system:

- **Aliases** — canonical merchant identities  
- **AliasPatterns** — substring/regex rules that map raw merchant strings to aliases  
- **RawBusinesses** — first‑seen unmapped merchant strings  
- **Category Overrides** — alias‑level category rules  
- **Normalization Pipeline** — transforms raw merchant strings into stable, comparable forms  
- **Transaction‑level categories** — the final truth  

CSV/Plaid categories are stored only for reference and **never** used for categorization.

---

# 4. Database Schema

## 4.1 Aliases
Represents a canonical merchant.

id (PK)  
alias_name (text)  
category_override_id (nullable FK → Categories)  
created_at  
updated_at  

## 4.2 AliasPatterns
Defines one or more match rules for each alias.

id (PK)  
alias_id (FK → Aliases.id)  
pattern (text)  
match_type (enum: contains, starts_with, regex)  
created_at  
updated_at  

## 4.3 RawBusinesses
Stores first‑seen unmapped merchant strings.

id (PK)  
raw_name_original (text)  
raw_name_normalized (text)  
csv_category_raw (nullable text, stored only for reference)  
category_from_first_transaction_id (nullable FK → Categories)  
is_mapped (bool)  
created_at  
updated_at  

## 4.4 RawBusinessAliasMap
Links raw businesses to aliases once mapped.

id (PK)  
raw_business_id (FK)  
alias_id (FK)  
created_at  

---

# 5. Normalization Pipeline

Normalization ensures consistent matching across noisy merchant strings.

### Steps

1. Trim whitespace  
2. Collapse multiple spaces  
3. Convert to uppercase  
4. Remove punctuation (- * . / :)  
5. Collapse spaces again  
6. Remove long numeric sequences (>6 digits)  
7. Optionally remove redundant parenthetical merchant names  

### Example

Raw:  
"ACH DEBIT  Wells Fargo Card Ccpymt 026030004374949 (Wells Fargo Card Ccpymt)"

Normalized:  
"ACH DEBIT WELLS FARGO CARD CCPYMT"

---

# 6. Matching Algorithm

## 6.1 On Import

1. Normalize raw_name  
2. Query AliasPatterns  
3. If any pattern matches:  
   - Assign alias  
   - Apply alias category override (if present)  
   - If alias has **no** category override → category = **Unassigned**  
   - Do NOT create RawBusiness  
4. If no pattern matches:  
   - Create RawBusiness  
   - Category = **Unassigned**  
   - Ignore CSV/Plaid category completely  

CSV/Plaid categories are stored only for reference and never used for categorization.

---

# 7. Category Resolution Priority (Final)

1. **Alias category override**  
2. **If alias exists but has no category override → Unassigned**  
3. **If no alias exists → Unassigned**  
4. **User manually assigns category** (saved to transaction)  

CSV/Plaid categories are never used.

---

# 8. Alias Management UI

## 8.1 Alias List Page

- List all aliases  
- Show category override  
- Show number of patterns  
- Show number of raw businesses mapped  

## 8.2 Alias Detail Page

- Alias name  
- Category override selector  
- List of patterns  
- Add/remove patterns  
- Pattern testing tool  

## 8.3 Pattern Testing Tool

User enters a raw merchant string → system:

- Normalizes it  
- Shows which patterns match  
- Shows which alias would be selected  
- Shows resulting category  

This prevents bad rules and helps tune patterns.

---

# 9. Raw Business Management UI

## 9.1 Unmapped Merchants Page

Shows only raw businesses where is_mapped = false.

Columns:
- raw_name_original  
- raw_name_normalized  
- csv_category_raw (reference only)  
- button: "Map to Alias"  

## 9.2 Mapping Workflow

User selects a raw business → chooses:

- Existing alias  
- OR create new alias  
- Add one or more patterns  
- Save → RawBusiness becomes mapped  

RawBusiness remains in DB for audit/debugging but is hidden from UI.

---

# 10. Retroactive Fixing

Because RawBusinesses are preserved:

- Adding new patterns can retroactively re‑categorize historical transactions  
- Alias category overrides can update past data  
- Debugging is possible because raw_name_normalized is stored  
- CSV/Plaid categories remain available for reference but never influence categorization  

---

# 11. Example Workflow

## 11.1 Importing a Wells Fargo Payment

Raw:  
ACH DEBIT WELLS FARGO CARD CCPYMT 026056002545711

Normalized:  
ACH DEBIT WELLS FARGO CARD CCPYMT

Patterns:
- WELLS FARGO CARD  
- CCPYMT  

Match → Alias: Wells Fargo Card Payment  
Category override → Transfers  
RawBusiness not created.

## 11.2 Importing a new unknown merchant

Raw:  
SQ *JOES COFFEE 12345

Normalized:  
SQ JOES COFFEE 12345

No patterns match → RawBusiness created.

Category = Unassigned  
CSV/Plaid category stored only as reference.

User later maps it to alias "Joe's Coffee" with patterns:
- JOES COFFEE  
- SQ JOES  

All future transactions auto‑map.

---

# 12. Summary

This system provides:

- Reliable merchant identity resolution  
- Scalable alias and pattern management  
- Automatic categorization  
- Retroactive correction capability  
- Clean UI for managing unknown merchants  
- Zero reliance on CSV/Plaid categories  
- A safe, predictable “Unassigned” workflow  

The redesign solves the core problem of inconsistent merchant strings and creates a foundation for a robust, future‑proof financial ingestion engine.
