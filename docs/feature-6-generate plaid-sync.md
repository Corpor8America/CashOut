# Plaid Sync System  
**Spening — Transaction Synchronization Specification**

This document defines how Spening synchronizes transactions from Plaid, including cursor handling, add/update/delete logic, pending-to-posted transitions, duplicate prevention, CSV protection, and account re-linking behavior.

---

## 1. Objective

Provide a deterministic, safe, and predictable Plaid sync process that:

- Imports new Plaid transactions  
- Updates existing Plaid transactions  
- Removes Plaid transactions that no longer exist  
- Correctly handles pending-to-posted transitions  
- Never touches CSV transactions  
- Avoids duplicates  
- Survives account re-linking  
- Maintains a stable sync cursor  
- Preserves user edits and business normalization rules  

---

## 2. Data Model Requirements

Each Plaid transaction stores:

- plaid_transaction_id  
- plaid_pending_transaction_id (nullable)  
- account_id  
- source = Plaid  
- date  
- name  
- credit or debit (positive values)  
- raw_business_id  
- alias_id (nullable)  
- category_id (nullable)  
- plaid_cursor (stored per account)

CSV transactions store:

- source = CSV  
- No Plaid IDs  
- Never modified by Plaid sync  

---

## 3. Sync Flow Overview

1. Load stored Plaid cursor for the account.  
2. Call Plaid `/transactions/sync` with the cursor.  
3. Process added, modified, and removed transactions.  
4. Update the cursor.  
5. Save all changes atomically.

The sync is incremental and idempotent.

---

## 4. Cursor Handling

### Normal Operation
When Plaid returns a new cursor, save it.

### Invalid Cursor
If Plaid returns `INVALID_CURSOR`:

- Reset cursor to null  
- Perform a full historical sync  
- Rebuild Plaid transaction state  

This handles account re-linking, token rotation, long sync gaps, and Plaid internal resets.

---

## 5. Adding New Transactions

For each transaction in `added`:

1. Check if `plaid_transaction_id` already exists.  
2. If not:
   - Insert new transaction  
   - Set `source = Plaid`  
   - Map amount to credit or debit  
   - Create or reuse RawBusiness  
   - Apply alias if exists  
   - Assign category using category priority rules  
3. If found:
   - Skip (duplicate prevention)

---

## 6. Updating Existing Transactions

For each transaction in `modified`:

1. Find by `plaid_transaction_id`.  
2. Update:
   - date  
   - name  
   - amount (credit or debit)  
   - pending_transaction_id  
   - Plaid merchant/category (initialization only)  
3. Do **not** overwrite:
   - alias_id  
   - user-edited category  
   - user-edited business mappings  

---

## 7. Removing Transactions

For each transaction in `removed`:

1. Find by `plaid_transaction_id`.  
2. Delete only if `source = Plaid`.  
3. Never delete CSV transactions.

This protects historical backfill.

---

## 8. Pending → Posted Transition

Plaid sends a pending transaction first, then a posted transaction with `pending_transaction_id`.

### Matching Logic

If `pending_transaction_id` matches an existing pending transaction:

- Update the pending transaction in place  
- Replace Plaid IDs  
- Replace amount, date, and name  
- Preserve user edits  

Otherwise:

- Insert as new

### Why In‑Place Update?

- Prevents duplicates  
- Preserves dedup  
- Preserves business normalization  
- Preserves user category overrides  

---

## 9. Duplicate Prevention

A transaction is a duplicate if:

- `plaid_transaction_id` already exists  
**or**  
- `pending_transaction_id` matches an existing pending transaction  

Duplicates are ignored.

---

## 10. CSV Protection Rules

Plaid sync must **never**:

- Modify CSV transactions  
- Delete CSV transactions  
- Merge CSV and Plaid transactions  
- Deduplicate across CSV and Plaid  
- Attempt to “fix” CSV data  

CSV is authoritative historical backfill.

---

## 11. Business Normalization Interaction

When adding a Plaid transaction:

1. Create or reuse RawBusiness using Plaid merchant name.  
2. If RawBusiness has no category:
   - Initialize using Plaid category.  
3. If RawBusiness already has a category:
   - Do not overwrite it.  
4. If an alias exists:
   - Alias category overrides raw business category.  

---

## 12. Category Priority Rules

Category priority:

1. Alias category  
2. Raw business category  
3. Plaid category (initialization only)  
4. None  

Plaid sync must **not** override user-controlled categories.

---

## 13. Account Re‑Linking Behavior

When a user re-links an account:

- Plaid may issue a new access token  
- Plaid may invalidate the cursor  
- Plaid may resend historical transactions  

### Required Behavior

If cursor is invalid:

- Reset cursor  
- Full resync  
- Match existing transactions by Plaid IDs  
- Do not duplicate  
- Do not delete CSV transactions  

---

## 14. Error Handling

### Non‑fatal Errors

- Network issues  
- Temporary Plaid outages  
- Rate limits  

Behavior:

- Abort sync  
- Do not update cursor  
- No partial writes  

### Fatal Errors

- INVALID_CURSOR  
- Access token revoked  
- Account removed  

Behavior:

- Reset cursor  
- Full resync next attempt  

---

## 15. Safety Guarantees

The Plaid sync system guarantees:

- No silent corruption  
- No accidental deletion of CSV data  
- No duplicate Plaid transactions  
- No loss of user edits  
- No incorrect category overrides  
- No incorrect business mappings  
- Full recovery from cursor invalidation  
- Full recovery from account re-linking  

---

## 16. Summary

| Feature | Behavior |
|--------|----------|
| Incremental sync | Uses Plaid cursor |
| Pending→posted | In‑place update |
| Add/update/delete | Based on Plaid IDs |
| CSV protection | Never touched |
| Duplicate prevention | ID‑based |
| Business normalization | RawBusiness + Alias rules |
| Category priority | Alias → Raw → Plaid |
| Cursor invalidation | Full resync |
| Re‑linking | Safe, non‑duplicating |
| User edits | Always preserved |
