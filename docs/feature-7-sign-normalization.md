# Spening — Universal Sign Normalization Specification
Authoritative rules for converting Plaid + CSV values into internal Credit/Debit fields.

## 1. Purpose
External data sources (CSV files, Plaid API, manual entry) use inconsistent sign conventions.  
Spening must normalize all transactions into a single internal representation:

- Credit → Money entering the account  
- Debit → Money leaving the account  

This document defines the exact rules for converting any external amount into this internal model.

## 2. Core Principle
The sign of the external amount is never trusted.  
All sources must be normalized using the same universal rule.

This removes inconsistencies between:
- checking vs credit card accounts  
- CSV vs Plaid  
- bank-specific conventions  
- reversed signs  
- ambiguous formats  

## 3. Internal Transaction Model (Target Format)

{
  "Credit": decimal?,   // money in
  "Debit": decimal?,    // money out
  "Amount": decimal     // Debit - Credit (always >= 0)
}

Rules:
- Exactly one of Credit or Debit must be non-null.
- Amount = Debit - Credit.
- Amount is always positive (absolute value).
- External sign conventions are discarded.

## 4. Universal Normalization Rule (applies to ALL sources)

If the external source provides a single Amount column:

If amount < 0:
    Credit = abs(amount)
    Debit = null
Else:
    Credit = null
    Debit = amount

This rule applies to:
- Plaid transactions
- CSVs with a single Amount column
- Manual entries

## 5. CSV Import Rules

### 5.1 CSVs with separate Credit and Debit columns
- If both columns contain values → skip row (invalid input)
- If only Credit has a value:
    Credit = parsed credit value
    Debit = null
- If only Debit has a value:
    Debit = parsed debit value
    Credit = null

### 5.2 CSVs with a single Amount column
Apply the universal normalization rule from Section 4.

## 6. Plaid Import Rules

Plaid’s amount field represents money leaving the account as a positive number.  
To unify behavior across all sources, Plaid transactions must follow the universal normalization rule:

If plaid.amount < 0:
    Credit = abs(plaid.amount)
    Debit = null
Else:
    Credit = null
    Debit = plaid.amount

Examples:
- Payroll deposit (Plaid negative) → Credit
- Mortgage payment (Plaid positive) → Debit
- Credit card payment (Plaid negative) → Credit
- Credit card purchase (Plaid positive) → Debit

## 7. Account Type Handling

AccountType (Checking, Savings, CreditCard):
- Used only for UI grouping and reporting.
- Never used to infer or modify sign logic.
- All accounts follow the same normalization rules.

## 8. Deduplication Impact

Because all sources normalize into the same Credit/Debit structure:
- CSV and Plaid transactions become comparable
- Sign mismatches no longer break dedup
- Amount comparisons are stable
- Hashing becomes deterministic

## 9. Examples

### Checking CSV (Amount column)
Raw Amount | Normalized Credit | Normalized Debit
1500.00    | null              | 1500.00
-1500.00   | 1500.00           | null

### Plaid Checking
Plaid Amount | Meaning           | Normalized
-2500.00     | Payroll deposit   | Credit = 2500
1800.00      | Mortgage payment  | Debit = 1800

### Credit Card CSV
Raw Amount | Meaning   | Normalized
-45.23     | Purchase  | Debit = 45.23
45.23      | Payment   | Credit = 45.23

## 10. Final Rule Summary (Agent Checklist)

1. Ignore external sign conventions.
2. Normalize all single-amount sources using the universal rule.
3. For CSVs with Credit/Debit columns, trust the column, not the sign.
4. Exactly one of Credit or Debit must be set.
5. AccountType never affects sign logic.
6. Amount = Debit - Credit.
