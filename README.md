# Spening

A self-hosted personal finance tracker that connects to your bank accounts via
[Plaid](https://plaid.com) and shows your spending in a simple web interface.

## Features
- Link bank/credit card accounts via Plaid
- Incremental transaction sync (cursor-based)
- Full year re-fetch
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
| `ENCRYPTION_KEY` | Yes | `openssl rand -base64 32` |
| `DB_PASSWORD` | Yes | PostgreSQL password |

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
