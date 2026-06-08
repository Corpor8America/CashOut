# Phase 7 — Dockerfile, Docker Compose & GitHub Actions CI/CD

## Progress Tracker

- [ ] 7.1 Create `Spening/Dockerfile` (multi-stage build)
- [ ] 7.2 Create `docker-compose.yml` (production — pulls from Docker Hub)
- [ ] 7.3 Update `docker-compose.dev.yml` (already exists from Phase 1 — verify it matches)
- [ ] 7.4 Create `.env.example` (repository root)
- [ ] 7.5 Create `.github/workflows/docker-publish.yml`
- [ ] 7.6 Add GitHub repository secrets
- [ ] 7.7 Create `README.md`
- [ ] 7.8 Verify local Docker build
- [ ] 7.9 Verify GitHub Actions publishes image to Docker Hub

---

## Context

This phase packages the application into a Docker image and sets up automated publishing to Docker
Hub via GitHub Actions. After this phase, deploying to a homelab is a two-file operation:
copy `docker-compose.yml` and `.env` to the server, then run `docker compose up -d`.

---

## Task 7.1 — Dockerfile

Create `Spening/Dockerfile`. Multi-stage build: the `build` stage compiles and publishes; the
`runtime` stage is the lean final image. The build stage uses the full SDK image; the runtime
stage uses the smaller ASP.NET runtime image.

```dockerfile
# ── Stage 1: Build ────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj and restore first — Docker layer cache means this only
# re-runs when the .csproj changes, not on every code change.
COPY Spening.csproj ./
RUN dotnet restore

# Copy all source and publish
COPY . ./
RUN dotnet publish Spening.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore

# ── Stage 2: Runtime ──────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Non-root user for security
RUN adduser --disabled-password --no-create-home appuser
USER appuser

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "Spening.dll"]
```

> The `COPY` in the build stage uses a path relative to the build context. When building from the
> repository root with `context: .` in docker-compose, the path is `Spening/Spening.csproj`.
> When building from inside `Spening/` with `docker build .`, it is `Spening.csproj`. The
> compose files below set context to `.` (repo root) and dockerfile to `Spening/Dockerfile`.

---

## Task 7.2 — docker-compose.yml (Production)

Create at repository root. This is the file used on the homelab server. It pulls the published
image from Docker Hub — no source code needed on the server.

```yaml
version: "3.9"

services:
  db:
    image: postgres:16-alpine
    restart: unless-stopped
    environment:
      POSTGRES_DB: spening
      POSTGRES_USER: spening
      POSTGRES_PASSWORD: ${DB_PASSWORD}
    volumes:
      - pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U spening"]
      interval: 5s
      timeout: 5s
      retries: 5

  app:
    image: yourusername/spening:latest   # ← replace yourusername with your Docker Hub username
    restart: unless-stopped
    depends_on:
      db:
        condition: service_healthy
    environment:
      - PLAID_CLIENT_ID=${PLAID_CLIENT_ID}
      - PLAID_SANDBOX_SECRET=${PLAID_SANDBOX_SECRET}
      - PLAID_PRODUCTION_SECRET=${PLAID_PRODUCTION_SECRET}
      - ENCRYPTION_KEY=${ENCRYPTION_KEY}
      - ConnectionStrings__Default=Host=db;Database=spening;Username=spening;Password=${DB_PASSWORD}
    ports:
      - "8080:8080"

volumes:
  pgdata:
```

**To deploy on the homelab:**
```bash
# Copy docker-compose.yml and .env to the server, then:
docker compose pull
docker compose up -d
```

**To update to a new version:**
```bash
docker compose pull
docker compose up -d
```

---

## Task 7.3 — docker-compose.dev.yml (Verify)

This file was created in Phase 1. Verify it matches the following (update if needed):

```yaml
version: "3.9"

services:
  db:
    image: postgres:16-alpine
    restart: unless-stopped
    environment:
      POSTGRES_DB: spening
      POSTGRES_USER: spening
      POSTGRES_PASSWORD: ${DB_PASSWORD}
    volumes:
      - pgdata_dev:/var/lib/postgresql/data
    ports:
      - "5432:5432"   # exposed for local DB tooling
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U spening"]
      interval: 5s
      timeout: 5s
      retries: 5

  app:
    build:
      context: .
      dockerfile: Spening/Dockerfile
    restart: unless-stopped
    depends_on:
      db:
        condition: service_healthy
    environment:
      - PLAID_CLIENT_ID=${PLAID_CLIENT_ID}
      - PLAID_SANDBOX_SECRET=${PLAID_SANDBOX_SECRET}
      - PLAID_PRODUCTION_SECRET=${PLAID_PRODUCTION_SECRET}
      - ENCRYPTION_KEY=${ENCRYPTION_KEY}
      - ConnectionStrings__Default=Host=db;Database=spening;Username=spening;Password=${DB_PASSWORD}
      - ASPNETCORE_ENVIRONMENT=Development
    ports:
      - "8080:8080"

volumes:
  pgdata_dev:
```

---

## Task 7.4 — .env.example

Create at repository root (committed to git — no real values):

```bash
# Plaid credentials — get these from https://dashboard.plaid.com
PLAID_CLIENT_ID=your_client_id_here
PLAID_SANDBOX_SECRET=your_sandbox_secret_here
PLAID_PRODUCTION_SECRET=your_production_secret_here

# 32-byte base64-encoded key for AES-256 access token encryption
# Generate with: openssl rand -base64 32
ENCRYPTION_KEY=base64_encoded_32_byte_key_here

# PostgreSQL password
DB_PASSWORD=changeme_use_a_strong_password
```

---

## Task 7.5 — GitHub Actions Workflow

Create `.github/workflows/docker-publish.yml`:

