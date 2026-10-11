> Publication update: Phase 7–10 sources and the Windows installation guide are included in this authorized GitHub release. The reports below record verification at the time of each phase; statements about an unpublished revision are historical. Remote CI for the published commit must be checked in GitHub Actions. A separate fresh Compose database/volume was built and started from the publication tree on ports 3017/5097; all three services were healthy, all three health endpoints returned 200, initial Admin bootstrap and demo Viewer/Manager creation/login passed, and disabling bootstrap then recreating the backend preserved access. This is a local fresh-stack check, not a clean Windows OS installation. No public application deployment is claimed.

# Phase 10 delivery and verification — 2026-10-11

This is the current delivery report; historical phase reports follow below. Work is local. **No GitHub push, public deployment, new business module or schema migration was performed.**

## Repository review

The existing application already had the sales, inventory, procurement, warehouse, BOM/production, RBAC, audit and reporting implementations; they were inspected instead of assumed complete. High-value issues were an overloaded wrapping navigation bar, inconsistent visual hierarchy/statuses, eager business-route imports, no frontend lint/component-test commands, missing separate liveness/readiness, an unbounded health dependency check, and Nginx security-header inheritance on cached assets. The production-preview query fetched unrelated warehouse balances. README contained obsolete claims about implemented manufacturing.

## UI/UX improvements

Central semantic green/neutral/status/focus/spacing/type tokens; grouped role-aware sidebar with icons and desktop collapse; native mobile drawer with Escape/focus restoration; route context, language, user/role/account disclosure and logout; skip link. Dashboard retains real metrics while improving card/chart hierarchy, product/customer grouping, loading skeletons and operational overview. Tables/forms/details share denser styling, bounded horizontal regions, keyboard access, errors/disabled states and confirmed-save feedback. Sales/purchase/production workflow indicators show current state without inventing event history. Manufacturing, purchasing and stock statuses are consistent and localized. Production timestamps now use the selected locale.

Actual browser checks cover 1440/1024/768/390 widths. Axe scanned login, dashboard, product list/form, order detail, delete dialog and mobile drawer with zero final violations in the selected rules. Login caption/back-link contrast issues found during work were corrected. Native dialog trapping/Escape/focus restoration and mobile menu interaction are covered. This is not formal WCAG certification.

## Backend improvements

Added process-only `/health/live` and PostgreSQL `/health/ready` with a linked two-second deadline. `/health` retains its response contract. Four new real-PostgreSQL tests cover healthy contracts and dependency outage behavior; fixture restart reconnects to the container's current mapped port. Existing API validation, policies, pricing, state transitions, transaction ownership, audit semantics and lock order were preserved. No critical business concurrency defect was found requiring a workflow rewrite. All existing concurrency/rollback/idempotency tests pass.

## Database improvements and performance

No index/migration change. Production preview now limits stock lookup to recipe materials, using the existing unique warehouse/product index. On deterministic isolated data: 5,000 → five returned stock rows, 57 → 11 shared hit blocks; warm median **0.421 → 0.031 ms**. Seven warm samples follow a discarded warmup; raw SQL/plans and six additional measured reporting queries are in [Performance](docs/PERFORMANCE.md). No production workload, HTTP latency, cold-cache or unchanged-query improvement is claimed.

Initial JavaScript entry: **589.77 → 416.88 kB**, gzip **169.82 → 129.08 kB**, measured by separate locked baseline/final builds. Route chunks load on demand; reporting remains about 403 kB. The 29.3% reduction applies to the initial minified entry, not total code or measured page-load time.

## Automated tests and executed commands

| Check | Actual result |
| --- | --- |
| `dotnet build Catalog.slnx` | PASS, zero warnings/errors |
| `dotnet test Catalog.slnx` | PASS, **417/417**, zero skipped; real PostgreSQL Testcontainers |
| `npm ci` | PASS, clean locked install |
| `npm run lint` / `npm run typecheck` | PASS |
| `npm test` | PASS, **16/16** Vitest/Testing Library cases |
| `npm run build` | PASS; no >500 kB chunk advisory |
| Existing browser group 1 | PASS **20/20**: auth, CRUD, sales, inventory, security, reporting, localization, sessions, dialog/mobile |
| Existing browser group 2 | PASS **22/22**: warehouse and procurement/receipt workflows |
| Existing browser group 3 | PASS **7/7**: AI status/errors/cancellation/localization with mocked provider |
| Existing browser group 4 | PASS **16/16**: BOM/production lifecycle, availability, permissions, dates, immutable draft rules |
| New smoke + axe | PASS **2/2**; final representative scans have no selected-rule violations |
| Portfolio responsive/capture | PASS **1/1**, 32 route/width combinations; actual synthetic APIs |
| `node scripts/verify.mjs` | PASS on disposable Compose: all modules, persistence/restarts, safe database-outage errors, no test credentials in backend logs |
| `npm audit` | PASS, zero reported advisories in final locked dependency tree |
| NuGet transitive vulnerability audit | PASS, no vulnerable packages reported for API/test projects |
| Documentation links / translation audit | PASS; centralized UI keys and intentional brand literals |
| Mermaid browser rendering | PASS, 21 current diagrams rendered |

Browser total: **68/68** including capture; full workflows remain separate from the short smoke test. After the final missing Turkish “Created at” label was fixed, production + smoke + axe + screenshot refresh were rerun: **19/19 targeted checks passed**. Component tests remained **16/16**. [Reproduction commands, isolation and manual checks](docs/TESTING.md).

Initial failed runs are retained in ignored local evidence: Docker was closed on the first backend attempt; the first health fixture restart used a stale mapped port; older browser tests needed explicit mobile-menu opening and a real in-flight-request synchronization after lazy loading. Contrast findings and capture fixture contract errors were fixed and rerun. These initial attempts are not counted as passes.

## CI/CD

`.github/workflows/ci.yml` now triggers on PR/main/manual dispatch, uses read-only repository permissions, and runs backend restore/build/Testcontainers tests; frontend npm ci/lint/types/Vitest/build/audit; and a separate disposable Compose smoke/axe job. No AI keys or real deployment credentials are needed. Test-only bootstrap values are confined to the ephemeral smoke runner. Local equivalents passed. **Remote GitHub CI is UNVERIFIED** because no approved push/PR occurred. No deployment job was added.

## Docker, health and data compatibility

Existing multi-stage/unprivileged runtime Dockerfiles and dependency caching were retained. Compose still starts three services, keeps PostgreSQL unexposed and preserves its named volume. It now uses readiness for backend health. Nginx HTML no-cache and hashed-asset immutable caching retain security headers in their actual location contexts.

`docker compose up -d --build --wait` passed for independent test projects and the existing `demoproject` runtime. The main runtime is healthy at localhost:3000 / localhost:5080. Main bootstrap remained disabled. Pre/post comparison of **23 business tables/views** is identical; no synthetic test data was inserted into the main database. A fresh custom-format backup was restored into a separate database and all 23 signatures matched. Nine migrations remain applied; existing migration files are byte-identical.

Actual HTTP checks: `/health`, `/health/live`, `/health/ready` return 200; `/index.html` has no-cache and cached `/assets/*.js` has immutable/max-age. Both include nosniff, DENY framing and referrer policy. Database-outage behavior is separately proven by the new tests.

## Security

Reviewed JWT/current-user/SecurityVersion, deactivation, RBAC/last-Admin, CORS, validation, safe ProblemDetails, auth limiter, audit payload allowlists, logs, runtime user privileges and environment boundaries. Fixed Nginx header inheritance. Initially considered Vitest 3 tooling had critical/transitive advisories; selected compatible Vitest 4.1.11/ESLint 10 instead. Final npm/NuGet audits report none. This is not a penetration test or complete container-image CVE scan.

Gitleaks scanned all 300 publish-candidate files with zero findings; a separate source-pattern/private-file check also passed. AI integrity is verified separately. AI's nine implementation files remain byte-identical to Phase 9, and all seven browser AI tests pass; no live provider request was made. NOTICE usage terms remain unchanged; PUBLICATION_PLAN.md was not recreated.

## Documentation and screenshots

English README rewritten around actual connected workflows, architecture, quick start, test commands, screenshots and operational limits; Turkish README updated. ARCHITECTURE, DATABASE, SECURITY and PERFORMANCE updated. New [TESTING](docs/TESTING.md), [DEPLOYMENT](docs/DEPLOYMENT.md), [UI_DESIGN](docs/UI_DESIGN.md).

Nine named desktop screenshots cover login, dashboard, products, orders, inventory, warehouses, purchasing, BOMs and production detail; mobile Turkish production detail and the responsive matrix supplement them. All are actual browser captures from synthetic API data. The main database and real credentials are excluded.

## Remaining issues and unverified areas

- Remote Actions execution remains UNVERIFIED pending an approved push/PR.
- Public production hosting, TLS/proxy operations, external backup retention, full image CVE scanning and penetration testing were not performed. Deployment requirements are documented.
- Axe and representative keyboard/viewport checks do not replace a comprehensive screen-reader/manual accessibility audit.
- SQL/bundle results are local synthetic measurements; no production SLA or universal performance gain is claimed.
- Existing single-instance startup migrations and in-memory AI quotas need an operational strategy for multi-instance hosting.

## Acceptance criteria

