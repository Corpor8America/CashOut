# Run Matching Specification
This document defines the behavior of the "Run Matching" operation in CashOut. Running matching applies all alias patterns to unmapped transactions, updates merchant associations, cleans up RawBusiness entries, and recalculates categories. This process is deterministic and idempotent.

---

# 1. Purpose
"Run Matching" allows the user to apply newly created or updated alias patterns to all existing transactions that are not currently assigned to an alias. This ensures that the system remains consistent after alias creation, alias editing, or alias deletion.

---

# 2. Transactions Eligible for Matching
Only transactions where:

alias_id IS NULL

These include:
- Newly imported transactions
- Transactions that previously belonged to a RawBusiness
- Transactions whose alias was deleted
- Transactions that were never matched before

Transactions that already have an alias_id are not reprocessed.

---

# 3. Normalization Step
Before matching, each eligible transaction's raw_name is normalized using the same normalization pipeline used during import:

- Convert to uppercase
- Trim leading/trailing whitespace
- Collapse multiple spaces
- Remove control characters
- Normalize punctuation

The result is stored in transaction.normalized_name.

Normalization ensures consistent and repeatable matching behavior.

---

# 4. Pattern Evaluation
Each alias has one or more AliasPatterns. For each eligible transaction:

1. Evaluate all AliasPatterns in deterministic order.
2. A pattern may be:
   - contains (substring match)
   - starts_with (prefix match)
   - regex (regular expression match)
3. The first alias whose pattern matches the transaction's normalized_name is selected.

If no patterns match, the transaction remains unmapped and keeps or creates a RawBusiness association.

---

# 5. Assigning Alias
If a pattern matches:

- transaction.alias_id is set to the alias's id
- transaction.raw_business_id is set to NULL
- The transaction is now considered aliased

Alias assignment always overrides RawBusiness membership.

---

# 6. RawBusiness Cleanup
After alias assignment:

1. Any RawBusiness that no longer has any transactions referencing it is deleted.
2. This keeps the "Unmapped Merchants" list clean and accurate.
3. RawBusiness entries only exist for merchants that currently have unmapped transactions.

---

# 7. Category Recalculation (Persisted Update)
After alias assignment, each transaction's category is **persistently updated** using the standard priority rules:

1. Alias category override (if present)
2. RawBusiness category (from first-seen transaction)
3. CSV/Plaid category
4. Unassigned

**This is not a display-only behavior.  
The transaction's stored category_id is updated in the database.**

This ensures:
- Reports use the correct category
- Historical data remains stable
- Manual recategorization remains possible
- Alias deletion does not erase category history

---

# 8. UI Updates
After matching completes:

- Transactions that matched an alias now display the alias name.
- Their categories reflect the updated, persisted category_id.
- RawBusiness entries with no remaining transactions disappear from the "Unmapped Merchants" list.
- The user sees an immediate reduction in unmapped merchants.

---

# 9. Idempotency
Running "Run Matching" multiple times without changing aliases or patterns produces the same result. This ensures predictable behavior and prevents accidental remapping.

---

# 10. Summary
"Run Matching" performs the following steps:

1. Identify all transactions without an alias_id.
2. Normalize their raw_name values.
3. Evaluate all alias patterns.
4. Assign alias_id when a match is found.
5. Remove raw_business_id for matched transactions.
6. Delete RawBusiness entries with no remaining transactions.
7. Persistently update transaction.category_id using priority rules.
8. Update the UI to reflect new mappings.

This operation ensures that CashOut remains consistent, predictable, and fully aligned with the user's alias definitions.

