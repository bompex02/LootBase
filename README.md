# LootBase

LootBase is a starter monorepo for comparing Counter-Strike 2 inventory values through Steam login, player profiles, and a leaderboard.

## Current Stack

Checked on 2026-07-03:

| Area | Version |
| --- | --- |
| Backend | .NET 10 LTS / ASP.NET Core 10 |
| EF Core | 10.0.9 |
| PostgreSQL provider | Npgsql.EntityFrameworkCore.PostgreSQL 10.0.2 |
| Database target | PostgreSQL 18 |
| Frontend | Nuxt 4.4.8 |
| UI | Nuxt UI 4.9.0 |
| Styling | Tailwind CSS 4.3.2 |
| TypeScript | 6.0.3 |
| Vue | 3.5.39 |
| Node | >= 22, Node 24 LTS recommended for deploys |

Sources used for the version decisions:

- .NET download/support: https://dotnet.microsoft.com/en-us/download/dotnet
- .NET support policy: https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core
- Nuxt 4 docs: https://nuxt.com/docs/4.x/getting-started/installation
- Nuxt 4 release: https://nuxt.com/blog/v4
- Nuxt UI docs: https://ui.nuxt.com/docs/getting-started/installation/nuxt
- Tailwind Nuxt guide: https://tailwindcss.com/docs/installation/framework-guides/nuxt
- Node release schedule: https://nodejs.org/en/about/previous-releases
- PostgreSQL versioning: https://www.postgresql.org/support/versioning/

## Structure

```text
backend/src/LootBase.Api             ASP.NET Core minimal API
backend/src/LootBase.Application     use cases, DTOs, service contracts
backend/src/LootBase.Domain          domain entities and game constants
backend/src/LootBase.Infrastructure  EF Core, Steam, pricing, inventory providers
frontend/                            Nuxt 4 app
```

CS2 is currently the first inventory provider. The interfaces are already game/provider based, so Dota, Rust, TF2, or another price source can be added without changing the frontend contract.

## Local Run

Backend:

```bash
dotnet build LootBase.sln -m:1
dotnet run --project backend/src/LootBase.Api/LootBase.Api.csproj
```

Frontend:

```bash
cd frontend
npm install
npm run dev
```

Open http://localhost:3000. The API listens on http://localhost:5188.

Without a `ConnectionStrings__LootBase` value, the API uses EF Core InMemory and seeds demo leaderboard data.

## PostgreSQL

Start PostgreSQL:

```bash
docker compose up -d postgres
```

Use this connection string when you want real persistence:

```bash
ConnectionStrings__LootBase="Host=localhost;Port=5432;Database=lootbase;Username=lootbase;Password=lootbase"
```

Migrations are intentionally not generated yet. The current dev path uses `EnsureCreated` for the initial model; switch to migrations before production.

## Steam

Steam OpenID is wired through:

```text
GET /api/auth/steam/login
GET /api/auth/steam/callback
POST /api/auth/logout
```

Set these values for a real Steam login flow:

```bash
Steam__Realm="http://localhost:5188/"
Steam__ReturnUrl="http://localhost:5188/api/auth/steam/callback"
Steam__FrontendAuthSuccessUrl="http://localhost:3000/me"
Steam__WebApiKey="your-steam-web-api-key"
```

The app currently uses a demo CS2 inventory provider and a static pricing provider. Replace `Cs2DemoInventoryProvider` and `StaticPricingProvider` with Steam inventory and Skinport/CSFloat/Buff providers when moving beyond the scaffold.

## API

```text
GET  /api/health
GET  /api/leaderboard?appId=730&limit=50
GET  /api/players/{steamId64}
GET  /api/me
POST /api/me/inventory/refresh
```

Authenticated endpoints use an HttpOnly cookie issued after Steam OpenID verification.