| ID | Result | Evidence |
| --- | --- | --- |
| AC-01 | PASS | 417 backend tests, 65 existing browser regressions and full API verifier |
| AC-02 | PASS | Shared shell, desktop collapse, mobile drawer, role-aware nav and rendered checks |
| AC-03 | PASS | Real API dashboard, charts/KPIs, skeletons and capture |
| AC-04 | PASS | Shared table styling/scroll regions, existing filter/sort/pagination regression |
| AC-05 | PASS | Form styling, validation/component/browser tests, confirmed-save feedback |
| AC-06 | PASS | Structured metadata, shared detail styling and workflow indicators; real detail captures |
| AC-07 | PASS | TR/EN browser regressions, language component tests and static audit |
| AC-08 | PASS | 32 actual route/width checks at 1440/1024/768/390, no document overflow |
| AC-09 | PASS | Final axe scans and keyboard/dialog/mobile interaction checks; bounded scope |
| AC-10 | PASS | Executed .NET build |
| AC-11 | PASS | Executed frontend production build |
| AC-12 | PASS | 417/417 backend |
| AC-13 | PASS | 16/16 component, 68/68 browser including capture |
| AC-14 | PASS | Real PostgreSQL Testcontainers; constraints, transactions and query translation |
| AC-15 | PASS | Short navigation/auth smoke plus full product/sales/warehouse/procurement/BOM/production journeys |
| AC-16 | PASS | Existing concurrent deductions/receipts/transfers/production, replay, rollback and last-Admin tests |
| AC-17 | PASS | Eight actual EF SQL measurements and saved EXPLAIN ANALYZE BUFFERS plans |
| AC-18 | PASS | Header fix, dependency advisory remediation, policy/log/error regression; no full security certification |
| AC-19 | UNVERIFIED | Workflow implemented; local equivalent passes; no remote run without push |
| AC-20 | PASS | Test and existing runtime Compose build/start/health |
| AC-21 | PASS | 23-table/view signatures preserved; independent backup restore; existing volume retained |
| AC-22 | PASS | Current professional English README and Turkish entry point |
| AC-23 | PASS | Actual layered architecture, diagrams rendered, local links verified |
| AC-24 | PASS | Actual synthetic screenshots and four-width matrix |
| AC-25 | PASS | Gitleaks zero findings on all 300 publish candidates; private/generated files excluded; no commit/push |
| AC-26 | PASS | Nine AI files identical; seven browser cases pass; no provider credential required |

---

# Phase 9 delivery and verification — 2026-10-10

This report supersedes the historical phase reports below for current functionality and test totals. Scope: additive manufacturing on the existing full-stack application; no GitHub push performed.

## Implemented Features

Product RawMaterial/FinishedGood classification, versioned single-level BOM CRUD (physical deletion restricted to unused drafts), Admin-only activation/deactivation, one active version, immutable activated/retired recipes, saved material requirement snapshots, authoritative server calculation/preview, production commands, warehouse availability, requirements/shortage reports, separate manufacturing KPIs, audit and Admin/Manager/Viewer enforcement. React forms/detail/lists support TR/EN, responsive material cards, backend preview, refreshed availability and accessible confirmations. Existing catalog, sales, inventory, warehouse, procurement, reporting and AI behavior remains covered by regression tests.

## Database Changes

Migration `20261010091341_Manufacturing` adds BillOfMaterials, BillOfMaterialsItems, ProductionOrders, ProductionOrderMaterials and ProductionNumbers; adds Products.ProductType (RawMaterial default) and nullable typed InventoryMovements.ProductionOrderId. Restricted historical FKs, unique versions/materials/order numbers, filtered one-active-BOM/per-production-movement indexes, integer bounds, valid timestamps/states and trigger-protected history/snapshots are enforced. Deferred checks validate recipes, exact material snapshots and expected consumption/output traceability at commit. Previous migrations are byte-identical. No existing tables/data were reset. Down requires a verified backup or forward fix.

## BOM Design

Positive integer recipe/base quantities with distinct RawMaterials for a FinishedGood. Drafts editable; ActivatedAt permanently freezes recipe content. Activating a fresh version retires the prior one in the same transaction. Required quantities use checked Int64 multiplication, exact divisibility and positive Int32 result validation. ProductionOrders retain their original BOM and copied requirements, even when another version activates. Draft resave explicitly selects the current active recipe and rebuilds the snapshot.

## Production Lifecycle

Draft → Released → InProgress → Completed. Draft/Released may Cancel; standard cancellation after start is forbidden. Creation/release never change inventory. No partial production. Started/Completed events record authenticated actors and UTC timestamps. Released planning/material fields and finalized orders cannot be edited.

## Inventory Integration

Start deducts every material from SourceWarehouse via the shared InventoryService.Apply and records ProductionConsumption movements plus QuantityConsumed. Complete verifies actual consumption and receives exactly PlannedQuantity into DestinationWarehouse with ProductionOutput. Source/destination may be equal. No cross-depot borrowing, reservation, automatic transfer or purchase order. Product/warehouse activity checks and company-wide Int32 cap remain enforced. Global Inventories remains a read-only SUM view; LegacyInventories is untouched.

## Transaction Strategy

Document, all balances, material quantities, movements, actor/time/status and audit commit in one caller-owned PostgreSQL transaction. Multi-material shortage, output overflow and deliberately failed audit FK insert roll back all business effects. Informational availability can change; stock is authoritatively checked under locks inside start. After consumption, displayed shortage is for outstanding required-minus-consumed quantity.

## Concurrency Strategy

Order row FOR UPDATE serializes retries; sorted warehouses FOR SHARE → sorted products FOR UPDATE → sorted warehouse/product balances FOR UPDATE matches all existing stock writers. Unique movement indexes provide another defense. Real tests cover simultaneous starts, separate API instances, production versus sales/transfer and completion versus receipt. Retried successful commands return the persisted result without another movement/audit.

## Tests — actual executions

| Check | Result | Evidence |
|---|---|---|
| .NET build | PASS, 0 warnings/errors | `dotnet build Catalog.slnx --no-restore` |
| Backend suite | PASS, **413/413**, 0 skipped, 54.382 s | `dotnet test Catalog.slnx --no-build`; [manufacturing tests](backend/Demo.Api.Tests/ManufacturingTests.cs), all prior suites |
| Manufacturing coverage | PASS, **62** test cases | BOM validation/versioning/immutability/delete; scaling/fraction/overflow/snapshot; commands; atomic stock; races; real audit failure; raw SQL guards; roles/revocation/reports |
| Populated migration upgrades | PASS | [MigrationTests](backend/Demo.Api.Tests/MigrationTests.cs): Phase 8 explicitly applied before Phase 9; old-table hashes and default ProductType checked, repeat migration/model compatibility |
| Core browser regression | PASS, **20/20**, 55.0 s | catalog, business, inventory, security, reporting, localization, states, ui |
| Warehouse/procurement browser regression | PASS, **22/22**, 29.6 s | warehouses.spec.ts and procurement.spec.ts |
| Existing AI browser regression | PASS, **7/7**, 10.6 s | ai.spec.ts |
| Manufacturing browser | PASS, **16/16**, 20.1 s | [manufacturing.spec.ts](frontend/tests/manufacturing.spec.ts): form/schema/rows, lifecycle, roles, availability, recovery, TR/EN, Istanbul date round-trip, confirmed draft deletion, actual complete manufacturing flow |
| Total browser cases | PASS, **65/65** | Four suites above; isolated backend restart between groups retains normal auth limiter |
| Frontend production build | PASS | `npm --prefix frontend run build`, also Docker frontend build |
| Docker fresh startup | PASS | isolated catalogmanufacturingqa20261010 with no added services; nine migrations, healthy backend/frontend/PostgreSQL |
| API end-to-end | PASS | [scripts/verify.mjs](scripts/verify.mjs): existing workflows plus actual recipe/snapshot/start/output/retries/typed FKs/audit/reports, container restart persistence, database outage safe errors and secret-log checks |
| Backup restoration | PASS, **19/19** preserved old table/view signatures | Main Phase 8 custom dump restored into isolated phase9_backup_check; all signatures match |
| Restored backup upgrade | PASS, **19/19** preserved signatures, nine migrations and healthy API | Current backend started against restored Phase 8 DB with bootstrap disabled |
| Main local upgrade | PASS, **19/19** preserved signatures | fresh pre-upgrade backup; demoproject upgraded in place; source data matches verified restore; nine migrations, two existing RawMaterial products, no synthetic BOM/production orders |
| TR/EN source audit | PASS | static JSX/literal translation keys checked; dynamic manufacturing statuses/actions/fields mapped |
| Visual review | PASS | actual completed production detail at 390 px; material cards show required/consumed/current/shortage without viewport overflow |
| Mermaid | PASS, **21** rendered diagrams | Includes two new manufacturing diagrams |
| Local documentation links | PASS | nineteen Markdown files and balanced fences; final link check rerun below delivery |
| Source secret scan | PASS, **0 findings** | Gitleaks isolated Git-eligible export with redacted output; ignored private/test-runtime data excluded |
| Existing AI code | PASS, **9/9 byte-identical** | seven AI service files, AiController and AiAssistant.tsx compared to Phase 8 source baseline |
| Existing license/publication constraints | PASS | NOTICE unchanged, PUBLICATION_PLAN absent, no remote push |

Ignored local evidence is under `.tools/phase9/`: backend-tests.log, build.log, frontend-build.log, manufacturing-browser.log, regression-browser.log, warehouse-procurement-browser.log, ai-browser.log, api-verification.log, compose-fresh-final.log, compose-upgrade.log, compose-delivery.log, data-before/pre-upgrade/after.json, backup-restored-signatures.json, backup-upgraded-signatures.json, main-before.dump, main-before-final.dump, ai-integrity.json, gitleaks.json, mermaid.log and production-mobile.png. Private credentials and binary backups are not source/publication artifacts.

## Verification — acceptance criteria

| Criterion | Status | Actual evidence |
|---|---|---|
| AC-01 BOM CRUD works | PASS | create/draft edit/list/detail/delete integration cases; confirmed delete browser case |
| AC-02 BOM versioning | PASS | distinct versions and immutable history tests |
| AC-03 One active BOM | PASS | concurrent activation plus raw SQL partial unique index rejection |
| AC-04 Active BOM cannot edit | PASS | API and direct SQL immutable recipe/header tests; read-only UI |
| AC-05 Persist material snapshot | PASS | order creation/snapshot tests and real browser workflow |
| AC-06 Correct calculation | PASS | scaling, fractional rejection, Int32 overflow tests and backend preview |
| AC-07 Source-warehouse shortages | PASS | selected-depot shortage/report tests with stock in another depot |
| AC-08 Draft does not change stock | PASS | explicit creation balance/movement assertions |
| AC-09 Release does not change stock | PASS | explicit release balance/movement assertions |
| AC-10 Atomic start consumption | PASS | successful consumption and multi-material rollback/race tests |
| AC-11 Insufficient stock prevents start | PASS | 409, retained Released state and unchanged balances |
| AC-12 Consumption movements | PASS | exact type, product, warehouse, quantity, order FK and actor assertions |
| AC-13 Completion increases finished stock | PASS | planned=produced and destination stock asserted in API/browser |
| AC-14 Selected destination | PASS | zero source output and exact destination output assertions |
| AC-15 Repeated start safe | PASS | sequential retries and competing API instances, one consumption per material |
| AC-16 Repeated completion safe | PASS | sequential retries and competing API instances, one output |
| AC-17 Concurrent stock cannot go negative | PASS | production/production, sales and transfer races; PostgreSQL locks and bounds |
| AC-18 Prior workflows remain functional | PASS | all 351 earlier backend tests plus 49 earlier browser cases and API verification |
| AC-19 RBAC | PASS | anonymous/Viewer denial, Manager draft/start/complete, Admin activation, revocation |
| AC-20 Audit | PASS | actor, action and exactly-once records; failed FK rolls back start/complete |
| AC-21 EN/TR frontend | PASS | recipe/form/detail/shortage/actual completed-order language tests, source audit |
| AC-22 Existing data preserved | PASS | nineteen old table/view signatures equal before/after main upgrade and restore |
| AC-23 Fresh and upgrade migrations | PASS | real fresh Testcontainers/Compose, populated Phase 8 upgrade and restored backup |
| AC-24 Docker Compose startup | PASS | isolated clean startup and healthy in-place main upgrade |
| AC-25 Automated tests | PASS | 413 backend and 65 browser cases, none skipped |
| AC-26 Accurate documentation | PASS | implementation-backed manufacturing design, API/RBAC/DB/report docs, rendered diagrams and local-link checks |

