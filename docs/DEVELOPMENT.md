# Development and verification

[README](../README.md) · [Architecture](ARCHITECTURE.md) · [API](API.md)

## Compose

From the repository root, run `docker compose up --build`. Docker must use Linux
containers. No host SDK or manual database setup is necessary. Optional environment
configuration: copy `.env.example` to `.env`; both PowerShell's Copy-Item and a
Unix shell's cp work. Real .env files are ignored; examples are committed.

| Variables | Purpose |
| --- | --- |
| POSTGRES_DB / USER / PASSWORD | Database configuration; development placeholders only |
| JWT_SECRET / ISSUER / AUDIENCE | Signing key ≥32 UTF-8 bytes, required issuer/audience |
| JWT_LIFETIME_MINUTES | 60 by default; allowed range 1–1440 |
| ASPNETCORE_ENVIRONMENT | Development enables Swagger; rejects demo JWT key elsewhere |
| FRONTEND_PORT / BACKEND_PORT | Host ports, default 3000 / 5080 |
| FRONTEND_ORIGIN | Exact CORS origin, default http://localhost:3000 |
| VITE_API_BASE_URL | Browser API including /api, default http://localhost:5080/api |

Change ports, origin and API URL together. The Vite API URL is embedded at build
time, so rebuild after changes. Updating POSTGRES_PASSWORD does not rotate a
password in an existing volume. Do not use development placeholders in production.
PostgreSQL is internal; frontend/API ports bind to localhost. Persistent data survives
`docker compose down`; adding `-v` removes it.

## Host development

.NET 10 SDK (global.json allows latest feature roll-forward), Node 22 and running
Docker are required. Provide ConnectionStrings__Default, Jwt__Secret, Jwt__Issuer,
Jwt__Audience and Cors__Origins__0 as environment variables when starting the API
outside Compose. Backend defaults are not a replacement for a connection string.

```sh
dotnet restore Catalog.slnx
dotnet build Catalog.slnx --no-restore
dotnet test --no-build
cd frontend
npm ci
npm run build
```

The tests use Microsoft.Testing.Platform (global.json), not a VSTest setup.
Testcontainers starts isolated PostgreSQL databases and applies real migrations;
the application's running Compose database is not needed for backend tests.
For frontend development, copy frontend/.env.example to frontend/.env if needed
and run `npm run dev` with a matching CORS origin.

## Browser and API acceptance

Start Compose, then from frontend run:

```sh
npx playwright install chromium
npm run test:e2e
```

Docker alternative from the root:

```sh
docker build -f frontend/Dockerfile.e2e -t catalog-e2e frontend
docker run --rm --network demoerp_default catalog-e2e
```

Use the actual network from `docker network ls` (derived from the Compose project
name; `DemoErp` usually becomes `demoerp_default`). The runner forwards local
3000/5080 URLs to Compose services, keeping the browser bundle/CORS unchanged.
Custom ports: pass FRONTEND_URL and API_URL to the test container.

`node scripts/verify.mjs` checks schema, auth, CRUD, order totals/reference protection,
restart persistence and safe errors/logs. It restarts services and briefly stops
PostgreSQL: use a disposable evaluation stack. Custom stacks require
COMPOSE_PROJECT_NAME, API_URL and FRONTEND_URL plus matching Compose variables.
Browser tests create synthetic accounts and records; they are not production probes.

## Screenshots

The capture script registers a synthetic reviewer account and creates demo data.
Use a fresh disposable Compose project to avoid capturing personal/business data.
It runs Chromium against actual pages and does not mock API responses.

PowerShell example after starting a fresh stack on the default ports:

```powershell
docker build -f frontend/Dockerfile.e2e -t catalog-e2e frontend
docker run --rm --network demoerp_default --mount "type=bind,source=$PWD/docs/screenshots,target=/work/screenshots" -e PORTFOLIO_CAPTURE=1 catalog-e2e
```

Create docs/screenshots first if needed. Replace the network with your project name;
for nondefault ports pass FRONTEND_URL/API_URL. The captured records are synthetic
and intentionally remain in that disposable database until it is removed.

## CI and publication

.github/workflows/ci.yml runs backend restore/build/tests on Ubuntu with Docker
available for Testcontainers, plus npm ci/build on Node 22. Actions are pinned to
verified official release SHAs. Browser acceptance remains a separate local check;
there is no deployment job. Remote CI results must be checked after publication.

Before publication, inspect source candidates and all relevant Git history with a
secret scanner. Examples deliberately contain local placeholders; actual credentials,
generated files, databases, traces and logs must remain excluded. A clean scan is
not proof that every possible secret format is absent. [Recorded evidence](../VERIFICATION.md).
