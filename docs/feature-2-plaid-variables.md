# Plaid Configuration via Environment Variables

## Overview
This feature removes all Plaid configuration values from the database and centralizes them in environment variables. This ensures secure handling of secrets, simplifies deployment, and guarantees consistent configuration across environments.

## Objective
Eliminate Plaid settings from the `AppSettings` table and rely exclusively on environment variables for all Plaid configuration.

## Required Environment Variables

The backend must read the following variables at startup:

- `PLAID_CLIENT_ID`
- `PLAID_SECRET`
- `PLAID_ENV`  
  Accepted values: `sandbox`, `development`, `production`
- `PLAID_REDIRECT_URI` (optional)
- `PLAID_PRODUCTS` (optional)

These variables form the complete configuration required to initialize the Plaid client.

## Implementation

### Backend Behavior
- All Plaid configuration is loaded from environment variables during application startup.
- The `AppSettings` table no longer stores:
  - Plaid client ID  
  - Plaid secret  
  - Plaid environment  
  - Any Plaid-related configuration  
- Missing required environment variables should cause the application to fail fast with a clear error.

### Database Behavior
- Any existing Plaid-related rows in the `AppSettings` table are deprecated and ignored.
- Future migrations should remove Plaid configuration fields entirely.

### Admin UI Behavior
- The Admin UI may display Plaid configuration values in **read‑only** mode for visibility.
- Secrets (client ID, secret) must never be shown in plaintext.

