# Architecture

## Current system map

Start with the [seven current architecture diagrams](architecture/README.md): system, backend layers, provider abstraction, agent collaboration, secure approval, RAG and visual workflow execution. [Feature status](FEATURE_STATUS.md) distinguishes implementation evidence from future work. The source remains a layered monolith; optional AI/knowledge/monitoring/workflow services use the existing authorization and transactional business boundaries.

The phase sections below preserve earlier decisions. Their phase-scoped statements about absent storage, RAG, manufacturing or AI capabilities are historical where later modules supersede them. Current provider configuration is in [Local Ollama](LOCAL_OLLAMA.md), agent responsibilities in [AI agents](AI_AGENTS.md), and actual execution results in [verification](../VERIFICATION.md).

## Phase 10 quality and frontend architecture

The application remains a layered monolith with one API project and one React app. There are no independent module assemblies, repositories or distributed business services. Scoped application services own transactions; shared inventory locks and PostgreSQL guards protect cross-module consistency. EF Core provides typed projections/migrations and PostgreSQL provides relational constraints, row/advisory locks and transactional behavior.

The React shell is now shared in `AppShell.tsx`; role-aware grouped navigation and route-level lazy imports keep page concerns separate. TanStack Query continues to own server state; React Hook Form/Zod validate forms. Shared status/progress/loading/success components establish visual consistency without introducing a generic table framework. [UI decisions](UI_DESIGN.md), [test strategy](TESTING.md), [deployment](DEPLOYMENT.md).

Liveness is process-only; readiness checks PostgreSQL with a two-second deadline while `/health` preserves its previous contract. Docker Compose uses readiness. No new infrastructure, business module or schema migration was added in Phase 10.


Phase 9 adds ManufacturingService, ManufacturingModel, BOM/ProductionOrders controllers and React manufacturing pages. Stock changes call the existing InventoryService.Apply with caller-owned transactions and compatible warehouse/product/balance locks. The reporting additions are database queries with no new infrastructure. [Manufacturing design](MANUFACTURING.md).

Current Phase 8 stock architecture: `WarehouseInventories` is authoritative; `Inventories` is a read-only aggregate view and `LegacyInventories` retains frozen openings. All stock writers share warehouse/product/balance lock helpers. [Warehouse architecture, lifecycle, lock order and migration](WAREHOUSE_MANAGEMENT.md). Earlier phase sections below describe their original implementation.

[README](../README.md) · [Database](DATABASE.md) · [API](API.md) · [Development](DEVELOPMENT.md) · [Inventory](INVENTORY.md)

The repository contains one ASP.NET Core API project and one React application.
`Domain`, `Contracts`, `Services` and `Data` are directories within `Demo.Api`;
there are no separate Application or Infrastructure assemblies.

```mermaid
flowchart LR
  UI[React and TypeScript] -->|HTTP JSON and Bearer JWT| API[ASP.NET Core controllers]
  UI --> Query[TanStack Query cache]
  API --> Validation[FluentValidation request validators]
  API --> Services[Scoped business services]
  Services --> EF[Scoped EF Core AppDbContext]
  EF --> DB[(PostgreSQL 17)]
  API --> Errors[ProblemDetails exception handling]
```

Controllers own HTTP status codes and validation. Services implement authentication,
catalog operations, reference checks and order transitions. Dependency injection
creates a scoped context/services per request; token generation is a singleton
using validated JWT options. EF tracking and transactions provide the persistence
boundary without additional repository or unit-of-work wrappers.

The optional [AI Business Assistant](AI_ARCHITECTURE.md) adds a bounded, read-only
orchestrator and fixed tool registry above the existing ReportingService. Its
IAiModelClient adapter uses the official OpenAI .NET SDK; credentials stay in backend
configuration. The feature is off by default and adds no database schema or chat storage.

Read operations use `AsNoTracking` and explicit DTO projections, including category
names, counts and order-line product names. Order reads avoid per-line queries.
Business lists currently return all rows; audit history uses server pagination and
filters. Load testing is not implemented.
TanStack Query owns server state and mutation invalidation. React Hook Form/Zod
provide immediate form feedback; the backend remains authoritative.

## Authentication and errors