```yaml
name: Build & Publish Docker Image

on:
  push:
    branches:
      - main          # pushes to main → :latest tag
    tags:
      - "v*.*.*"      # version tags → semver tags (v1.2.3 → :v1.2.3 and :1.2)

jobs:
  build-and-push:
    runs-on: ubuntu-latest

    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Set up Docker Buildx
        uses: docker/setup-buildx-action@v3

      - name: Log in to Docker Hub
        uses: docker/login-action@v3
        with:
          username: ${{ secrets.DOCKERHUB_USERNAME }}
          password: ${{ secrets.DOCKERHUB_TOKEN }}

      - name: Extract metadata (tags and labels)
        id: meta
        uses: docker/metadata-action@v5
        with:
          images: ${{ secrets.DOCKERHUB_USERNAME }}/spening
          tags: |
            type=raw,value=latest,enable=${{ github.ref == 'refs/heads/main' }}
            type=semver,pattern={{version}}
            type=semver,pattern={{major}}.{{minor}}

      - name: Build and push
        uses: docker/build-push-action@v5
        with:
          context: .
          file: ./Spening/Dockerfile
          push: true
          tags: ${{ steps.meta.outputs.tags }}
          labels: ${{ steps.meta.outputs.labels }}
          cache-from: type=gha
          cache-to: type=gha,mode=max
```

### How Tags Work

| Event | Docker Hub tags produced |
|---|---|
| Push to `main` | `yourusername/spening:latest` |
| Push tag `v1.0.0` | `yourusername/spening:v1.0.0`, `yourusername/spening:1.0` |
| Push tag `v2.1.3` | `yourusername/spening:v2.1.3`, `yourusername/spening:2.1` |

---

## Task 7.6 — GitHub Repository Secrets

In the GitHub repository, go to **Settings → Secrets and variables → Actions → New repository secret**
and add:

| Secret name | Value |
|---|---|
| `DOCKERHUB_USERNAME` | Your Docker Hub username (e.g. `johndoe`) |
| `DOCKERHUB_TOKEN` | A Docker Hub **access token** (not your password) — create at https://hub.docker.com/settings/security |

These are referenced in the workflow as `${{ secrets.DOCKERHUB_USERNAME }}` and
`${{ secrets.DOCKERHUB_TOKEN }}`.

---

## Task 7.7 — README.md

Create `README.md` at repository root:

```markdown
# Spening

A self-hosted personal finance tracker that connects to your bank accounts via
[Plaid](https://plaid.com) and shows your spening in a simple web interface.

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
docker compose -f docker-compose.dev.yml up db -d
cd Spening
dotnet run
```

## Updating

```bash
docker compose pull
docker compose up -d
```
```

---

## Task 7.8 — Verify Local Docker Build

From the repository root:

```bash
# Build the image locally
docker build -f Spening/Dockerfile -t spening:local .

# Start with dev compose (builds from source)
docker compose -f docker-compose.dev.yml up --build
```

Navigate to `http://localhost:8080` and verify the app starts, connects to the DB, and the UI loads.

Common issues:
- **Migration error on startup** — ensure the DB container is healthy before the app starts.
  The `depends_on: condition: service_healthy` in compose handles this.
- **Missing env var** — verify `.env` is in the repository root and all required keys are set.

---

## Task 7.9 — Verify GitHub Actions Publishes to Docker Hub

```bash
# Push to main to trigger :latest build
git add .
git commit -m "feat: initial implementation"
git push origin main

# Then check GitHub Actions tab — the workflow should appear under "Actions"
# After it succeeds, verify on Docker Hub:
# https://hub.docker.com/r/yourusername/spening/tags

# To publish a versioned release:
git tag v1.0.0
git push origin v1.0.0
# → produces :v1.0.0 and :1.0 tags on Docker Hub
```

---

## Implementation Complete

All seven phases are done. The application is:
- Fully functional in a local browser (`dotnet run`)
- Dockerized and runnable via `docker compose`
- Automatically published to Docker Hub on every push to `main` and every version tag
- Ready to deploy on a homelab with `docker compose pull && docker compose up -d`

### Final File Checklist

```
spening/
├── .github/workflows/docker-publish.yml  ✓
├── .gitignore                             ✓
├── .env.example                           ✓
├── docker-compose.yml                     ✓
├── docker-compose.dev.yml                 ✓
├── README.md                              ✓
└── Spening/
    ├── Controllers/
    │   ├── AccountsController.cs          ✓
    │   ├── PlaidLinkController.cs         ✓
    │   ├── TransactionsController.cs      ✓
    │   ├── ReportsController.cs           ✓
    │   └── SettingsController.cs          ✓
    ├── Data/
    │   ├── AppDbContext.cs                ✓
    │   └── Migrations/                    ✓
    ├── Models/
    │   ├── LinkedAccount.cs               ✓
    │   ├── Transaction.cs                 ✓
    │   └── AppSetting.cs                  ✓
    ├── Pages/
    │   ├── _Host.cshtml                   ✓
    │   ├── Accounts.razor                 ✓
    │   ├── Transactions.razor             ✓
    │   ├── Reports.razor                  ✓
    │   └── Settings.razor                 ✓
    ├── Services/
    │   ├── EncryptionService.cs           ✓
    │   ├── PlaidService.cs                ✓
    │   ├── SettingsService.cs             ✓
    │   ├── TransactionService.cs          ✓
    │   └── ReportService.cs               ✓
    ├── Shared/
    │   └── MainLayout.razor               ✓
    ├── wwwroot/
    │   ├── app.css                        ✓
    │   └── plaidLink.js                   ✓
    ├── App.razor                          ✓
    ├── appsettings.json                   ✓
    ├── Dockerfile                         ✓
    ├── Program.cs                         ✓
    └── Spening.csproj                    ✓
```
