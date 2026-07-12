# LootBase

![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![Nuxt](https://img.shields.io/badge/Nuxt-4-00DC82?logo=nuxt&logoColor=white)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-18-4169E1?logo=postgresql&logoColor=white)
![TypeScript](https://img.shields.io/badge/TypeScript-6-3178C6?logo=typescript&logoColor=white)

LootBase ist eine Full-Stack-Webanwendung zum Vergleichen von Counter-Strike-2-Inventarwerten. Nutzer melden sich per Steam an, synchronisieren ihr öffentliches CS2-Inventar und erscheinen anschließend im Leaderboard.

Der aktuelle Stand unterstützt CS2. Weitere Spiele und Preisquellen sind über Provider-Strukturen vorbereitet.

## Funktionen

- Steam Login über OpenID 2.0
- Abruf öffentlicher CS2-Inventare über Steam Community Inventory
- Preisermittlung über die Skinport API
- Profilseite mit Inventarwert, Itemanzahl und Top-Items
- Leaderboard nach Inventarwert
- PostgreSQL-Unterstützung mit EF Core und Migrations
- EF-InMemory-Fallback für lokale Entwicklung ohne Datenbank
- Redis-Cache für wiederkehrende Preisabfragen
- Nuxt-Frontend mit serverseitigem Rendering und Cookie-Forwarding

## Architektur

```text
Nuxt Frontend
    |
    | HTTP / HttpOnly Cookie
    v
ASP.NET Core API
    |
    +-- Auth Endpoints
    +-- Player Endpoints
    +-- Leaderboard Endpoints
    |
    v
Application
    |
    +-- Inventory Refresh
    +-- Player Profiles
    +-- Leaderboard
    |
    v
Infrastructure
    |
    +-- Steam Inventory
    +-- Skinport Pricing
    +-- Redis Cache
    +-- EF Core Persistence
    |
    v
PostgreSQL oder EF InMemory
```

## Tech Stack

| Bereich | Technologie |
| --- | --- |
| Backend | .NET 10, ASP.NET Core Minimal API |
| ORM | Entity Framework Core 10 |
| Datenbank | PostgreSQL 18 oder EF InMemory |
| Cache | Redis oder Distributed Memory Cache |
| Auth | Steam OpenID 2.0, ASP.NET Cookie Auth |
| Frontend | Nuxt 4, Vue 3, TypeScript |
| UI | Nuxt UI 4, Tailwind CSS 4, Nuxt Icon |
| APIs | Steam Community Inventory, Skinport |

## Projektstruktur

```text
.
├── backend/
│   └── src/
│       ├── LootBase.Api/             # API, Auth, Endpoints
│       ├── LootBase.Application/     # Services, DTOs, Interfaces
│       ├── LootBase.Domain/          # Entities und Konstanten
│       └── LootBase.Infrastructure/  # EF Core, Steam, Pricing
├── frontend/                         # Nuxt App
├── docker-compose.yml                # PostgreSQL für lokale Entwicklung
├── .env.example                      # lokale Environment-Variablen
├── Directory.Packages.props          # zentrale NuGet-Versionen
└── LootBase.sln
```

## Voraussetzungen

- .NET SDK 10
- Node.js 22 oder neuer
- npm
- Docker optional für PostgreSQL und Redis
- Öffentliches Steam-Inventar für echten Inventory Sync

Steam-Inventar öffentlich setzen:

```text
Steam Profil -> Profil bearbeiten -> Privatsphäre-Einstellungen -> Inventar -> Öffentlich
```

## Lokaler Start

Backend:

```bash
cp .env.example .env
set -a
. ./.env
set +a

dotnet build LootBase.sln -m:1
dotnet run --project backend/src/LootBase.Api/LootBase.Api.csproj
```

Frontend:

```bash
cd frontend
npm ci
npm run dev
```

Standard-URLs:

| Anwendung | URL |
| --- | --- |
| Frontend | `http://localhost:3000` |
| Backend | `http://localhost:5188` |

Für Login und Cookies lokal konsequent `localhost` verwenden, nicht gemischt `localhost` und `127.0.0.1`.

## Konfiguration

Lokale Runtime-Werte liegen in `.env`. Die Datei ist gitignored; `.env.example` dient als Vorlage.

| Variable | Beschreibung |
| --- | --- |
| `ConnectionStrings__LootBase` | PostgreSQL Connection String. Leer bedeutet EF InMemory. |
| `ConnectionStrings__Redis` | Redis Connection String. Leer bedeutet lokaler Distributed Memory Cache. |
| `Steam__Realm` | OpenID Realm, lokal `http://localhost:5188/`. |
| `Steam__ReturnUrl` | Backend Callback für Steam OpenID. |
| `Steam__FrontendAuthSuccessUrl` | Ziel nach erfolgreichem Login. |
| `Steam__WebApiKey` | Optionaler Steam Web API Key für Profildaten. |
| `Cors__AllowedOrigins__0` | Erlaubter Frontend-Origin. |
| `NUXT_API_BASE` | Backend-URL, an die das Nuxt-Frontend `/api/**`-Requests proxyt (server- und clientseitig). |

`.env` wird von ASP.NET Core nicht automatisch geladen. Für lokale Starts die Datei im Terminal exportieren:

```bash
set -a
. ./.env
set +a
```

## PostgreSQL und Redis

Ohne Connection String nutzt das Backend EF InMemory. Damit kann die Anwendung ohne Datenbank gestartet werden; Daten gehen beim Neustart verloren.

PostgreSQL und Redis starten:

```bash
docker compose up -d postgres redis
```

Backend mit PostgreSQL und Redis starten:

```bash
set -a
. ./.env
set +a

dotnet run --project backend/src/LootBase.Api/LootBase.Api.csproj
```

Beim Start wendet das Backend ausstehende EF-Core-Migrations automatisch an (`Database.Migrate()`), sofern eine relationale Connection String gesetzt ist. Neue Migration nach Modelländerungen erzeugen:

```bash
dotnet ef migrations add <Name> \
  --project backend/src/LootBase.Infrastructure/LootBase.Infrastructure.csproj \
  --startup-project backend/src/LootBase.Api/LootBase.Api.csproj \
  --output-dir Persistence/Migrations
```

## Caching

Redis cached den kompletten Skinport-Preiskatalog pro Währung, 15 Minuten TTL, zusätzlich zu einem In-Process-Memory-Cache.

Wenn `ConnectionStrings__Redis` leer ist, nutzt das Backend automatisch einen lokalen Distributed Memory Cache.

## Steam Inventory

LootBase liest CS2-Inventare über den öffentlichen Steam-Inventory-Endpunkt:

```text
https://steamcommunity.com/inventory/{steamId64}/730/2?l=english&count=5000
```

Für CS2 gilt:

- `appid = 730`
- `contextid = 2`

## API

| Methode | Route | Beschreibung |
| --- | --- | --- |
| `GET` | `/api/health` | Healthcheck |
| `GET` | `/api/auth/steam/login` | Startet den Steam Login |
| `GET` | `/api/auth/steam/callback` | Callback nach Steam OpenID |
| `POST` | `/api/auth/logout` | Beendet die Session |
| `GET` | `/api/leaderboard?appId=730&limit=50` | Leaderboard |
| `GET` | `/api/players/{steamId64}` | Öffentliches Spielerprofil |
| `GET` | `/api/me` | Eigenes Profil, Auth erforderlich |
| `POST` | `/api/me/inventory/refresh` | Synchronisiert das eigene Inventar |
| `GET` | `/api/pricing/items?marketHashNames=...&currency=EUR` | Preise für mehrere Items |
| `GET` | `/api/pricing/items/{marketHashName}?currency=EUR` | Preis für ein Item |

## Datenmodell

| Entity | Zweck |
| --- | --- |
| `User` | SteamID64, Anzeigename, Avatar, Login- und Sync-Metadaten |
| `InventoryItem` | Steam Asset, Market Hash Name, Itemdaten und Preis |
| `InventorySnapshot` | Gesamtwert eines Inventars zu einem Zeitpunkt |

Der Inventory Sync speichert die aktuellen Items eines Nutzers und erzeugt pro Refresh einen Snapshot.

## Provider

Aktuelle Implementierungen:

| Provider | Aufgabe |
| --- | --- |
| `Cs2SteamInventoryProvider` | Liest öffentliche CS2-Inventare von Steam. |
| `PricingProvider` | Liest den Skinport-Preiskatalog und cached ihn. |

Weitere Spiele oder Preisquellen können über zusätzliche Provider ergänzt werden.

## Entwicklung

Backend builden:

```bash
dotnet build LootBase.sln -m:1
```

Frontend prüfen:

```bash
cd frontend
npm run typecheck
npm run build
```

Audit:

```bash
cd frontend
npm audit
```

Ports freigeben:

```bash
lsof -ti :5188 | xargs -r kill
lsof -ti :3000 | xargs -r kill
```

## Debugging in VS Code

1. Repository in VS Code öffnen.
2. C# Dev Kit installieren.
3. Backend builden.
4. In `Run and Debug` eine C#/.NET-Konfiguration für `LootBase.Api` verwenden.
5. Breakpoint in einem Endpoint setzen.
6. Request über Browser, Frontend oder HTTP-Client auslösen.

Bei manueller `launch.json` zeigt `program` auf:

```text
backend/src/LootBase.Api/bin/Debug/net10.0/LootBase.Api.dll
```

## Einschränkungen

- Nur öffentliche Steam-Inventare können gelesen werden.
- Skinport Pricing kann rate-limitiert werden.
- Nicht jedes Item hat jederzeit einen verfügbaren Marktpreis.
- Automatische periodische Inventory Syncs sind noch nicht implementiert.
- Aktuell ist nur CS2 angebunden.

## Roadmap

- Background Worker für regelmäßige Inventory Syncs
- Zusätzlicher Pricing Provider, z. B. CSFloat
- Verlauf des Inventarwerts
- Freundeslisten und private Leaderboards
- Tests für Services, Provider und API-Endpunkte
- Dockerfile und Deployment-Konfiguration

## Lizenz

Aktuell ist keine Lizenz hinterlegt.
