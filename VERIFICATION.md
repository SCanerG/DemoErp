# Verification record

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

## Interview Talking Points

| Decision | Why | Alternative / accepted trade-off |
| --- | --- | --- |
| Explicit EF 1:N relationships | Clear domain and referential integrity | JSON-embedded items would simplify storage but weaken relational querying |
| Restrict referenced records | Preserve order history; safe business errors | Soft delete requires global filtering/retention rules |
| DTO projections | Shape API responses; avoid entities/cycles/per-item queries | Include + mapping loads broader object graphs |
| One explicit order transaction | Reference checks and writes are atomic | Implicit SaveChanges transaction covers writes only; snapshot isolation has additional concurrency costs |
| Server-side decimal price snapshots | Reject browser manipulation; preserve historical amounts | Trusting client totals is unsafe; live prices would change history |
| Sequence + unique constraint | Concurrent readable order numbers | MAX+1 races; sequence gaps are accepted |
| Central translation dictionary/store | Immediate switching with no component duplication/library | i18next suits larger pluralized/multi-locale systems; dictionary lacks that tooling |
| Query for server state; RHF for forms | Predictable cache invalidation and local editing | Global form/server store would duplicate ownership and synchronization |
