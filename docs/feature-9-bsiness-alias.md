# Merchant Aliasing, Raw Businesses, and Alias Deletion Behavior
This document defines how CashOut handles merchant identity, aliasing, raw business storage, and what happens when an alias is deleted. This is the authoritative specification for the merchant normalization pipeline.

---

# 1. Overview
CashOut uses a three-layer merchant identity system:

1. RawBusiness  
   - Stores the exact merchant string from the transaction source.  
   - Created only when a transaction does not match any alias pattern.  
   - Represents a "first-seen" merchant identity.

2. Alias  
   - Represents a canonical merchant (e.g., "Amazon", "Wells Fargo Card Payment").  
   - May optionally define a category override.  
   - Has one or more patterns used to match future transactions.  
   - **Alias names must be editable.**  
     Editing an alias name does not affect patterns, categories, or linked transactions.

3. AliasPattern  
   - A substring or regex rule that determines whether a transaction should be auto-mapped to an alias.  
   - If a transaction matches a pattern, it is assigned to the alias and no RawBusiness entry is created.

---

# 2. Raw Business Name Rules
RawBusiness.raw_name must always store the exact merchant string as provided by the transaction source (Plaid or CSV). This value is never normalized, trimmed, uppercased, or modified.

Examples:
"ACH DEBIT WELLS FARGO CARD CCPYMT 026030004374949"
"AMZN MKTP US*2F93L3KJ2"
"SQ *SUNNY COFFEE 980123"
"TARGET T1234 00012345"

The RawBusiness table also stores:
- normalized_name: a cleaned version used for matching
- category_id: category from the first transaction that created this RawBusiness

Normalization is used only for matching, not for storage.

---

# 3. Transaction Storage Rules
Every transaction always stores:
- raw_name: exact merchant string from Plaid/CSV
- normalized_name: normalized version
- alias_id: nullable
- raw_business_id: nullable

Even if a transaction is auto-aliased, raw_name and normalized_name are still stored on the transaction itself. This ensures the system can reconstruct RawBusiness entries later if needed.

---

# 4. Alias Matching Pipeline
When a new transaction is imported:

1. Normalize the transaction's raw_name.
2. Attempt to match against AliasPatterns.
3. If a pattern matches:
   - The transaction is assigned alias_id.
   - The alias's category override (if any) is applied.
   - No RawBusiness entry is created.
4. If no pattern matches:
   - A new RawBusiness entry is created.
   - The transaction is linked to that RawBusiness.
   - The RawBusiness category is set from the first transaction.

---

# 5. Category Assignment Priority
When determining a transaction's category:

1. Alias category override (highest priority)
2. RawBusiness category (from first-seen transaction)
3. CSV/Plaid category
4. Unassigned

Alias category always supersedes RawBusiness category.

---

# 6. What Happens When an Alias Is Deleted
Deleting an alias removes:
- The alias record
- All AliasPatterns associated with it

This triggers a full reprocessing of all transactions that referenced that alias.

For each affected transaction:

1. alias_id is set to null.
2. The merchant resolution pipeline is re-run.
3. Because the alias no longer exists, the transaction will not match any alias pattern.
4. The system attempts to find an existing RawBusiness with the same normalized_name.
5. If none exists, a new RawBusiness entry is created using:
   - raw_name = transaction.raw_name
   - normalized_name = transaction.normalized_name
   - category = transaction.category (first-seen rule)
6. Category is reassigned using the standard priority rules.

This effectively returns the transaction to the same state as if CashOut had never seen that merchant before.

---

# 7. Why RawBusiness Must Be Reconstructed After Alias Deletion
Transactions that were auto-aliased never created RawBusiness entries originally. After alias deletion, these transactions need a RawBusiness entry so that:

- The merchant appears in the "Unmapped Merchants" UI.
- The user can remap it or create a new alias.
- Category fallback logic works correctly.
- Future transactions with the same merchant string behave consistently.

The transaction's own raw_name is the source of truth for reconstructing RawBusiness.

---

# 8. Alias Name Editing
Alias names must be editable at any time.

Editing an alias name:
- Does not affect existing patterns.
- Does not affect category overrides.
- Does not change any transaction mappings.
- Does not trigger reprocessing.
- Only updates the canonical display name for that merchant.

This allows users to refine or correct merchant names without disrupting the underlying mapping logic.

---

# 9. Summary
- RawBusiness stores the exact merchant string from the transaction source.
- AliasPatterns determine whether a transaction is auto-mapped.
- Auto-mapped transactions do not create RawBusiness entries.
- Every transaction always stores its raw_name and normalized_name.
- Deleting an alias forces all affected transactions to be reprocessed.
- RawBusiness entries are created retroactively when needed.
- Deleted aliases cause merchants to reappear in the "Unmapped" list.
- Alias names are fully editable without affecting mappings or categories.

This system ensures that CashOut remains debuggable, auditable, and capable of retroactive fixes without losing historical merchant identity.

