# Testing DemoErp

[README](../README.md) · [Executed results](../VERIFICATION.md)

## Backend

Use .NET SDK 10 and a running Docker daemon with Linux containers:

```sh
dotnet restore Catalog.slnx
dotnet build Catalog.slnx --no-restore
dotnet test Catalog.slnx --no-build
```

The suite uses xUnit v3 with Microsoft Testing Platform and real PostgreSQL 17 Testcontainers. Each fixture gets an isolated database and startup applies the committed migrations. It covers authentication/revocation, role boundaries and last-Admin protection, CRUD, sales, inventory, procurement, receipt replay, warehouse transfers, immutable BOMs, production consumption/output and audit rollback. Race tests exercise competing writes and verify final balances and movement counts.

Health tests stop only their own PostgreSQL container. Liveness must remain 200, readiness must return safe 503 within five seconds, and a restarted fixture reconnects to the container's current mapped port. The API readiness deadline itself is two seconds.

For class selection, this repository uses `dotnet test Catalog.slnx -- --filter-class Demo.Api.Tests.HealthTests`; do not substitute VSTest filtering flags. On Windows, Docker Desktop may require `DOCKER_HOST=npipe://./pipe/dockerDesktopLinuxEngine`.

## Frontend static and component checks

```sh
cd frontend
npm ci
npm run lint
npm run typecheck
npm test
npm run build
npm audit --audit-level=high
```

ESLint checks production TypeScript; TypeScript also checks test sources. Vitest runs `src/**/*.test.tsx` in jsdom with Testing Library. Tests cover readable/localized statuses, field errors, accessible loading, language state, role boundaries, protected content, login validation, actual workflow indicators and save-message dismissal. Browser-level dialog, table, search, select, pagination and dynamic-row behavior is covered in Playwright instead of imitating native browser behavior in jsdom.

Vitest's test-only API URL points at an unreachable loopback port; component tests do not contact a real backend. The pinned Vitest 4.1.11 works with the repository's Vite 6 and avoids the dependency advisories found in the initially considered Vitest 3 tooling. ESLint 10 requires Node 22.13+ on the Node 22 line; use current Node 22 for local/CI commands. See [Vitest](https://vitest.dev/guide/) and [typescript-eslint](https://typescript-eslint.io/getting-started/).

## Browser tests: use a disposable database

Never aim browser write workflows at a database containing user records. Create a separate Compose project, its own named volume, and distinct localhost ports. Supply synthetic Admin bootstrap values through an ignored environment file. Example PowerShell setup, with test-only credentials chosen locally:

```powershell
$env:COMPOSE_PROJECT_NAME='demoerp-browser-tests'
$env:FRONTEND_PORT='4400'
$env:BACKEND_PORT='6480'
$env:FRONTEND_ORIGIN='http://localhost:4400'
$env:VITE_API_BASE_URL='http://localhost:6480/api'
$env:BOOTSTRAP_ADMIN_ENABLED='true'
# Set BOOTSTRAP_ADMIN_EMAIL / NAME / PASSWORD to synthetic test values.
docker compose up -d --build --wait
$env:FRONTEND_URL='http://localhost:4400'
$env:API_URL='http://localhost:6480'
cd frontend
npx playwright install chromium
npm run test:smoke
npm run test:a11y
```

`test:smoke` covers login, API-backed dashboard, desktop collapse/navigation, language switching, mobile drawer Escape/focus restoration/navigation and logout. `test:a11y` runs axe on login, dashboard, product list/form, order detail, confirmation dialog and mobile navigation. It checks WCAG 2 A/AA and 2.1/2.2 AA tags, but is not a formal conformance audit. [Playwright accessibility guidance](https://playwright.dev/docs/accessibility-testing).

Full regressions use `npm run test:e2e -- <spec files>`. Run these groups separately, restarting **the disposable backend only** between groups to reset the deliberate 20/minute authentication limiter:

1. `catalog.spec.ts business.spec.ts inventory.spec.ts security.spec.ts reporting.spec.ts localization.spec.ts states.spec.ts ui.spec.ts`
2. `warehouses.spec.ts procurement.spec.ts`
3. `ai.spec.ts`
4. `manufacturing.spec.ts`

Provider calls are mocked for AI tests. No live AI credentials are needed. Playwright retains traces on failure. Treat trace files as potentially sensitive; never upload traces from real-user environments. CI uses only synthetic fixtures and does not publish raw traces containing login inputs.

## Screenshots and responsive checks

`portfolio.spec.ts` is excluded unless `ERP_TEST_ENVIRONMENT=isolated` is explicitly set. Run it only against a disposable stack; it creates actual synthetic products, customers, warehouses, receipts, transfers, BOMs and completed production/sales. Set `SCREENSHOT_DIR` to an output directory, then run `npm run test:e2e -- portfolio.spec.ts`.

The matrix renders dashboard, product form/list, order detail, warehouses, purchases, BOM form and production detail at 1440, 1024, 768 and 390 pixels and asserts no horizontal document overflow. Screenshots supplement these assertions; inspect layout, chart labels, clipped controls, long text, tables and drawer/dialog focus manually. Keyboard-only and screen-reader checks remain necessary beyond axe. Reduced-motion rules are centralized in CSS.

## API and SQL checks

`scripts/verify.mjs` includes a PostgreSQL outage check and must run only against a disposable Compose project, never concurrently with browser workflows. The guarded SQL benchmark uses a local database named `reporting_benchmark`; see [Performance](PERFORMANCE.md). Neither benchmark nor integration tests should ever use the primary Compose database.
