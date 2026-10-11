# PostgreSQL reporting performance

## Phase 10 query review — 2026-10-11

A new isolated PostgreSQL 17.11 database used the same deterministic 500 categories, 5,000 products, 2,000 customers, 20,000 orders, 80,000 order lines and 100,000 movements. The original fixture was seeded at the historical reporting migration and then upgraded through all nine migrations. No workload ran against the primary application database.

The production preview previously materialized every warehouse balance into a dictionary although only BOM materials were needed. `ManufacturingService.Preview` now filters the query to the recipe's material IDs before materialization. The existing unique `(WarehouseId, ProductId)` index supports this query; no new index or migration was justified.

| Query | Warm median ms | Review |
| --- | ---: | --- |
| Preview before: whole warehouse | 0.421 | Sequential scan; 5,000 rows, 57 shared hit blocks |
| Preview after: five material IDs | 0.031 | Existing composite index; five rows, 11 shared hit blocks |
| Completed-sales total | 0.176 | Existing reporting query; no Phase 10 change |
| Top products | 12.073 | Existing aggregate; no improvement claim |
| Customer summary | 1.613 | Existing aggregate; no improvement claim |
| Filtered orders | 0.293 | Existing date index; unchanged |
| Inventory joins | 3.136 | Current warehouse aggregation; unchanged |
| Movement history | 0.069 | Existing product/date access; unchanged |

One discarded warmup and seven warm `EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)` runs per captured EF command. Rows/buffers above come from the saved final plan; medians use all seven samples. Zero shared read blocks in the preview plans indicates warm cache; these results do not measure network, dictionary allocation, HTTP latency or cold storage. Hardware/environment variance makes comparison with the historical Phase 5 timings below inappropriate. [Actual measurements](performance/phase10/measurements.json), [before SQL](performance/phase10/preview-before.sql), [before plan](performance/phase10/preview-before.plan.json), [after SQL](performance/phase10/preview-after.sql), [after plan](performance/phase10/preview-after.plan.json).

Reproduce with a **fresh, disposable**, local PostgreSQL database named `reporting_benchmark`, then set `ConnectionStrings__Default` for it:

```sh
dotnet run --project scripts/performance -- --baseline --seed
dotnet run --project scripts/performance -- --measure --output docs/performance/phase10
```

The tool rejects another database name or a non-loopback host; it refuses seeding if any business data exists. Do not attempt the historical `--baseline` migration against an upgraded database; create a separate new benchmark database. Existing foreign-key, partial uniqueness, movement/history and reporting indexes were retained to protect their invariants and access paths. Index removal/addition would add write cost and migration risk without demonstrated benefit here.

### Frontend bundle measurement

Route-level lazy imports remove unrelated business screens from the initial entry bundle. Both baseline and final are built with their own locked dependencies using `npm ci` and `npm run build`; the initial JavaScript entry changed from 589.77 kB (169.82 kB gzip) to 416.88 kB (129.08 kB gzip), a 29.3% reduction in emitted minified entry size. Other chunks are loaded separately, so this is not a claim that total application code shrank by that percentage. Reporting remains a separate chart chunk below Vite's 500 kB advisory threshold. These are emitted bundle sizes, not page-load latency measurements.

---


Measured on 2026-10-09 in a separate local `reporting_benchmark` database, never in
the application's persistent database. Baseline was captured before adding the
ReportingIndexes migration. The same EF query shapes, filters and data were used
afterwards. These are database execution times, not HTTP latency or a production SLA.

## Environment and method

- PostgreSQL 17.11 (`postgres:17-alpine` at execution), Npgsql EF Core 10.0.3,
  EF runtime 10.0.12, .NET SDK 10.0.401 / runtime 10.0.12, Docker Desktop Linux.
- Docker reported 20 logical CPUs and 16,633,098,240 bytes memory available.
  shared_buffers 128 MiB, work_mem 4 MiB, effective_cache_size 4 GiB,
  max_parallel_workers_per_gather 2. Container CPU/memory limits were not customized.
- Deterministic 500 categories, 5,000 products/inventories, 2,000 customers,
  20,000 orders, 80,000 order items, 100,000 movements and one disabled synthetic actor.
  Orders span 730 days; 60% are Completed, 20% Pending, 20% Cancelled. Four distinct
  product lines per order; stored order totals match their line sums.
  Business IDs, timestamps and values are deterministic. Trigger-generated
  Inventory surrogate IDs vary; report/benchmark selection uses stable ProductId.
- Filter period: 2025-09-01T00:00:00Z <= timestamp < 2025-10-01T00:00:00Z.
  Customer report returns twenty rows, top products ten, filtered orders twenty;
  inventory selects one ten-product category and history one product's twenty movements.
