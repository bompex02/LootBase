# LootBase

![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![Nuxt](https://img.shields.io/badge/Nuxt-4-00DC82?logo=nuxt&logoColor=white)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-18-4169E1?logo=postgresql&logoColor=white)
![TypeScript](https://img.shields.io/badge/TypeScript-6-3178C6?logo=typescript&logoColor=white)

LootBase is a full-stack web app for comparing Counter-Strike 2 inventory values. Users sign in via Steam, sync their public CS2 inventory, and appear on a leaderboard ranked by market value.

Currently CS2 only. Additional games and pricing sources can be plugged in via the provider architecture.

## Features

- Steam login via OpenID 2.0
- Fetches public CS2 inventories from Steam Community Inventory
- Pricing via the Skinport API with a Redis cache (15-minute TTL)
- Player profile page: inventory value, item count, top items
- Leaderboard sorted by inventory value
- PostgreSQL + EF Core with auto-applied migrations
- EF InMemory fallback for local development (no database needed)
- Nuxt frontend with server-side rendering and cookie forwarding

## Architecture

```text
Nuxt Frontend
    |
    | HTTP / HttpOnly Cookie
    v
ASP.NET Core Minimal API
    |
    +-- Auth Endpoints
    +-- Player Endpoints
    +-- Leaderboard Endpoints
    +-- Pricing Endpoints
    +-- Items Endpoints
    |
    v
Application Layer (Services, DTOs, Interfaces)
    |
    v
Infrastructure Layer (Steam, Skinport, EF Core, Redis)
    |
    v
PostgreSQL 18  or  EF InMemory
Redis 8        or  In-process Distributed Cache
```

## Tech Stack

| Area | Technology |
| --- | --- |
| Backend | .NET 10, ASP.NET Core Minimal API |
| ORM | Entity Framework Core 10 |
| Database | PostgreSQL 18 or EF InMemory |
| Cache | Redis 8 or Distributed Memory Cache |
| Auth | Steam OpenID 2.0, ASP.NET Cookie Auth |
| Frontend | Nuxt 4, Vue 3, TypeScript |
| UI | Nuxt UI 4, Tailwind CSS 4, Nuxt Icon |
| Pricing | Skinport API |
| Inventory | Steam Community Inventory API |

## Project Structure

```text
.
├── backend/
│   └── src/
│       ├── LootBase.Api/             # API entry point, endpoints, auth
│       ├── LootBase.Application/     # Services, DTOs, interfaces
│       ├── LootBase.Domain/          # Entities and constants
│       └── LootBase.Infrastructure/  # EF Core, Steam, Skinport, Redis
├── frontend/                         # Nuxt 4 app
├── docker-compose.yml                # Full stack: frontend, backend, PostgreSQL, Redis
├── .env.example                      # Environment variable template
├── dev.sh                            # Option B: run backend + frontend without Docker
├── Directory.Packages.props          # Centralised NuGet versions
└── LootBase.sln
```

## Prerequisites

- Docker and Docker Compose **or** .NET SDK 10 + Node.js 22 (for manual dev)
- A public Steam inventory (required for inventory sync to work)

To make your Steam inventory public:
```
Steam Profile → Edit Profile → Privacy Settings → Inventory → Public
```

## Running Locally

### Option A — Docker Compose (recommended)

Runs everything (frontend, backend, PostgreSQL, Redis) in containers with a single command.

```bash
cp .env.example .env
# Add your Steam__WebApiKey to .env
docker compose up -d --build
```

Default URLs:

| App | URL |
| --- | --- |
| Frontend | `http://localhost:3000` |
| Backend API | `http://localhost:5188` |

### Option B — Manual dev (fast iteration, no Docker)

No Postgres or Redis needed — the backend falls back to an in-memory database and in-memory cache automatically.

```bash
./dev.sh
```

This script:
1. Frees ports 5188 and 3000 if already in use
2. Copies `frontend/.env.example` → `frontend/.env` on first run
3. Installs npm dependencies if missing
4. Starts the backend in the background (`http://localhost:5188`)
5. Starts the Nuxt dev server in the foreground (`http://localhost:3000`)

Press **Ctrl+C** to stop both processes.

> **Two gotchas:** Steam login requires `Steam__Realm`, `Steam__ReturnUrl`, `Steam__FrontendBaseUrl`, and `Cors__AllowedOrigins` to point at the correct ports. If you change ports, update all four together or login will silently break. Also, EF Core migrations run automatically on backend startup when a real Postgres connection string is present — you never need to run `database update` manually in Docker.

## Configuration

Runtime values live in `.env` (copied from `.env.example`). Docker Compose injects them as environment variables; the backend reads them as `IConfiguration` keys.

| Variable | Description |
| --- | --- |
| `ConnectionStrings__LootBase` | PostgreSQL connection string. Empty = EF InMemory. |
| `ConnectionStrings__Redis` | Redis connection string. Empty = in-process cache. |
| `Steam__Realm` | OpenID realm (must match the URL the browser hits). |
| `Steam__ReturnUrl` | Backend callback URL for Steam OpenID. |
| `Steam__FrontendBaseUrl` | Frontend base URL; redirect target after login (`/players/{steamId64}`). |
| `Steam__WebApiKey` | Optional Steam Web API key for fetching profile names and avatars. |
| `Steam__MarketBackfillSecret` | Secret for the `X-Backfill-Key` header used by pricing backfill endpoints. |
| `Cors__AllowedOrigins__0` | Allowed frontend origin for CORS. |
| `NUXT_API_BASE` | Backend URL that the Nuxt proxy forwards `/api/**` requests to. |

## API Endpoints

| Method | Route | Auth | Description |
| --- | --- | --- | --- |
| `GET` | `/api/health` | public | Service liveness check |
| `GET` | `/api/auth/steam/login` | public | Redirects to Steam OpenID login |
| `GET` | `/api/auth/steam/callback` | public | OpenID callback — sets session cookie |
| `POST` | `/api/auth/logout` | public | Clears session cookie |
| `GET` | `/api/leaderboard?appId=730&limit=50` | public | Leaderboard by inventory value |
| `GET` | `/api/players/{steamId64}` | public | Player profile + top 20 items |
| `POST` | `/api/players/{steamId64}/inventory/refresh` | own user | Sync inventory from Steam |
| `GET` | `/api/pricing/items?marketHashNames=...` | public | Bulk price lookup |
| `GET` | `/api/pricing/items/{marketHashName}` | public | Single item price |
| `GET` | `/api/pricing/history/{marketHashName}` | public | Price history (daily data points) |
| `GET` | `/api/pricing/status` | public | Pricing pipeline coverage stats |
| `POST` | `/api/pricing/snapshot-all` | 🔑 key | Write today's price snapshot for all items |
| `POST` | `/api/pricing/backfill-batch?batchSize=20` | 🔑 key | Backfill one batch of Steam market history |

`🔑 key` endpoints require the `X-Backfill-Key` header matching `Steam__MarketBackfillSecret`.

## Data Models

| Entity | Purpose |
| --- | --- |
| `User` | SteamId64, display name, avatar, login and sync metadata |
| `InventoryItem` | Steam asset, market hash name, item details, and current price |
| `InventorySnapshot` | Total inventory value at a point in time |
| `ItemPriceSnapshot` | Daily price history per item (from Skinport + Steam market backfill) |
| `SteamMarketCredential` | Steam refresh token for the authenticated market history API |

## EF Core Migrations

Migrations are applied automatically on startup (`Database.Migrate()`). You only need the CLI after changing a domain entity:

```bash
dotnet ef migrations add <MigrationName> \
  --project backend/src/LootBase.Infrastructure/LootBase.Infrastructure.csproj \
  --startup-project backend/src/LootBase.Api/LootBase.Api.csproj \
  --output-dir Persistence/Migrations
```

## Caching

Redis caches the full Skinport price catalog per currency with a 15-minute TTL, plus an in-process memory cache on top. If `ConnectionStrings__Redis` is empty, the backend automatically uses a local Distributed Memory Cache.

## Limitations

- Only public Steam inventories can be read
- Skinport pricing may be rate-limited
- Not every item has a market price at all times
- Automatic periodic inventory syncs are not yet implemented
- Only CS2 is currently wired up

## Roadmap

- Background worker for automatic periodic inventory syncs
- Additional pricing provider (e.g. CSFloat)
- Inventory value history chart per player
- Friends lists and private leaderboards
- Tests for services, providers, and API endpoints

## License

No license set.
