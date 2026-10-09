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

[AI architecture, tool matrix, configuration and 12 interview Q&As](docs/AI_ARCHITECTURE.md) ·
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

[Reporting contract and ten interview answers](docs/REPORTING.md) ·
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

### Limits and interview preparation

No known local failing check remains. Revocation adds one indexed user read per
protected request and does not cancel already-authorized in-flight business requests;
user administration additionally rechecks after its lock. Logout alone does not revoke
a JWT. There is no refresh/password-reset/per-token denylist, tenant isolation,
production deployment or load-test claim. Category/customer edits retain last-write-wins
semantics. Audit is application-level and allowlisted, not database CDC or tamper-proof
storage against a database owner; offset paging may shift during new arrivals. Browser
token storage and the in-memory rate limiter retain their documented demo limitations.

The reproducible setup, permission matrix, precise security trade-offs, audit
transaction boundaries and ten technical interview questions/answers are in
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
stock change is used. See [detailed design and ten interview answers](docs/INVENTORY.md).

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
