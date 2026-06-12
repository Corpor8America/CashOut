# Spening

A self-hosted personal finance tracker that connects to your bank accounts via
[Plaid](https://plaid.com) and shows your spending in a simple web interface.

## Features
- Link bank/credit card accounts via Plaid
- Incremental transaction sync (cursor-based)
- Full year re-fetch
- CSV import with configurable column mapping
- **Merchant normalization** — alias patterns that auto-categorize messy merchant strings
- Reports: monthly totals, by category, pivot, top merchants, largest transactions
- CSV export

## Quick Start (homelab)

1. Copy `docker-compose.yml` and `.env.example` to your server
2. Rename `.env.example` to `.env` and fill in your credentials
3. Run `docker compose up -d`
4. Open `http://<server-ip>:8080`

## Environment Variables

| Variable | Required | Description |
|---|---|---|
| `PLAID_CLIENT_ID` | Yes | Plaid dashboard client ID |
| `PLAID_SANDBOX_SECRET` | Yes | Plaid sandbox secret |
| `PLAID_PRODUCTION_SECRET` | No | Plaid production secret |
| `PLAID_ENV` | No | `sandbox` (default) or `production` |
| `ENCRYPTION_KEY` | Yes | `openssl rand -base64 32` |
| `DB_PASSWORD` | Yes | PostgreSQL password |

## Merchant Normalization

Spening normalizes inconsistent merchant strings (e.g. `AMZN MKTP US 123456789`) into canonical
merchant identities called **Aliases**. Go to **Merchants & Aliases** in the sidebar to:

1. **Create an alias** — e.g. "Amazon" with category "SHOPPING"
2. **Add patterns** — `Contains: AMAZON`, `StartsWith: AMZN`, or a Regex
3. **Test patterns** — paste any raw merchant string to preview how it resolves
4. **Review unmapped merchants** — merchants that didn't match any pattern land here

During import (Plaid sync or CSV), the pipeline:
1. Normalizes the raw merchant string (uppercase, strips punctuation and long numbers)
2. Matches against your alias patterns (lowest alias ID wins on ties)
3. Uses the alias category — or **Unassigned** if no alias matched or the alias has no category

CSV/Plaid categories are stored for reference only and never influence categorization.

After adding new patterns, click **Re-run Pattern Matching** on the Unmapped tab to retroactively
map existing raw merchants.

## Development

```bash
cp .env.example .env   # fill in values
docker compose -f docker-compose.dev.yml up -d
cd Spening
dotnet run
```

## Updating

```bash
docker compose pull
docker compose up -d
```

### Local update
```bash
docker compose -f docker-compose.dev.yml up -d --build
```