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

This means:
- You only categorize a merchant once
- All future transactions auto‑categorize
- Noise and numeric identifiers no longer matter
- The system becomes scalable and predictable

This redesign transforms Spening from a basic importer into a professional‑grade financial ingestion engine.

---

# 3. New Architecture Overview

Spening now uses a multi‑layered system:

- Aliases — canonical merchant identities
- AliasPatterns — substring/regex rules that map raw merchant strings to aliases
- RawBusinesses — first‑seen unmapped merchant strings
- Category Overrides — alias‑level category rules
- Normalization Pipeline — transforms raw merchant strings into stable, comparable forms

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
category_from_first_transaction_id (nullable FK → Categories)  
is_mapped (bool)  
created_at  
updated_at  

## 4.4 RawBusinessAliasMap
Links raw