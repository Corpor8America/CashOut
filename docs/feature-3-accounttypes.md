# Account Types: Linked vs Manual

## Overview
Spening supports two distinct account types, each with its own ingestion path and behavior. This separation ensures clean data handling, predictable sync behavior, and a clear user experience.

## Objective
Provide first‑class support for two account types:

- **Linked Accounts** (Plaid‑connected)
- **Manual Accounts** (CSV‑only)

Each type has different ingestion rules, sync behavior, and UI flows.

---

## Linked Accounts

Linked Accounts are connected through Plaid and support automated transaction synchronization.

### Characteristics
- Connected via Plaid Link.
- Support automated, incremental Plaid sync.
- May accept CSV imports **only for historical backfill**.

### CSV Backfill Rules
Once Plaid sync is active:

- CSV imports do **not** override Plaid data.
- CSV transactions do **not** participate in Plaid add/update/remove logic.
- CSV transactions are treated as separate, user‑managed historical data.
- CSV and Plaid transactions remain isolated in terms of deduplication and sync behavior.

This ensures Plaid remains the authoritative source for ongoing activity.

---

## Manual Accounts

Manual Accounts have no Plaid connection and rely entirely on CSV imports.

### Characteristics
- No Plaid integration.
- All transactions come from CSV uploads.
- Ideal for:
  - Cash accounts  
  - Unsupported institutions  
  - Historical data imports  
  - Offline accounts  

Manual Accounts use the same CSV importer and mapping system as Linked Accounts, but without any Plaid sync logic.

---

## UI Changes

Add two dedicated menu items:

- **Linked Accounts**
- **Manual Accounts**

Each menu item leads to its own management screen, ensuring:

- Clear separation of account types  
- Predictable user flows  
- Reduced confusion between Plaid‑connected and CSV‑only accounts  