- The harness runs ANALYZE, executes the actual EF query once to capture SQL and
  typed parameters, then runs `EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)` eight times.
  The first EXPLAIN is discarded; the remaining seven execution times yield the
  median. EF compilation, planning time, serialization, network/authentication,
  dashboard's other summary reads and report COUNT round trips are not included.
- These are warm-cache measurements. Cache was not cleared; no cold-cache results
  or CI timing gate is claimed. Reported plans are the final sample, which can differ
  from the sample at the median. Background load and submillisecond jitter remain.

## Measured comparison

| Query | Before median ms | After median ms | Observed reduction | Interpretation |
| --- | ---: | ---: | ---: | --- |
| Completed sales total | 1.456 | 0.325 | 77.7% | New selective completion index used |
| Top products | 14.029 | 10.923 | 22.1% | Completion filtering improved; item scan still dominates |
| Customer summary | 6.804 | 1.998 | 70.6% | Both date aggregates use new indexes |
| Filtered orders | 2.739 | 0.631 | 77.0% | Creation-date bitmap filtering reduces scanned rows |
| Inventory joins | 0.401 | 0.309 | — | Unchanged relevant indexes/query; variation, not evidence of an optimization |
| Movement history | 0.209 | 0.236 | — | Unchanged plan; 0.027 ms variation, no improvement claim |

Raw seven-sample measurements and parameters:
[baseline](performance/baseline/measurements.json),
[optimized](performance/optimized/measurements.json).
The absolute times are modest; the queries were not assumed to be slow.
Percentage changes on this dataset do not predict other workloads.

A further pair using the final harness and identical six SQL command shapes was
also recorded: [baseline recheck](performance/recheck/baseline.json) and
[optimized recheck](performance/recheck/optimized.json). Medians were sales total
1.504 → 0.561 ms, top products 19.395 → 15.514 ms, customer summary 5.586 → 4.463 ms,
filtered orders 1.773 → 0.875 ms, inventory 0.459 → 0.561 ms and movement history
0.267 → 0.294 ms. The date-query reductions repeat, but especially customer-query
and submillisecond percentages vary substantially on the shared development host.
The first table is one captured pair, not a guaranteed improvement factor. The
unchanged inventory/movement measurements continue to show ordinary variation.

## Plans and decisions

| Query | Baseline plan | Optimized plan |
| --- | --- | --- |
| Sales total | [JSON](performance/baseline/sales-total.plan.json) | [JSON](performance/optimized/sales-total.plan.json) |
| Top products | [JSON](performance/baseline/top-products.plan.json) | [JSON](performance/optimized/top-products.plan.json) |
| Customer summary | [JSON](performance/baseline/customer-summary.plan.json) | [JSON](performance/optimized/customer-summary.plan.json) |
| Filtered orders | [JSON](performance/baseline/filtered-orders.plan.json) | [JSON](performance/optimized/filtered-orders.plan.json) |
| Inventory joins | [JSON](performance/baseline/inventory-joins.plan.json) | [JSON](performance/optimized/inventory-joins.plan.json) |
| Movement history | [JSON](performance/baseline/movement-history.plan.json) | [JSON](performance/optimized/movement-history.plan.json) |

SQL files beside every plan contain the actual intercepted EF command, with named
parameters whose typed values are in measurements.json. They are not string-built
queries based on client sort names.

**Sales total:** baseline scans 20,000 orders and retains 486 completions, estimated
288; 598 shared hit blocks. After indexing, a bitmap index/heap scan retrieves the
matching period with 42 shared hit blocks in the saved sample. Estimates remain
approximate because status/date distributions are correlated.

**Top products:** baseline hashes the 486 matching orders and joins an 80,000-row
OrderItems sequential scan, then sorts/aggregates 1,944 matching lines into 1,910
products and top-N sorts the results. After indexing, the order date scan is cheaper,
but PostgreSQL still chooses an item sequential scan/hash join. Existing OrderId FK
index is present; forcing nested loops or adding a duplicate index is unjustified
at this selectivity. The observed 22.1% reduction is smaller than sales total for
that reason. Product/current-stock joins occur after aggregate sales, avoiding row
multiplication.

**Customer summary:** two independent order aggregates (completion and creation)
left join 2,000 customers. Baseline reads Orders twice, 1,217 total shared hit blocks;
optimized saved plan uses the partial completion and OrderDate/Id indexes, 107
hits. Hash aggregate memory in the baseline plan is 297 KiB and 105 KiB for the
two aggregates; no spill was observed. Top-N heapsort returns twenty customers.

