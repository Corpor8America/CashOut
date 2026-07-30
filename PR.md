## Summary

Fix CSV-backfilled transactions not appearing on the linked accounts transactions page, and add pgAdmin to the dev docker-compose stack.

## Bug Fix: CSV Transactions Missing from Linked Account View

### Problem

When backfilling a linked account via CSV, the imported transactions never appeared on the linked accounts transactions page.

### Root Cause

`CsvImportService.Import()` correctly resolved the `LinkedAccount.Id` (Guid) to the Plaid `account_id` string via `resolvedAccountId`, but never used it. The raw Guid was stored in `Transaction.AccountId` instead.

The transactions page filters by the Plaid `account_id` string, so CSV-imported transactions (with a Guid as their `AccountId`) never matched the filter.

### Fix

- **`CsvImportService.cs:134`** — Dedup query now filters by `resolvedAccountId` instead of `accountId`
- **`CsvImportService.cs:258`** — New transactions are created with `AccountId = resolvedAccountId`

### One-time data fix for existing backfilled transactions

Previously backfilled transactions will still have the wrong `AccountId`. Run this SQL against the database:

```sql
UPDATE transactions t
SET account_id = la.account_id
FROM linked_accounts la
WHERE t.account_id = la.id::text
  AND t.source = 'CSV';
```

## New: pgAdmin in Dev Docker Compose

Added `pgadmin` service to `docker-compose.dev.yml` for convenient database inspection.

| Setting | Value |
|---------|-------|
| Image | `dpage/pgadmin4:latest` |
| URL | `http://localhost:5050` |
| Email | `admin@admin.com` |
| Password | Same as `DB_PASSWORD` from `.env` |

Depends on `db` being healthy before starting.