## Remaining Limitations

This phase deliberately excludes fractional units/conversions, nested or semi-finished BOM explosion, reservations/allocation, forecasts, planned receipts/lead times, safety-stock planning, scheduling/work centers, partial completion, scrap, quality rejection, production reversal and cost accounting. Released orders are not guaranteed material simultaneously; availability/shortages are current per-order snapshots, not aggregated MRP. Whole-unit stock remains Int32 capped; coarse product locks and global recipe governance prioritize correctness over throughput. The frontend retains the existing non-blocking Vite chunk-size advisory (main chunk approximately 590 kB); build succeeds.

Public deployment, load benchmarking, live external AI-provider behavior and remote GitHub CI for this unpublished phase are **UNVERIFIED**. No claim of production certification or full enterprise MRP is made. Disposable test projects are stopped with their volumes retained; the user's main local application stays running.

# Phase 8 verification — 2026-10-10

Phase 8 is implemented and running locally at [Warehouses](http://localhost:3000/warehouses), [Stock transfers](http://localhost:3000/stock-transfers) and [Warehouse report](http://localhost:3000/reports/warehouses). Authentication is required. GitHub was not pushed.

## Implementation summary

Four entities/tables were added: Warehouse, WarehouseInventory, StockTransfer and StockTransferItem. The PostgreSQL `Inventories` view now reads SUMs of authoritative warehouse balances; frozen legacy openings retain their original IDs. Admin manages warehouses/default/status; Admin and Manager perform transfers and stock operations; Viewer reads. TR/EN list/form/detail/report screens include warehouse stock, shared manual controls, source availability, sales fulfillment and receipt destinations. Transfer completion writes both balances, two typed movements per item, status/actor/time and audit atomically; a repeated completion returns the original result.

The full design, ER diagram, transfer flow, lock order, migration assumptions, API/authorization matrix, reporting definitions, limitations are in [Warehouse management](docs/WAREHOUSE_MANAGEMENT.md). README EN/TR, Architecture, Database, Inventory, API, Reporting, Procurement, Authorization and Audit documentation were updated. Existing NOTICE conditions were retained; `docs/PUBLICATION_PLAN.md` remains absent.

## Database migration and data preservation

**PASS** — migration `20261010081430_MultiWarehouse`, eighth migration, applied on fresh Testcontainers/Compose databases and upgraded the existing local Compose database without volume resets. Existing quantities/minimum levels/timestamps/IDs copy once to MAIN; previous Orders, GoodsReceipts and InventoryMovements receive MAIN as an explicit administrative association because no historical warehouse metadata existed. Original non-warehouse fields stay unchanged; no fabricated stock movements are generated. Legacy receipt fingerprints remain version 1 and replay without adding stock; new fingerprints include destination warehouse as version 2.

**PASS** — local pre-upgrade custom-format backup restored into an isolated PostgreSQL database. All 14 existing table row signatures match that backup and also match after the local upgrade (excluding only the three additive warehouse/transfer/fingerprint-version fields). The regression migration fixture additionally preserves nonzero opening stock, order deductions, products, orders, purchase orders, immutable receipt/header/items, and a legacy receipt idempotency replay; running migrations twice does not duplicate stock. Existing local state remains two inventory/product records, one MAIN warehouse, and zero pre-existing movement records. Bootstrap remains disabled. Backups, signatures and test logs are ignored `.tools/phase8/` artifacts.

## Executed checks

| Check / actual command | Result | Evidence |
| --- | --- | --- |
| `dotnet test Catalog.slnx` with bundled SDK and `DOCKER_HOST=npipe://./pipe/dockerDesktopLinuxEngine` | **PASS** | 351/351, zero skipped, 44.491 s; includes 53 WarehouseTests plus the enhanced migration and all previous modules |
| `npm --prefix frontend run build` | **PASS** | TypeScript and Vite production build; non-blocking bundle-size warning remains |
| Playwright `catalog.spec.ts business.spec.ts inventory.spec.ts security.spec.ts reporting.spec.ts localization.spec.ts states.spec.ts ui.spec.ts` | **PASS** | 20/20, 58.0 s |
| Playwright `warehouses.spec.ts procurement.spec.ts` | **PASS** | 22/22, 24.9 s (10 new warehouse tests + 12 procurement regressions) |
| Playwright `ai.spec.ts` | **PASS** | 7/7, 12.4 s |
| `docker compose up -d --build --wait` with isolated project/ports 4200 and 6280 | **PASS** | PostgreSQL, API, frontend healthy; fresh schema installation |
| `docker compose --env-file .tools/phase4/runtime.env up -d --build --wait` with project `demoproject` | **PASS** | Existing local database upgraded; PostgreSQL, API, frontend healthy at ports 5080/3000 |
| Live authenticated `/api/warehouses?isDefault=true`, `/api/reports/warehouses`; frontend `/warehouses` | **PASS** | One MAIN default, valid report, HTTP 200 SPA |
| `node scripts/verify.mjs` against **isolated** `catalogwarehouse20261010`, API 6280/frontend 4200 | **PASS** | CRUD, RBAC/revocation, stock, orders, procurement, warehouse transfer, replay, conservation, audit, reporting, persistence/restart, safe unavailable-database errors, credential-free logs |
| `dotnet build Catalog.slnx --no-restore` | **PASS** | Zero warnings and errors |
| Gitleaks on an isolated export of 216 Git-eligible files | **PASS** | Zero findings; NOTICE and AI sources unchanged |
| Static TR/EN key and JSX audit | **PASS** | No raw untranslated static JSX or missing literal translation keys |
| Markdown links/fences and Mermaid browser rendering | **PASS** | 18 documents; all local links and 19 Mermaid diagrams valid |
| AI source byte comparison against Phase 7 | **PASS** | 8 backend AI/controller files and frontend AiAssistant.tsx are byte-identical |

Frontend verification covers required/default warehouse selection, same-warehouse rejection, source inventory display, fractional/duplicate/empty line validation, loading/error/retry states, role-protected controls/routes, explicit sales and receipt warehouse payloads, English/Turkish switches and mobile transfer details. The global inventory minimum form loads the selected depot threshold, keeps it independent of the company SUM, and invalidates the aggregate after a depot update. Existing product status test now scopes its status badge because product warehouse stock adds further badges. Reporting waits for asynchronous ResizeObserver measurements before asserting the same no-page-overflow condition; the width assertion remains intact. API auth rate limits were unchanged; browser suites restart the isolated API between batches.

## Concurrency and transaction evidence

**PASS** — real PostgreSQL/Testcontainers exercises competing 80/60 transfers from 100 (exactly one succeeds), same-transfer concurrent completion, separate API-instance completion, transfer versus sales confirmation, transfer versus manual stock out, receipt versus transfer, opposing multi-product transfers with reversed input order, source insufficiency across multiple lines, company Int32 overflow, and an actual database FK failure on audit insert. Assertions check final quantities, unchanged global SUM, typed warehouse movements, Draft preservation on rollback, and no duplicate completion audit. Completed transfer header/items and inventory movements are database-immutable. All writers use document → sorted warehouse → sorted product → sorted balance locks.

## Acceptance criteria

| Criterion | Result | Verified behavior |
| --- | --- | --- |
| AC-01 Warehouse CRUD | **PASS** | Admin create/read/edit/status/default; deliberate soft deactivation preserves history |
| AC-02 One default warehouse | **PASS** | Partial unique index, deferred exact-one check, concurrent switches |
| AC-03 Non-negative warehouse stock | **PASS** | DB check and locked writer validation |
| AC-04 Migrate without duplication | **PASS** | Opening copy to MAIN, repeat migration stable |
| AC-05 Global stock equals warehouse SUM | **PASS** | Read-only aggregate view, integration and report assertions |
| AC-06 Drafts do not change stock | **PASS** | Create/edit/cancel balance assertions |
| AC-07 Deduct source | **PASS** | Completion source balances verified |
| AC-08 Increase destination | **PASS** | Completion destination balances verified |
| AC-09 Preserve total product stock | **PASS** | Single/multi-product and concurrent conservation assertions |
| AC-10 Reject insufficient source | **PASS** | HTTP 409, Draft remains, no destination stock |
| AC-11 Atomic completion | **PASS** | Multi-line shortage and database audit failure roll back everything |
| AC-12 Repeat completion safe | **PASS** | Sequential/concurrent/cross-instance replay, two movements only |
| AC-13 Concurrent non-negative stock | **PASS** | Transfer, sales, manual, receipt and opposing-transfer races |
| AC-14 Transfer movement history | **PASS** | TransferOut/In, own before/after, correct warehouses/shared typed FK |
| AC-15 Selected sales warehouse | **PASS** | Only selected stock is used; no borrowing; UI payload asserted |
| AC-16 Return to original warehouse | **PASS** | Actual deduction warehouse restored after default changes |
| AC-17 Selected receipt warehouse | **PASS** | Partial receipts into different depots; changed-key-payload rejection; UI selection |
| AC-18 Warehouse-aware manual stock | **PASS** | Shared form/warehouse endpoints, in/out/adjust/minimum, global reads |
| AC-19 RBAC | **PASS** | Anonymous 401, Viewer mutation 403, Manager warehouse-admin 403, permitted operations |
| AC-20 Audit | **PASS** | Warehouse governance and transfer lifecycle, completion actor/time/products/quantities, atomic failure |
| AC-21 TR/EN frontend | **PASS** | New screens and existing modules, language switches and static key audit |
| AC-22 Warehouse reporting | **PASS** | Database SUM/count comparison, missing zero pairs, filters/pagination |
| AC-23 Existing modules | **PASS** | All prior backend tests, 39 previous frontend tests and full API verifier |
| AC-24 Data-preserving migrations | **PASS** | Real fresh/upgrade tests and live 14-table signature preservation with restored backup |
| AC-25 Docker startup | **PASS** | Fresh isolated Compose and existing local upgrade healthy |
| AC-26 Automated tests | **PASS** | 351 backend and 49 frontend tests passed, no skipped backend tests |
| AC-27 Documentation | **PASS** | EN/TR README and architecture/database/API/module docs, two new diagrams, 12 Q&A |

## Remaining limitations / unverified external actions

Warehouse quantities and aggregate minimums retain the existing Int32 contract limit. Product locks conservatively serialize same-product writes across warehouses. Global low stock compares aggregated minimums, so consult warehouse reports for individual depot shortages. MAIN association on historical records is an assumption, not recovered historical warehouse data. Automatic migration downgrade is intentionally refused; use a verified backup or reviewed forward migration.

Partial/in-transit/reversible transfers, reservations, bins, batches/serials and hard warehouse deletion are outside this phase. Product selection follows the existing ERP all-products lookup; warehouse and inventory listings are paginated. Production asset build still emits the existing 500 kB chunk-size advisory. **UNVERIFIED** — production/load testing, a live external AI-provider request, remote deployment and GitHub CI/publication for this phase. None are claimed as passed; GitHub was not pushed.

---

# Phase 7 implementation and verification — 10 October 2026

Implemented locally on the existing Phase 6 application. Main application:
http://localhost:3000. No Phase 7 GitHub push, PR or remote CI run was performed.
The earlier Phase 3–6 publication remains separate from this local verification.
No paid AI request was made; the existing eight AI source files match the published
Phase 6 snapshot. Existing NOTICE conditions and the absence of PUBLICATION_PLAN.md
are preserved.

## Implemented features and architecture

Supplier search, active filtering, pagination, detail/create/edit/status; Draft PO
creation/editing with entered purchasing costs and server-calculated totals;
submission, pre-receipt cancellation, partial/full goods receiving; immutable
receipt detail/history and typed inventory movement references. All new pages
support EN/TR, locale dates/currency, role presentation and confirmation dialogs.
Separate current procurement summary and paged supplier/status/date PO report.

`SupplierService`, `ProcurementService` and protected controllers extend the single
API and existing EF/JWT/audit services. React adds a lazy procurement page module,
shared typed contracts/Zod schemas and existing query-cache invalidation. No
microservices, generic repositories, CQRS or additional infrastructure was added.
[Full design, ERD and lifecycle](docs/PROCUREMENT.md).

## Database, business rules and consistency

Migration `20261010072904_Procurement` (seventh) adds Suppliers, PurchaseOrders,
PurchaseOrderItems, GoodsReceipts and GoodsReceiptItems, restrictive relationships,
two number sequences, unique identifiers/line/user-key indexes, bounded quantity/
cost checks, receipt immutability and receipt-line ownership triggers. Existing
InventoryMovement enum values are retained; PurchaseReceipt and a separate real
GoodsReceiptId FK are appended. The existing Orders reference remains intact.

Only Draft is editable; active references are required at draft/save/submission.
Draft and submission change no stock. Only Draft/Submitted before any receiving
can cancel. Receipt quantities derive PartiallyReceived/Received. There is no
arbitrary status setter or posted receipt edit/delete. Purchasing UnitCost is a
snapshot independent of Product.Price; decimal totals are calculated in the API.

Receipt posting starts a single PostgreSQL Read Committed transaction, acquires a
user/key advisory lock, checks replay, locks the PO FOR UPDATE, validates remaining
quantities, and locks inventory in sorted product UUID order. Receipt/items,
received totals, stock, typed immutable movements, PO status and allowlisted audit
commit together. Failures roll everything back. Two separate API applications
sharing PostgreSQL were exercised, not just concurrent tasks in one application.

A database-unique authenticated user + UUID key identifies the logical operation.
SHA-256 fingerprint covers PO ID, trimmed notes and sorted item ID/quantity pairs.
A matching retry returns the original receipt (200), including after full receipt;
a changed payload returns409. First creation returns201 with Location. Failed
transactions reserve no key. The client keeps the operation key for same-form
retries and replaces it after a changed reviewed payload. The key is not persisted
across page reload/navigation; after an uncertain response, check history or retry
in the same form before starting another operation.

BusinessRead permits all three roles. BusinessWrite permits Admin/Manager. JWT
active/role/security-version revocation remains enforced. Actor IDs are taken from
JWT sub. Supplier Create/Update/Activate/Deactivate, PO Create/Update/Submit/Cancel/
Receive and GoodsReceiptPost are audited atomically. Explicit field allowlists
exclude contacts, notes, request payloads, operation keys and fingerprints.

## Commands actually executed

PowerShell used the repository's `.tools/dotnet` SDK 10.0.401 and runtime10.0.12.
Testcontainers used `DOCKER_HOST=npipe://./pipe/dockerDesktopLinuxEngine`.
Ignored environment files supplied local credentials and are never source artifacts.

| Actual command/check | Result |
| --- | --- |
| `dotnet build Catalog.slnx --no-restore` | PASS, zero warnings/errors |
| `dotnet test Catalog.slnx --no-build` | PASS, 298/298, zero skipped, 90.831 s final run |
| New procurement tests | PASS, 49 real PostgreSQL tests including separate API instances |
| New Phase 6→7 upgrade test | PASS, all nine old tables including stock/movement/audit history unchanged |
| `npm --prefix frontend run build` | PASS, TypeScript and Vite; existing nonblocking main chunk warning, 513.87 KB / 154.53 KB gzip |
| `docker compose --env-file .tools/phase4/runtime.env build` | PASS, backend/frontend |
| `docker compose --env-file .tools/phase4/runtime.env up --build -d --wait` | PASS, main frontend/backend/PostgreSQL healthy |
| Disposable `catalogprocurement20261010` Compose `up --build -d --wait` | PASS, fresh database applies all seven migrations; ports4100/6180 |
| Chromium `procurement.spec.ts` | PASS, 12/12, 17.8 s |
| Chromium eight existing ERP spec files | PASS, 20/20, 1.9 min |
| Chromium `ai.spec.ts` | PASS, 7/7, 10.8 s; enabled-provider states mocked, actual disabled endpoint checked |
| `node scripts/verify.mjs`, isolated project only | PASS, core auth/RBAC/CRUD/Swagger/stock/order/audit, added procurement workflow/audit/replay/report, restart persistence, safe failure/log checks |
| `dotnet-ef migrations has-pending-model-changes --project backend/Demo.Api --no-build` | PASS, no pending model changes; older EF tool notice only |
| Main database upgrade preservation | PASS, SQL backup first, nine full table hashes equal before/after; seven migrations; no procurement fixtures in main |
| Main authorized login/read smoke | PASS, old dashboard/products/inventory plus suppliers, POs, report, summary and disabled AI status |
| `node .tools/i18n-audit.mjs` | PASS, literal translations/JSX text |
| Markdown links/fences and Mermaid render | PASS, 17 documents, 174 links before this report update, 17 diagrams rendered |
| `node .tools/review-repo.mjs` | PASS, 201 source candidates, no private/generated files, credential patterns or trailing spaces |
| Gitleaks8.30.1 on Git-eligible source export | PASS, 201 files, approximately1.30MB, zero detected leaks |
| Phase 7 remote GitHub CI | UNVERIFIED / not requested; no push performed |

The Chromium commands ran `catalog-e2e` on the disposable Compose network with
FRONTEND_URL=http://localhost:4100 and API_URL=http://localhost:6180. The procurement,
ERP and AI groups were separated, restarting only the disposable backend between
groups to respect the existing auth rate limit. The API verification uses
COMPOSE_PROJECT_NAME=catalogprocurement20261010 and never stops the main database.
It intentionally restarts/stops that disposable database to verify persistence and
safe error behavior. Synthetic records are retained there for immutable audit.

Initial attempts were not counted as PASS: Docker was stopped, then an incorrect
named-pipe URI was corrected. The new upgrade fixture referenced a non-existent
legacy Orders column and was corrected. Two browser selectors were corrected,
including an explicit accessible status-filter label. All final runs above passed.

## Automated coverage

Suppliers: validation, normalized unique code, edit/status/filter/page and history
FK protection. PO: missing/inactive references, empty/duplicate/invalid lines,
zero/negative/precision costs, server totals, no draft/submission stock, retained
line IDs, Draft-only edits, transitions and parallel unique document numbers.
Receipt: partial/full quantities, wrong/duplicate/empty lines, missing key,
zero/negative/excess quantities, Draft/Cancelled rejection, actor/typed movements,
immutable receipt/item DB triggers, pre-receipt cancellation and final status.
Atomicity: forced audit failure and second-product stock overflow roll back all
changes; failed-key retry succeeds. Concurrency: competing30 receipts after60/100,
multiple POs/products, parallel matching/changed keys, canonical reordered items,
trimmed notes and key scope per user. Separate API hosts share PostgreSQL locks.
Auth: anonymous reads/writes, Viewer writes, Manager flow, Admin receipt, read-only
Viewer and revoked active status. SQL reporting/summary/filtering tested directly.

Frontend: validation and cost preview, duplicate options, weighted progress,
remaining quantities/default zero, form loading/empty/error/retry, localized UI,
permission guards, confirmation/pending duplicate prevention, stable retry keys,
changed payload keys, immutable receipt links, report filter requests, mobile
layout and actual Manager supplier→Draft→Submitted→partial→full→inventory flow.
Screenshots were visually inspected; fixture screenshots are labelled in their
filenames and retained only under ignored `.tools/phase7`.

## Final acceptance criteria

| Criterion | Result | Executed evidence |
| --- | --- | --- |
| AC-01 Supplier CRUD and active status | PASS | PostgreSQL supplier tests, real Manager browser and API verify (deactivation replaces physical delete) |
| AC-02 Draft creation | PASS | PO tests and real form |
| AC-03 Backend totals | PASS | Entered1.25×3=3.75 versus sales price99, edit and real22.50 total |
| AC-04 Creation does not change stock | PASS | Stock assertions before/after Draft and submission |
| AC-05 Draft submission | PASS | Active-reference tests and browser confirmation |
| AC-06 Partial receipt | PASS | 6/10 receipt, partial state and stock |
| AC-07 Full receipt | PASS | Remaining4/10, final Received and total stock10 |
| AC-08 Never exceed ordered | PASS | Validation/check constraints, over-receiving tests |
| AC-09 Atomic receipt/inventory | PASS | Actual audit-trigger failure and multi-product overflow rollback |
| AC-10 Correct inventory movements | PASS | Quantity/actor/type/FK assertions, real inventory receipt links |
| AC-11 Duplicate receipt safety | PASS | Six parallel same-key calls, one201 and five200, one stock increase |
| AC-12 Concurrent over-receive safety | PASS | Two30 after60/100; one201, one409; separate API hosts too |
| AC-13 Authentication/RBAC | PASS | Anonymous/Viewer negative tests, Manager/Admin positive and revocation |
| AC-14 Procurement audit | PASS | API verify asserts every supplier/PO/receipt action in PostgreSQL |
| AC-15 TR/EN React screens | PASS | Browser switch/form/detail/mobile and static translation audit |
| AC-16 Existing modules | PASS | 248 original backend tests,20 ERP and7 AI browser tests plus API verify |
| AC-17 Data-preserving migration | PASS | Fresh/upgrade tests and all nine main table hashes equal |
| AC-18 Compose startup | PASS | Fresh disposable stack and preserved main volume healthy |
| AC-19 Automated tests | PASS | 298 backend and39 frontend checks, no skipped |
| AC-20 Actual documentation | PASS | API/schema/design/README updates, link/fence and diagram checks |

## Remaining limits

No failing local acceptance criterion remains. Existing Vite main-bundle size
warning is nonblocking. Procurement has no invoice/payment/tax/FX/return/valuation
workflow or CSV export; these are documented scope limits. Posted receipt
correction requires a future compensating business flow. No operation-key TTL or
browser reload persistence is implemented. Number sequences may contain gaps.
Reports use current supplier/product labels, while ordered quantities/costs remain
snapshots. Database locks require all receipt writers to follow the service
transaction contract; this phase does not claim protection from a privileged
operator disabling constraints/triggers. Live paid AI and Phase 7 remote CI remain
unverified by design.

---

> Publication note — 9 October 2026: this source snapshot includes the locally verified Phase 3–6 changes. The phase reports below record validation before publication; subsequent GitHub Actions results are tracked separately from these local checks.

# Phase 6 verification — 9 October 2026

The optional AI Business Assistant was implemented locally on the existing Phase 5
application. No GitHub push or remote CI run was performed. The main application is
running at http://localhost:3000 with AI disabled and no provider key configured.
No live paid provider request was made.

| Check actually executed | Result |
| --- | --- |
| `dotnet build Catalog.slnx --no-restore` | PASS, zero warnings/errors |
| `dotnet test Catalog.slnx --no-build` | PASS, 248/248, zero skipped, 40.032 s |
| Focused AI + official SDK transport tests | PASS, 81/81; fake model/HTTP, real PostgreSQL, no network/provider charges |
| `npm run build` in frontend | PASS, TypeScript and Vite production build; nonblocking existing main-bundle size warning around 504 KB |
| `docker compose --env-file .tools/phase4/runtime.env build` | PASS, backend/frontend images |
| `docker compose --env-file .tools/phase4/runtime.env up --build -d --wait` | PASS, main backend/frontend/PostgreSQL healthy without AI credentials |
| Fresh `catalogai20261009` Compose stack on ports 4000/6080 | PASS, six existing migrations, isolated synthetic data |
| `node scripts/verify.mjs` on isolated stack | PASS, auth/RBAC, AI anonymous/Viewer-disabled checks, CRUD, Swagger, stock/order/audit, restart persistence and safe errors/logging |
| Chromium AI UI group | PASS, 7/7, including real disabled endpoint and mocked enabled-provider states |
| Chromium existing ERP regression group | PASS, 20/20, 53.0 s |
| `migrations has-pending-model-changes` | PASS, no model drift; EF tool 10.0.3 prints its older-than-runtime notice |
| Existing main data preservation | PASS, all nine table signatures identical INCLUDING CompletedAt, before and after deployment |
| Existing main migration count | PASS, remains six; no Phase 6 migration/table added |
| Main login/dashboard/AI status smoke | PASS, authorized login, summary and safe disabled/unconfigured status |
| Static JSX/literal translation audit | PASS |
| Markdown links/fences and Mermaid render | PASS, sixteen documents and fifteen diagrams including AI architecture |
| Source hygiene and Gitleaks 8.30.1 | PASS, Git-eligible files only, no generated/private files or detected credentials |
| Live OpenAI endpoint/model accuracy, retention/residency | UNVERIFIED; no suitable key plus explicit live-test authorization was supplied |
| Azure OpenAI, persistent chat, document RAG | Not implemented; documented future extension only |

The first combined browser run had 24 passes and three failures: one AI assertion
expected different wording from the existing Turkish 403 translation, and two later
security tests exhausted the real 20-authentication-attempts/IP/minute limiter.
The assertion was corrected. Final AI and ERP groups were run separately, restarting
only the disposable backend between groups; the production limiter was not weakened.
All 27 distinct browser cases passed in the final groups. The AI group was rerun after
adding a successful Turkish response assertion. Enabled UI answers are explicitly
mocked and do not establish live-provider model quality.

## Security and contract evidence

The 81 new backend cases cover all nine registered tools against real PostgreSQL,
Viewer/Manager/Admin reads, anonymous protection, revoked/inactive/stale-role sessions,
revocation during the model wait and final delivery, direct policy denial, unsupported
SQL/write/audit/URL tool names, malicious product/user text, strict unknown/duplicate
argument rejection, invalid limits/dates/types, bounded rows and tool rounds/calls.
They also cover empty real data with deterministic no-fabrication fallback, server
sources, current inventory, customer pseudonyms/contact exclusion, no JWT/key/hash
forwarding, input/output lengths, minute/day quotas, UTC reset, independent user quotas,
missing/disabled/invalid configuration, cancellation, timeout, safe query/provider
failures and unchanged business records/audit during AI reads.

Official SDK tests intercept HttpClient and verify the actual serialized strict tool
schemas, bounded output setting, disabled parallel calls, backend-only Authorization
header, correlation IDs, final JSON/language parsing, malformed wire responses and
sanitized 400/401/429/500 provider errors with no retries. These are real SDK adapter
tests with fake HTTP responses, not live service tests. No paid calls are embedded in CI.

The seven AI browser cases cover real Viewer navigation/default-disabled status,
empty/whitespace/overlong input, suggestions, loading/duplicate prevention, answer and
source rendering, plain-text handling of hostile HTML/Markdown, EN/TR labels and
successful responses, mobile overflow, cancellation, 429/503/403, configuration-needed
state and 401 session clearing. Reload discards conversations and no chat storage key
is created. Desktop/mobile screenshots were visually inspected; enabled screenshots
under ignored `.tools/phase6` contain deliberately synthetic UI fixture answers.

## Data and runtime preservation

The main database still has two products, two inventories, one order, ten users and
zero inventory movements. All nine data-table signatures remain identical, including
the nullable legacy completion field. The six migration records remain intact; EF
reports no model change. A private pg_dump and signatures are ignored under
`.tools/phase6`. Test mutations ran only against disposable Testcontainers/Compose
PostgreSQL. The main bootstrap remains disabled. AI activation is optional and is
not required for any existing ERP module.

## Acceptance criteria

| Criterion | Status | Executed evidence |
| --- | --- | --- |
| AC-01 Existing ERP functional | PASS | 248 backend tests, 20 ERP browser cases, Docker verify |
| AC-02 Authorized AI page | PASS | Real Viewer navigation; route uses existing read permission |
| AC-03 Backend chat API | PASS | Endpoint integration and browser POST contract |
| AC-04 Backend-only credentials | PASS | SDK Authorization/header/body tests, Compose/frontend review, source scan |
| AC-05 Real authorized report data | PASS | Nine tools execute PostgreSQL reporting queries with fake model |
| AC-06 No arbitrary SQL/writes | PASS | Fixed dispatch; rejected unknown SQL/write/audit/URL calls; read-only/audit checks |
| AC-07 Validated tool arguments | PASS | Strict schemas, unknown/duplicate/type/date/limit rejection |
| AC-08 Current permissions | PASS | Active/SecurityVersion/role rechecks during tool and final-answer waits, policy denial |
| AC-09 Actual executed sources | PASS | Server source names/parameters/paths verified; no-tool source list empty |
| AC-10 TR/EN support | PASS | SDK response parsing, server language contract and both UI languages; live linguistic quality UNVERIFIED |
| AC-11 Safe provider failures | PASS | Fake provider/HTTP 429, unavailable, malformed, timeout and cancellation tests |
| AC-12 Rate/bounded execution | PASS | Per-user minute/day quotas, UTC resets, bounded lists, five calls/two rounds |
| AC-13 Automated tests | PASS | 248 backend and all 27 browser cases in final groups |
| AC-14 Docker without AI key | PASS | Fresh and main stacks healthy; real disabled status/chat verified |
| AC-15 Data/migrations preserved | PASS | Nine unchanged table signatures, six migrations, no model drift |
| AC-16 README/AI docs | PASS | Both READMEs, architecture/security/API/authorization docs, link/fence/diagram checks |

## Reproduction and limitations

Use the .NET SDK matching global.json, Docker and the existing frontend package lock:

```powershell
dotnet build Catalog.slnx
dotnet test Catalog.slnx
cd frontend
npm ci
npm run build
```

The existing development docs describe bootstrapping a disposable Compose stack and
running the Playwright Docker image. Run `ai.spec.ts` and the other tests separately
if the real authentication quota would be exceeded; do not disable the limiter.
The container runner forwards arguments, for example:

```text
node tests/container-runner.mjs ai.spec.ts
node tests/container-runner.mjs --grep-invert "AI "
```

Local execution used ignored `.tools/dotnet` and `.tools/ef` installations, the private
ignored Phase 4 bootstrap environment for disposable tests, and a bootstrap-disabled
runtime environment for the main stack. These credential files are not repository
artifacts. Normal `docker compose up --build` defaults AI off and keeps the database
volume. No key is necessary for ordinary verification.

No live-provider answer-quality, geographical processing or data-retention guarantee
is claimed. Nonempty model answers may misquote authorized results; users must review
source reports. Reports are multiple current reads, not one transactional snapshot.
Stock coverage assumes constant historical completed demand. Unsupported actions,
user/audit tools, arbitrary SQL, document queries and persistent conversation context
remain unavailable. Quotas are per process and reset on restart; production needs
shared enforcement and provider spend controls. Up to three bounded provider calls
can incur costs per question when enabled.

[AI architecture, tool matrix and configuration](docs/AI_ARCHITECTURE.md) ·
[AI privacy/security and residual risks](docs/AI_SECURITY.md).

---

# Phase 5 verification — 9 October 2026

Executive dashboard, advanced reports, bounded CSV and measured date-query indexes
were implemented locally on top of the existing Phase 4 application. GitHub push
and Phase 5 remote CI were not performed.

| Check | Actual result |
| --- | --- |
| `dotnet build Catalog.slnx --no-restore` | PASS, zero warnings/errors |
| `dotnet test` | PASS, 167/167, zero skipped, final run 28.537 s |
| `npm run build` in frontend | PASS, TypeScript + Vite production build |
| `docker compose --env-file .tools/phase4/runtime.env build` | PASS, both application images |
| `docker compose --env-file .tools/phase4/runtime.env up --build --wait` | PASS, existing main stack; backend/frontend/postgres healthy |
| Fresh isolated Compose startup | PASS, six committed migrations applied |
| `node scripts/verify.mjs` on isolated stack | PASS, auth/RBAC, CRUD, Swagger, restart persistence, inventory transitions, safe errors/logging |
| Full Chromium suite in Docker | PASS, 20/20 in 57.0 s |
| Optional benchmark harness | PASS, 20,000 orders / 80,000 items / 100,000 movements, six actual EF commands, EXPLAIN ANALYZE BUFFERS, before/after and repeated pair |
| `migrations has-pending-model-changes` | PASS, no model drift |
| Static JSX/literal translation audit | PASS |
| Mermaid rendering | PASS, 14 diagrams including reporting flow |
| Screenshot capture script | PASS, twenty real synthetic-data captures; four new report/dashboard images |
| Existing main data preservation | PASS, identical signatures for all nine pre-existing data tables, excluding newly added nullable CompletedAt |
| Main legacy completion policy | PASS, one completed legacy order retained null CompletedAt and was excluded from period sales |
| Bootstrap credentials in main container | PASS, bootstrap disabled, bootstrap password absent |
| Source/credential hygiene | PASS, 175 Git-eligible source/evidence files; Gitleaks 8.30.1 found zero leaks |

The main database retained two products, two inventories, one order, ten users and
zero movements; its migration count increased from four to six. A private pre-upgrade
pg_dump and data signatures are ignored under `.tools/phase5`. No application data
was used in the performance fixture. Synthetic test/capture volumes remain separate.

New PostgreSQL tests cover completion-date sales versus pending/cancelled/legacy,
stored line prices, actual daily/monthly grouping, zero buckets, half-open UTC and
offset-equivalent bounds, grouped product/customer totals, current inventory,
creation-date status counts, filtered counts/paging/stable ties, invalid filters,
all thirteen anonymous reads/exports, Viewer exports, CSV BOM/quotes/newlines/formula
protection, 10,001-row export rejection and completion timestamp/audit idempotence.
The upgrade test preserves a pre-Phase-5 completed order without fabricating a date.

The browser suite verifies real KPI/API values, Istanbul local-midnight conversion,
period presets/custom empty periods, all four reports, customer/product/category/
status/stock filters, paging/page-size changes, CSV download, Viewer read access,
Admin-only audit denial, mobile overflow, TR/EN and error/retry states. Previous
catalog, stock, role-management, audit and session regression suites still pass.

[Reporting contract](docs/REPORTING.md) ·
[Measured results, raw plans, repeated samples and reproduction](docs/PERFORMANCE.md).
The first benchmark pair measured sales totals at 1.456 → 0.325 ms and filtered
orders at 2.739 → 0.631 ms. Additional samples confirm the direction while showing
host variation. Inventory and movement queries have no claimed improvement.
Cold-cache timings, production HTTP latency, write-throughput impact, non-Chromium
browsers and Phase 5 remote CI remain UNVERIFIED.

---

# Verification record

## Phase 4 authorization, users and audit — 9 October 2026

The existing application was extended locally. Phase 3/4 source has not been pushed;
GitHub publication, remote CI and clone/run of this revision are **UNVERIFIED**.
Historical publication results below concern previously published commits only.

### Implemented

- Predefined Admin/Manager/Viewer roles and seven centralized ASP.NET Core policies
  enforce the [permission matrix](docs/AUTHORIZATION.md#implemented-permission-matrix).
- Public registration remains public but always creates Viewer. DTOs ignore privilege
  overposting. Protected requests check the current database user, active flag,
  SecurityVersion and role. Role/status/email changes invalidate old tokens.
- Admin-only user list/detail/create/basic edit/role/status endpoints and localized
  responsive screens. Admin creation and access changes have explicit confirmations;
  self-role changes/self-deactivation are blocked. There is no physical user deletion.
- PostgreSQL advisory transaction lock serializes user writes and bootstrap across
  processes. The acting Admin is revalidated after waiting; the last active Admin is
  protected even with simultaneous mutual demotion/deactivation attempts.
- Configuration-only one-time Admin bootstrap, with no default password/public
  endpoint. It never promotes an existing account, preserves passwords on restart,
  and retains an internal provisioned-account marker.
- AuditLogs record allowlisted business/access changes with actor/name snapshots,
  UTC time, jsonb before/after and server correlation ID. Audit and business writes
  share the transaction. InventoryMovement remains the full quantity ledger.
- Admin-only audit list/detail with user/action/entity/date filters, validated paging,
  stable descending ordering, localized badges, before/after details and mobile cards.
  PostgreSQL rejects ordinary audit UPDATE/DELETE. No audit mutation API exists.
- Central React permissions hide unauthorized actions/navigation and guard direct
  URLs. API 401 clears the matching session; 403 shows a localized access error while
  retaining authentication. User/audit queries refresh after mutations.

### Database preservation

Migration `20261009122237_SecurityAndAudit` adds Role (Viewer default), IsActive,
SecurityVersion, UpdatedAt and IsBootstrapAccount to Users. It adds role/version
checks, AuditLogs with optional User FK, jsonb snapshots, four filtering/ordering
indexes and an immutable-audit trigger. Earlier migrations were not rewritten.
Audit EntityName/EntityId are logical references, not polymorphic FKs.

A private SQL backup was taken before migration and remains ignored. The normal
volume retained its nine users (all backfilled Viewer), two products, two inventory
rows, one order and zero old inventory movements. A separate bootstrap Admin brought
the user count to ten. Four migrations are applied. Bootstrap was then disabled and
its password removed from the running API environment; a fresh Admin login and safe
user/audit reads succeeded. The database volume was not reset.

### Actual verification

| Executed check | Result |
| --- | --- |
| `dotnet build Catalog.slnx` using local .NET 10 SDK | PASS; zero warnings/errors |
| `dotnet test --no-build` | PASS; 128/128, zero skipped; final run 34.583 seconds |
| `dotnet-ef migrations has-pending-model-changes --project backend/Demo.Api --no-build` | PASS; no pending model changes |
| `npm run build --prefix frontend` and production Docker frontend `npm run build` | PASS; TypeScript/Vite |
| `docker compose --env-file .tools/phase4/admin.env -p catalogsecurity20261009 up --build --wait` with matching 3500/5580 URL/origin settings | PASS; fresh PostgreSQL volume, four migrations, three healthy services and one-time Admin |
| `node scripts/verify.mjs` with disposable COMPOSE_PROJECT_NAME/API_URL/FRONTEND_URL and ignored Admin credentials | PASS; auth/Viewer restrictions, explicit promotion, revoked token, CRUD, Swagger, order/stock flows, restart persistence and safe logs/errors |
| `docker build -f frontend/Dockerfile.e2e -t catalog-e2e frontend` | PASS |
| `docker run --rm --network catalogsecurity20261009_default --env-file .tools/phase4/admin.env --mount "type=bind,source=$PWD/.tools,target=/work/.tools" -e FRONTEND_URL=http://localhost:3500 -e API_URL=http://localhost:5580 catalog-e2e` | PASS; 18/18 Chromium tests, 42.6 seconds |
| `docker compose --env-file .tools/phase4/admin.env up --build --wait` on normal existing volume | PASS; old business/user data preserved, all existing users Viewer, new separate Admin |
| `docker compose --env-file .tools/phase4/runtime.env up -d --wait` | PASS; bootstrap disabled, password absent in API environment, account count unchanged |
| `docker compose --env-file .tools/phase4/runtime.env build` | PASS; final explicit image build |
| Admin login and GET /users + /audit-logs after bootstrap-secret removal | PASS; ten safe user DTOs, one bootstrap creation audit; no hash fields |
| `node .tools/i18n-audit.mjs` | PASS; static JSX and translation-key audit |
| `node .tools/check-portfolio-docs.mjs` | PASS; twelve Markdown documents, local links and balanced fences |
| `node .tools/extract-mermaid.mjs` + Docker Chromium renderer | PASS; twelve Mermaid diagrams, including authorization/audit flow and audit ERD |
| Actual portfolio capture on fresh catalogsecurityscreens20261009 (3600/5680) | PASS; sixteen screenshots with synthetic data, including five new user/audit EN/TR/detail captures |
| Visual review | PASS; desktop users/audit/detail and Turkish mobile user management inspected |
| `node .tools/review-repo.mjs` and `.tools/gitleaks/runtime/gitleaks.exe dir .tools/phase4/source --redact --no-banner` | PASS; 128 Git-eligible source files, no generated/private files, credential patterns, trailing whitespace or Gitleaks findings |
| GitHub push / remote Actions / new-revision clone | UNVERIFIED; no publication authorized for this phase and no push performed |

The backend suite retains all 84 prior regression cases and adds 44 security/
authorization/provisioning cases. These cover anonymous administrative requests,
every Viewer business-write restriction, all-role business reads, non-Admin delete/
user/audit restrictions, Manager operations, explicit Admin account creation, public
registration overposting, basic-edit overposting, self-protection, duplicate emails,
invalid roles, inactive login, role/status/email token revocation, concurrent Admin
changes, safe/immutable audit, exact Product before/after/actor values and deletion
history, inventory/minimum/order/role events, no success audit on a failed confirmation,
filters/pagination and actual PostgreSQL audit-INSERT FK failures rolling back product,
role, stock and order changes. Separate fresh databases test bootstrap restart/collision,
legacy-user password/login preservation, Viewer backfill and concurrent mutual changes
with exactly two active Admins. Existing migration tests preserve products/orders/stock.

Browser tests retain the catalog/auth/order/inventory/localization/accessibility
coverage and exercise actual Viewer/Manager/Admin sessions, denied URLs, hidden
controls, user form validation/editing, role/status confirmations, explicit Admin
creation, stale-session login redirection, audit filters/pagination/details, Turkish
mobile navigation and 403 without logout. Isolated mocked error/state tests supplement
the real API workflow tests; they do not replace backend authorization verification.

### Limits

No known local failing check remains. Revocation adds one indexed user read per
protected request and does not cancel already-authorized in-flight business requests;
user administration additionally rechecks after its lock. Logout alone does not revoke
a JWT. There is no refresh/password-reset/per-token denylist, tenant isolation,
production deployment or load-test claim. Category/customer edits retain last-write-wins
semantics. Audit is application-level and allowlisted, not database CDC or tamper-proof
storage against a database owner; offset paging may shift during new arrivals. Browser
token storage and the in-memory rate limiter retain their documented demo limitations.

The reproducible setup, permission matrix, precise security trade-offs, audit
transaction boundaries are in
[SECURITY.md](docs/SECURITY.md), [AUTHORIZATION.md](docs/AUTHORIZATION.md) and
[AUDIT_LOGGING.md](docs/AUDIT_LOGGING.md). Real local Admin credentials, backups, logs,
traces and tool outputs stay outside Git.

## Phase 3 inventory verification — 9 October 2026

Phase 3 extends the existing application locally. It has not been committed or
pushed; remote CI and a clone of this revision are **UNVERIFIED**. Earlier publication
results below describe earlier commits only.

### Implemented and database changes

Authenticated inventory dashboard/detail/history, receipt/issue/directional adjustment,
minimum levels, English/Turkish forms and mobile layouts are implemented. Pending
order creation remains possible above available stock; confirmation checks stock
atomically. Product/category changes and order transitions refresh inventory caches.

Migration `20261009114734_InventoryManagement` adds Inventories (unique Product FK)
and InventoryMovements (Product/User FKs, nullable Order FK), nonnegative balance
checks, movement delta/reference checks, history indexes and a unique order/product/
movement-type index. Triggers initialize new products at zero, protect inventory
identity/existence and reject movement UPDATE/DELETE. Existing products are backfilled
at zero. The two earlier migrations remain unchanged.

### Business rules, concurrency and transactions

Receipts/increase adjustments add stock; issues/decrease adjustments subtract only
when sufficient stock exists. Each quantity change records before/after, actor, UTC
time, reason and optional order reference. Minimum-level changes create no quantity
movement. Pending cancellation does nothing to stock. Confirmation deducts grouped
product quantities once; completion does not deduct again. Confirmed cancellation
returns actual recorded deductions once. Legacy orders without deductions return no
invented stock. Terminal transitions are rejected; repeating the current status is
idempotent.

ReadCommitted transactions acquire the order row with PostgreSQL FOR UPDATE, then
inventory rows sequentially in sorted Product Guid order. Every stock writer uses
the inventory row lock; competing requests see committed balances after waiting.
The order lock and unique movement index prevent duplicate order processing. Balance,
movement and status writes commit together. Tests inject failures after SQL writes
and before commit to prove rollback. No in-process-only lock or optimistic browser
stock change is used. See [detailed design](docs/INVENTORY.md).

### Executed commands and results

Commands were run from the repository root unless stated otherwise. The local .NET
10 SDK and EF tool were available in ignored `.tools` directories; Docker Desktop
ran Linux containers. Disposable projects used separate volumes and ports.

| Check / executed command | Result |
| --- | --- |
| `dotnet build Catalog.slnx` | PASS; zero warnings/errors |
| `dotnet test --no-build` | PASS; 84/84, zero skipped; final run 24.379 seconds |
| `dotnet-ef migrations has-pending-model-changes --no-build --project backend/Demo.Api` | PASS; no pending model changes |
| `docker compose -p cataloginventory20261009 up --build --wait` with ports 3300/5380 and matching frontend origin/API URL | PASS; clean database automatically applied all three migrations; three healthy services |
| `docker compose up --build --wait` on the existing normal volume | PASS; preserved two products and one completed order; two zero-balance inventory rows added |
| Frontend `npm run build` (also executed in the Docker build) | PASS; TypeScript and production Vite bundle |
| `docker build -f frontend/Dockerfile.e2e -t catalog-e2e frontend` | PASS; npm ci and test image |
| `docker run --rm --network cataloginventory20261009_default --mount "type=bind,source=$PWD/.tools,target=/work/.tools" -e FRONTEND_URL=http://localhost:3300 -e API_URL=http://localhost:5380 catalog-e2e` | PASS; 15/15 Chromium tests, final run 33.5 seconds |
| `$env:COMPOSE_PROJECT_NAME='cataloginventory20261009'; $env:API_URL='http://localhost:5380'; $env:FRONTEND_URL='http://localhost:3300'; node scripts/verify.mjs` | PASS; API/auth/Swagger, all stock operations, shortage, order deduction/return, repeated transitions, restart persistence and safe failure/logging |
| `node .tools/i18n-audit.mjs` | PASS; JSX text and literal translation keys |
| `node .tools/check-portfolio-docs.mjs` | PASS; nine Markdown files, local links and balanced fences |
| `node .tools/extract-mermaid.mjs` followed by Docker Chromium `render-mermaid.mjs` | PASS; all nine diagrams rendered using Mermaid 12.1.0 |
| Portfolio capture script on separate fresh project cataloginventoryscreens20261009 (3400/5480) | PASS; eleven actual application screenshots, including inventory EN/TR/detail |
| Visual review | PASS; new inventory desktop screenshots and Turkish mobile history/forms inspected |
| `node .tools/review-repo.mjs` | PASS; Git-eligible source excludes generated/private files, credential patterns and trailing whitespace |
| `.tools/gitleaks/runtime/gitleaks.exe dir .tools/phase3/source --redact --no-banner` | PASS; 106 Git-eligible source files exported separately from private tools/backups; no leaks found |
| Phase 3 publication / remote Actions / clone of this revision | UNVERIFIED; local changes have not been published |

The PostgreSQL integration suite covers product initialization/backfill, all seven
endpoint authentication checks, quantity/reason/direction/minimum validation, shortage,
overflow, immutable ledger/constraints, manual operations, duplicate product lines,
all invalid transitions, legacy confirmed orders, cancellation, multi-product rollback,
competing orders, same-order concurrent confirmation/cancellation, parallel adjustments/
issues, deterministic multi-product locking and failures after SQL writes. The browser
suite additionally covers existing catalog/auth/order behavior, stock forms/dialogs,
shortage warnings, audit links, mobile layout, localization, empty/error/loading states
and inventory cache refresh after product/category mutations.

### Remaining limits

No known failing local check remains. Existing database data and its volume were
preserved; a private pre-migration SQL backup stays in ignored local storage. Synthetic
verification data intentionally remains in disposable databases because the ledger
is immutable. Manual stock requests have no idempotency key: replaying a completed
manual request applies another operation. Order transition retries are idempotent.
At the integer ceiling, a cancellation that would overflow returns 409 and rolls back.
Reservations, multiple warehouses, valuation and completed-order returns remain out
of scope. Lists are unpaginated; no production deployment or load test is claimed.

## Publication verification — 8 October 2026

The owner approved publication. Source commit
`ec9f768d378b75e496d040712c450990c3eea4bb` was pushed by normal fast-forward to
the existing public [SCanerG/DemoErp repository](https://github.com/SCanerG/DemoErp).
The original six commits remain ancestors; the repository URL/visibility and
no-open-source-license usage terms were preserved.

- GitHub confirmed the source commit, README and 94-file source tree. Obsolete
  samples/visuals and generated/private files are absent from the current tree.
- [GitHub Actions run](https://github.com/SCanerG/DemoErp/actions/runs/37691499135):
  backend restore/build/tests and frontend npm ci/build all succeeded.
- A new clone from GitHub at that exact source commit passed Compose config and
  `up --build --wait` on an isolated fresh database (ports 3250/5350).
  All three services became healthy; frontend login, API health and OpenAPI returned
  successful HTTP responses. Both committed migrations were applied automatically.
- Gitleaks scanned the published seven-commit history, including the source commit,
  with no findings. No force push or fabricated history was used.
- The README foregrounds implemented functionality and executed checks. A short
  general AI-assisted-development notice remains in NOTICE; no manual-only claim
  is made.

This follow-up documentation records checks performed after the source publication.
The older preparation table below deliberately retains its pre-publication statuses.

## Publication preparation snapshot — 8 October 2026

The existing application was preserved. English/Turkish README files, architecture,
database/API/development documents, eight real application screenshots, a minimal
GitHub Actions workflow and an owner-approved no-open-source-license notice were
prepared. No commit or push has been performed for this preparation.

| Check | Result |
| --- | --- |
| Backend restore/build using CI commands | PASS; 0 warnings, 0 errors |
| Backend tests, real PostgreSQL/Testcontainers | PASS; 37/37, 0 skipped, 13.2 seconds |
| Frontend npm ci/build | PASS; installed lockfile versions; npm audit reported 0 vulnerabilities |
| Compose config and clean application-source export up --build --wait | PASS; three healthy services, disposable project catalogportfolio20261008 on 3200/5280 |
| Applied PostgreSQL migrations | PASS; InitialCreate and BusinessWorkflow, no manual schema setup |
| Chromium browser suite on this fresh stack | PASS; 12/12, 26.0 seconds |
| scripts/verify.mjs on disposable stack | PASS; API/schema/auth/order rules, restart persistence, safe failure/logging |
| Actual screenshots | PASS; eight captures with synthetic records, all visually inspected |
| Mermaid rendering | PASS; six diagrams rendered to SVG/PNG by Mermaid 12.1.0 in Chromium |
| Existing remote backup | PASS; mirror of all refs and verified complete Git bundle; six commits |
| Gitleaks 8.30.1, all relevant remote history | PASS; six commits, no findings |
| Gitleaks, prepared publication working tree | PASS; no findings |
| Generated/private-file and whitespace review | PASS; examples only, no real .env, dependencies, DB/logs or local tools |
| New revision on GitHub / remote Actions | UNVERIFIED; explicit publication approval required first |
| Clone/run of the new revision from GitHub | UNVERIFIED; the new sources have not been pushed |

Remote baseline: `17dcffbef56881a9dca5e6fa102144dd54dd923d`, public
`SCanerG/DemoErp`, main. The isolated preparation checkout retains this history.
The application's original working directory has no local commits; no history was
fabricated. The old sample/visuals are removed only in the local prepared checkout.
Tools, bundles, scanner reports and generated diagram previews stay in ignored
local storage. Development placeholders are clearly identified; a clean secret
scan is not proof against every possible secret format.

The entries below record the earlier business-module implementation phase and its
separate disposable-stack run; counts/timings/76-file export refer to that phase.

## Earlier business implementation verification

Date: 8 October 2026 (Europe/Istanbul). Existing architecture, authentication, Docker services, environment configuration and Product routes were extended rather than recreated.

## Implemented

- Application-wide Turkish/English copy, header/auth language selectors, immediate switching, localStorage preference persistence, localized dates/currency and accessible document language/title.
- Responsive Products/Categories/Customers/Orders navigation with active-page styling and user/logout controls. Existing dark green identity retained; restrained statistic accents and text-labeled order badges added.
- Category and Customer CRUD list/create/detail/edit/delete screens and authenticated APIs. Counts are projected from related records.
- Required Product category, category selection in create/edit, category display/link in list/detail. Existing products are preserved through migration.
- Order list/create/detail/status APIs and screens. Multiple product lines, quantity validation, live previews, server-authoritative price snapshots/totals, transactional creation and concurrency-safe order numbers.
- Mobile business lists and order details use cards. Authentication, Product CRUD, loading/error/empty states, confirmation dialogs and previous regression tests remain working.
- README updated with domain relationships, workflow, transaction/price strategy and localization decisions.

## Database Changes

Migration: `20261007205005_BusinessWorkflow`, following the unchanged InitialCreate migration.

| Change | Configuration |
| --- | --- |
| Categories | UUID PK, required bounded name/description, active flag, UTC timestamps |
| Customers | UUID PK, bounded name/email/phone/address, active flag, UTC timestamps |
| Orders | UUID PK, required CustomerId FK, string status, UTC dates, numeric(18,2) total |
| OrderItems | UUID PK, required OrderId/ProductId FKs, quantity, numeric(12,2) price snapshot, numeric(18,2) line total |
| Products | Required CategoryId FK; existing fields preserved |
| Relationships | Category 1:N Product; Customer 1:N Order; Order 1:N OrderItem; Product 1:N OrderItem |
| Delete behavior | Restrict referenced Category/Customer/Product; Cascade Order → Items (no public order deletion) |
| Indexes | Product.CategoryId, Order.CustomerId, OrderItem.OrderId/ProductId; unique Order.OrderNumber; existing unique normalized User.Email |
| Constraints | Nonnegative total/price; quantity > 0; LineTotal = Quantity × UnitPrice; defined order statuses |
| Number generation | PostgreSQL bigint OrderNumbers sequence + unique order-number index |

The migration creates a General category only when pre-category products exist, assigns those products, then makes CategoryId required. A dedicated test applied InitialCreate, inserted an old-format product, applied BusinessWorkflow and verified product/price/category preservation and no pending model changes. Empty-database startup applied both migrations automatically; no manual schema setup was used.

## Business Rules

- Products require an existing active category on creation/reassignment. An unchanged inactive category remains valid for an existing product.
- Categories with products, customers with orders, and products used by order items return safe 409 business errors when deletion is attempted. PostgreSQL FKs also protect races after the precheck.
- Customer name is required; optional email must be valid; all strings have bounded lengths.
- Orders require an existing active customer and 1–100 distinct existing active products; quantities are integers from 1 to 100,000.
- Only Product.Price read from PostgreSQL supplies UnitPrice. Browser prices/line totals/order totals are ignored. Decimal arithmetic computes all lines and the total; existing order price snapshots survive product price edits.
- Repeatable-read transaction includes reference checks and all order/item writes. Exceptions roll back all writes. The failure test throws after SaveChanges has executed SQL but before transaction commit and checks zero additional orders/items.
- Numbers use ORD-UTC-date-sequence; concurrent requests receive distinct numbers. The sequence never resets daily and may have gaps after rollback.
- Pending → Confirmed → Completed; Pending/Confirmed → Cancelled. Completed/Cancelled are terminal; repeating a status is idempotent. Conditional status updates reject stale concurrent changes with 409.
- Orders are cancelled rather than publicly deleted. Order-value summary excludes cancelled orders. Read queries project DTOs, counts and nested items/product names without per-item service/database loops.

## Tests

**37 .NET tests passed, zero skipped** (17 added to the previous 20). xUnit v3, WebApplicationFactory and real PostgreSQL Testcontainers; committed migrations are used.

New coverage: authentication on all Category/Customer/Order operations; required/valid Product category; referenced-category deletion protection; Category/Customer CRUD and customer validation; missing customer/empty items/zero or negative quantity/missing or inactive product/duplicate product rejection; no partial persistence after invalid orders; browser price manipulation ignored; multiple-line decimal totals; preserved price snapshots; referenced Product/Customer deletion protection; concurrent order numbers; valid/invalid status transitions; rollback after SQL insertion; existing-product migration upgrade.

**12 Chromium tests passed** against the clean Docker stack (20.1 seconds). Three new tests plus category adaptation of existing Product tests cover:

- Real registration/login; Category create/update; Product create/category/update; Customer create/update; two-line Order create/detail/confirm/complete/list/reload.
- EN → TR → EN, preference after refresh, Turkish navigation/forms/status/currency, immediate translation of existing validation errors, localized server validation errors.
- Turkish empty/error screens and active navigation for all new business lists on 390px mobile.
- Previous auth expiry/401/race/blocked-storage, Product CRUD/price boundaries, loading/retry, mobile actions and keyboard delete-dialog regressions.

An initial new test exposed validation text becoming part of the password input's accessible name. The login field now has a stable localized name with an associated error description; the entire browser suite was rerun successfully. Initial test-client enum deserialization failures were corrected to follow the API string-enum contract; failed attempts were not reported as PASS.

## Verification

| Command / executed check | Result |
| --- | --- |
| `dotnet build` at root | PASS — .NET 10, 0 warnings / errors |
| `dotnet test` at root | PASS — 37/37, 0 skipped |
| `npm run build` in frontend | PASS — TypeScript + Vite; latest source also compiled in both Docker builds |
| `docker compose build` | PASS — normal workspace and clean source export |
| `docker compose up --build` | PASS — attached command, new volume, two automatic migrations, three healthy services |
| `node scripts/verify.mjs` | PASS — auth/CRUD/schema/Swagger plus business relationships, safe deletion, price calculations/statuses/indexes |
| All-container restart | PASS — user/Product plus Category/Customer/Order/items/status/total still available |
| Forced PostgreSQL interruption | PASS — safe 500 ProblemDetails + trace ID + unexpected server error log |
| Password/hash/JWT log check | PASS — test credentials absent from backend logs |
| Playwright Linux Docker build/run | PASS — 12/12 Chromium tests |
| Static JSX/literal translation-key audit | PASS — no uncentralized UI text/missing literal keys (brand letter excluded) |
| Manual screenshot review | PASS — English desktop catalog, Turkish registration, Turkish mobile order lines/total; no clipped mobile line totals |
| NuGet vulnerable-package scan | PASS — API and tests report no known vulnerable packages |
| npm audit | PASS — 0 vulnerabilities reported |
| Git candidate scan | PASS — 76 files; no generated/private files, credential patterns or trailing whitespace |
| Normal localhost stack | PASS — three services healthy; original volume preserved |
| Direct interactive manual browser control | UNVERIFIED — tool initialization fails with Windows sandbox CryptUnprotectData error; not counted as a successful manual interaction |

Host .NET commands used the ignored `.tools/dotnet/dotnet.exe`. A mistaken npm invocation at the repository root was rejected because package.json is in frontend; the correct frontend command and Docker build commands subsequently passed.

### Clean start and cleanup

The Git repository still has no commits. A clean export of its 76 Git-eligible source files excluded `.env`, generated outputs, dependencies and local tools. This was not a remote Git clone. Compose project `catalogbusiness20261008` used a verified new volume and browser-facing ports 3100/5180; database/JWT defaults required no manual setup. All APIs and deployed Chromium flows ran against this stack. The exact attached startup command was intentionally stopped after verification; it is a long-running server command, not a test expected to exit naturally. Only the temporary test stack/volume was removed. Normal ports 3000/5080 remain healthy with the existing volume.

## Remaining Issues

Direct interactive manual browser control was unavailable due to the tool's Windows sandbox initialization failure. Actual browser workflows were executed automatically in Linux Chromium, and EN/TR screenshots were manually reviewed; this is explicitly distinguished from interactive manual operation. Native Windows Chromium, remote cloning, production deployment, cross-platform certification and load testing are not claimed.

Shared business data; no roles/ownership, pagination, inventory, taxes, discounts, payments, refresh/revocation or password recovery. Product/Category/Customer edits are last-write-wins; Order status uses conditional updates. Order unit prices are snapshots, while Product/Customer names remain live references. Frontend totals are previews; the final server total is authoritative. Sequence gaps are intentional. Startup migrations remain a local-demo strategy; multi-instance deployment needs controlled migration execution.

Existing Product routes remain, but create/update require categoryId as requested. Browser tests intentionally create disposable accounts and order data. The API verification script deletes only its own order via test-admin SQL cleanup; the public API has no order-delete endpoint. No Git commits/history were fabricated.