**Filtered orders:** Pending plus creation date yields 162 matching orders. The
baseline sequential scan/top-N sort reads 598 order buffers; the optimized bitmap
date scan plus status filter reduces work. Total saved-plan hits fall from 739 to
85, including joins/subplans. SQL item counts use the existing OrderItems.OrderId
index for four lines per returned order. This is one application SQL command, even
though the correlated database subplan loops twenty times.

**Inventory:** existing Products.CategoryId bitmap index finds ten products; unique
Inventories.ProductId index retrieves one stock row each. A sequential scan of 500
categories is appropriate for its small size. No additional inventory/category
index was added. Saved-plan hits 60 before / 48 after can vary with page layout/cache.

**Movements:** existing (ProductId, CreatedAt) bitmap scan selects twenty movements
from 100,000, followed by a small quicksort and one disabled actor join. Twenty rows
were estimated and observed; 24 shared hits in both saved plans. A single-row Users
sequential scan is cheaper than forcing an index. No new movement index was needed.

Saved sample plans report shared reads = 0 for the warmed roots. Sorts remain in
memory; plans include memory details where PostgreSQL emits them. Sequential scans
of small dimensions or most of OrderItems are deliberate planner choices, not
automatically defects. No global timeout was increased.

## New indexes and costs

ReportingIndexes adds only:

1. `IX_Orders_CompletedAt`: btree CompletedAt, partial predicate
   `Status = 'Completed' AND CompletedAt IS NOT NULL`. Completed sales/trend filters
   use this subset; pending/cancelled/legacy rows do not occupy it. Measured size
   240 KiB on this fixture.
2. `IX_Orders_OrderDate_Id`: btree (OrderDate, Id), supporting creation-date periods
   and stable order-date lookup. Measured size 808 KiB. Mixed descending date and
   ascending ID output may still require a sort; this is not advertised as sort-free.

No standalone Status index or duplicated CustomerId/OrderItem/Product/Inventory
indexes were added. The new indexes consume storage, insert/update work and WAL;
completion inserts an entry into the partial index. Write-throughput costs were
not benchmarked. Broad periods can legitimately favor sequential scans.

## Reproduce safely (PowerShell)

The optional tool has no startup hook and refuses any connection whose host is not
localhost/127.0.0.1 or database name is not exactly `reporting_benchmark`. Seeding
also refuses nonempty user/customer/product/order tables. No records are deleted
or overwritten. The fixture has no usable account password and no real contacts.
SQL fixture insertion intentionally bypasses service/audit events; functional
workflows are separately tested through the API. The synthetic movement chains
are manual receipts ending at each stock balance; they do not pretend to reconstruct
historical order deductions.

Run from the repository root with .NET 10 and Docker available. Use a **new** named
volume/container if the example names already exist; do not reuse application data.

```powershell
docker run -d --name demoerp-reporting-benchmark -e POSTGRES_DB=reporting_benchmark -e POSTGRES_USER=catalog -e POSTGRES_PASSWORD=benchmark-only-local-password -p 127.0.0.1:5740:5432 -v demoerp_reporting_benchmark:/var/lib/postgresql/data postgres:17-alpine
$env:ConnectionStrings__Default='Host=127.0.0.1;Port=5740;Database=reporting_benchmark;Username=catalog;Password=benchmark-only-local-password'
dotnet run --project scripts/performance/Reporting.Benchmarks.csproj -- --baseline --seed --measure --output .tools/performance/baseline
dotnet run --project scripts/performance/Reporting.Benchmarks.csproj -- --measure --output .tools/performance/optimized
```

The tool applies migrations itself. `--baseline` explicitly targets ReportingCompletion
(five migrations, no new reporting indexes); the normal mode applies the sixth
ReportingIndexes migration. On an already seeded benchmark database, omit `--seed`.
`--baseline` may remove only the reporting indexes by migration rollback; the guard
still restricts it to the isolated benchmark database. Never use this tool's database
name for a real application. Repeat pairs if assessing runtime noise; compare the
captured SQL/parameters/row counts before interpreting a difference.

To stop without losing the fixture: `docker stop demoerp-reporting-benchmark`.
To remove **only this disposable fixture**, first verify the container mounts and
the volume's purpose, then:

```powershell
docker inspect demoerp-reporting-benchmark --format '{{json .Mounts}}'
docker rm -f demoerp-reporting-benchmark
docker volume rm demoerp_reporting_benchmark
```

These exact names are independent of Compose's application volume. This cleanup
is intentionally manual; the seed tool does not have a delete-all operation.
