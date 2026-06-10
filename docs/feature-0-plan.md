# Spening Feature Implementation Workflow  
## Execution Order & Grouped Feature Dependencies

This workflow defines the correct order for implementing the six Spening feature specifications.  
Features are grouped into phases based on dependency, shared context, and required sequencing.

---

# Phase 1 — Core Infrastructure & Configuration  
These features must be implemented first because all other systems depend on them.

## 1. Application Versioning  
File: `feature-1-application-version.md`

Purpose:  
- Establish a single authoritative version source  
- Enable CI version enforcement  
- Provide version visibility to backend and UI  

Why first:  
- CI/CD and Docker workflows depend on this  
- Ensures all future features ship with proper versioning  

---

## 2. Plaid Environment Variables  
File: `feature-2-plaid-variables.md`

Purpose:  
- Move Plaid secrets/config out of the database  
- Standardize environment‑based configuration  
- Ensure secure and consistent Plaid initialization  

Why second:  
- Required before implementing Linked Accounts or Plaid Sync  
- Ensures the backend starts with correct Plaid configuration  

---

# Phase 2 — Account Architecture  
These features define the structure of accounts and ingestion paths.

## 3. Account Types: Linked vs Manual  
File: `feature-3-accounttypes.md`

Purpose:  
- Establish two ingestion paths  
- Define CSV‑only vs Plaid‑connected behavior  
- Provide UI separation  

Why this phase:  
- CSV Import and Plaid Sync both depend on account type  
- Must exist before ingestion logic is implemented  

---

# Phase 3 — Data Normalization Layer  
This layer is required before any ingestion system (CSV or Plaid) can function correctly.

## 4. Business Normalization  
File: `feature-4-business-normalization.md`

Purpose:  
- Normalize raw merchant names  
- Provide aliasing and category override system  
- Ensure deterministic categorization  

Why now:  
- CSV Import and Plaid Sync both rely on RawBusinesses, Aliases, and category priority  
- Must be implemented before ingestion logic  

---

# Phase 4 — Ingestion Systems  
These features define how data enters the system.  
They depend on all previous phases.

## 5. CSV Import System  
File: `feature-5-csv-import.md`

Purpose:  
- Map CSV columns  
- Deduplicate rows  
- Handle credit/debit logic  
- Surface skipped rows  
- Maintain mapping profiles  

Dependencies:  
- Account Types (Manual vs Linked)  
- Business Normalization  
- Credit/Debit schema  

Why before Plaid Sync:  
- CSV import is simpler  
- Helps validate normalization and transaction schema  

---

## 6. Plaid Sync System  
File: `feature-6-plaid-sync.md`

Purpose:  
- Incremental Plaid sync  
- Pending→posted transitions  
- Add/update/remove logic  
- Cursor handling  
- CSV protection  
- Business normalization integration  

Dependencies:  
- Plaid environment variables  
- Account Types  
- Business Normalization  
- CSV Import (for shared transaction model)  

Why last:  
- Most complex ingestion path  
- Requires all foundational systems to be in place  

---

# Summary Table

| Phase | Feature | File |
|-------|---------|------|
| 1 | Application Versioning | feature-1-application-version.md |
| 1 | Plaid Environment Variables | feature-2-plaid-variables.md |
| 2 | Account Types | feature-3-accounttypes.md |
| 3 | Business Normalization | feature-4-business-normalization.md |
| 4 | CSV Import System | feature-5-csv-import.md |
| 4 | Plaid Sync System | feature-6-plaid-sync.md |

---

# Execution Workflow (Agent‑Friendly)

1. **Initialize core infrastructure**  
   - Implement versioning  
   - Implement Plaid environment variable loading  

2. **Define account architecture**  
   - Add Linked + Manual account types  
   - Update UI navigation  

3. **Implement normalization layer**  
   - RawBusinesses  
   - Aliases  
   - RawBusinessAliasMap  
   - Category priority logic  

4. **Implement ingestion systems**  
   - CSV Import (mapping, dedup, skipped rows, profiles)  
   - Plaid Sync (cursor, add/update/remove, pending→posted)  

5. **Verify integration**  
   - Ensure CSV and Plaid both feed into normalization  
   - Ensure account types enforce correct ingestion rules  
   - Ensure versioning and environment variables are respected  

