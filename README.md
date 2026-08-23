# CashOut

A self-hosted personal finance tracker that imports bank transactions via CSV
and displays spending in a simple web interface.

## Features

- CSV import with configurable column mapping
- PDF import for statement files
- Reports: monthly totals, by category, cash flow
- CSV export

## Quick Start (homelab)

1. Copy `docker-compose.yml` and `.env.example` to your server
2. Rename `.env.example` to `.env` and fill in your credentials
3. Run `docker compose up -d`
4. Open `http://<server-ip>:8080`

## Environment Variables

| Variable | Required | Description |
|---|---|---|
| `DB_PASSWORD` | Yes | PostgreSQL password |

## Development

```bash
cp .env.example .env   # fill in values
docker-compose -f docker-compose.dev.yml up -d
cd CashOut
dotnet run
```

## Updating

```bash
docker-compose -f docker-compose.dev.yml up -d --build
```