Registration normalizes email; a unique database index handles races. Identity's
PBKDF2 hasher uses 210,000 iterations. JWT Bearer validates HS256, signature,
issuer, audience and expiry. Authentication requests are limited to 20/IP/minute.
Safe DTOs exclude password hashes. Business records remain shared without tenants,
while explicit Admin/Manager/Viewer policies restrict operations. Every protected
request reads the user's active flag, role and SecurityVersion to reject stale tokens.
See [security flow and bootstrap](SECURITY.md) and [permission matrix](AUTHORIZATION.md).

Browser sessions use sessionStorage with a memory fallback. Current-session 401
or expiry clears authentication/cache; late responses for a replaced token do not
clear the new session. Logout does not revoke an issued token. This browser storage
is accessible to JavaScript; production hardening would need an explicit XSS and
identity strategy.

Validation returns 400 ProblemDetails, missing records 404 and reference/status
conflicts 409. Unexpected failures produce a generic 500 with a trace ID and JSON
server logs. Request bodies and sensitive SQL values are not logged.

## Orders

One repeatable-read transaction checks active references, reads current database
prices, allocates a sequence number and saves the order and all items. A failure
rolls back every business write. Sequence allocation is not rolled back, so gaps
are expected. Unit prices are snapshots; names remain live references.

Order status changes use a ReadCommitted transaction and an Order row lock, then
deterministically ordered Inventory row locks. Confirmation deducts stock and records
movements; confirmed cancellation reverses actual deductions. Repeated statuses do
not change stock. A filtered unique movement index adds duplicate protection.
Product/category/customer edits use last write wins. Referenced records, including
products with stock history, cannot be physically deleted. There is no order deletion
API. [Inventory locks, transactions and legacy-order handling](INVENTORY.md).

## Administration and audit

UserService serializes administrative changes with a PostgreSQL advisory transaction
lock and rechecks the actor under that lock. Self-demotion/deactivation and removal
of the last active Admin are blocked. A one-time configuration bootstrap creates a
new Admin after migrations, without promoting existing accounts. It is disabled by
default and has a persisted marker for restart safety.

AppDbContext.SaveChangesAsync stages explicit allowlisted AuditLog entries with
business writes. Existing transaction boundaries are preserved; EF implicit
transactions cover ordinary tracked CRUD. Product updates lock the product row for
accurate before values. InventoryMovement remains the quantity ledger. Audit reads
are Admin-only, filtered and paginated. [Fields, diagrams and boundaries](AUDIT_LOGGING.md).

## Reporting reads

DashboardController and ReportsController reuse BusinessRead and a scoped
ReportingService alongside the existing services. GroupBy/Select produce scalar
PostgreSQL aggregates and DTOs; sorting uses explicit expressions with stable IDs,
and filtering/pagination remain in SQL. CompletedAt is maintained by the existing
OrderService transaction. No CQRS, repository wrapper, caching service or reporting
engine was introduced. The React reporting module is lazy loaded and uses one
charting library, Recharts, with TanStack Query for server state. Business mutations
invalidate report/dashboard caches centrally.

```mermaid
flowchart LR
    UI[React Dashboard] --> API[Reporting API]
    API --> LINQ[EF Core Query]
    LINQ --> DB[(PostgreSQL)]
    DB --> DTO[DTO Response]
    DTO --> UI
```

[Reporting data flow and business definitions](REPORTING.md) ·
[Actual SQL plans and performance trade-offs](PERFORMANCE.md).

## Localization and deployment

`translations.ts` plus a lightweight React external store provides immediate TR/EN
switching and localStorage preference persistence. Document language/title and
number/date formatting follow the selected locale. Currency remains USD and
user-entered data is not translated.

Compose runs PostgreSQL, API and an unprivileged Nginx frontend, with ordered health
checks and a persistent database volume. API startup applies committed migrations
with bounded retries. Host ports bind to localhost; PostgreSQL is internal.
Swagger is Development-only. Production migrations/deployment, HTTPS and distributed
rate limiting are outside the demonstrated scope.

## Phase 7 procurement integration

[Procurement and supplier management](PROCUREMENT.md) documents the actual PO
lifecycle, restrictive EF relationships, cost snapshots, atomic receipt/stock/audit
writes, PostgreSQL order-then-inventory locks, user/key idempotency, permissions and
UI behavior. The existing modules and AI tool definitions remain unchanged.
The current procurement summary is separate from sales totals and excludes Draft/
Cancelled outstanding units; the procurement report uses PO creation dates.
Receipt stock history uses PurchaseReceipt with a dedicated GoodsReceipt FK,
preserving the existing sales Order reference. Procurement audit actions are
visible through the existing Admin audit list and entity/action filters.
