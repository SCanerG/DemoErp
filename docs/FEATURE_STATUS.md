# Verified feature inventory

Reviewed **2026-10-22** against the local Phase 11.8 source tree and existing execution evidence. “Verified” means a concrete implementation plus recorded relevant checks, not production certification or proof that every model response is correct. These features are not assumed to be merged into the public default branch. [Repository-state review](REPOSITORY_REVIEW.md).

Publication follow-up: the owner subsequently requested an update. A fresh normal clone confirmed `main` at `0192250`; this publication incorporates the verified local feature set while preserving the existing standalone `showcase/` application. The earlier unknown-parity classifications below describe the pre-publication audit. Remote CI and hosting still require their own observed results.

| Capability | Classification | Source and evidence |
| --- | --- | --- |
| Catalog, customers and sales orders | Implemented and verified | `Controllers`, `Services/BusinessServices.cs`, domain/contracts; PostgreSQL integration and browser regression suites |
| Warehouse inventory and transfers | Implemented and verified | `InventoryService`, `WarehouseService`, `TransferService`; atomic balances/movements, rollback and concurrency tests |
| Suppliers, purchase orders and goods receipts | Implemented and verified | `ProcurementService`; partial receipt, replay protection and stock/audit integration |
| BOM and manufacturing | Implemented and verified | `ManufacturingService`; single-level versioned BOM, material snapshots, atomic consumption/output tests |
| Executive reporting | Implemented and verified | `ReportingService`, React Reporting; database-driven KPIs, filtered/paged CSV; [metric definitions](REPORTING.md) |
| JWT, roles and audit | Implemented and verified | Current user/role/SecurityVersion checks, Admin/Manager/Viewer policies, immutable application audit; [security](SECURITY.md) |
| Read-only AI Assistant | Implemented and verified | `AiAssistantService`, nine fixed tools, owner-scoped optional history, bounded contracts; intercepted-provider and actual local Ollama evidence |
| Ollama integration | Implemented and verified | `OllamaModelClient`, loopback validation, actual `qwen3:8b` tool calling through authenticated .NET; Development-only |
| OpenAI integration | Implemented but not fully verified | Official SDK Responses adapter and transport/security tests exist; successful real OpenAI inference is not recorded. Model listing or quota rejection is not inference success |
| Development Mock | Implemented and verified | Provider contract, labels, real authorized report access and existing tests; not live AI |
| Specialized agents and collaboration | Implemented and verified | `AgentOrchestrator`, Sales/Inventory/Order Draft/Knowledge services; actual sequential Sales + Inventory runs and separate source tables |
| Human-approved sales/purchase operations | Implemented and verified | Existing `WorkflowProposals` plus business services; fingerprint/expiry/idempotency, stale-data/authority/replay and rollback tests. Live browser purchase approval remains a separate opt-in |
| Local knowledge/RAG | Implemented and verified | Document extraction/indexing, local embeddings, authorized pgvector + full-text retrieval, exact source grounding; recorded actual embedding/retrieval/citation checks |
| Proactive monitoring | Implemented and verified | Durable leased monitoring runs, deterministic read capability, deduplicated private alerts; no scheduled LLM calls in this monitoring subsystem |
| AI Quality Dashboard | Implemented and verified | ActivitySource/Meter, bounded private metadata, 33-case fixed evaluation suite, comparison gate and actual UI/API acceptance |
| Visual Workflow Designer | Implemented and verified | React Flow 12.12.0, 21 registered types, versioned DAGs, schedules/events, safe simulation, recovery and human approval; actual inventory/schedule/pending-order UI acceptance |
| CI configuration | Implemented and locally checked | `.github/workflows/ci.yml` contains build/test/audit/evaluation/browser jobs. A current published remote run has not been verified for this local tree |
| Docker/local deployment | Implemented and verified locally | Three-service Compose topology, health checks and loopback bindings; native optional-AI overrides exist |
| Public hosted DemoERP | Not verified | `demo.scgenco.com` could not be verified as a functioning application during this review. No hosted AI capability is claimed |
| Public branch parity | Not verified | Local HEAD is unborn and no remote is configured; the observed public README describes older capabilities |

## In progress

No additional runtime feature is labelled in progress without source evidence. This documentation/screenshot refresh is prepared locally for owner review and eventual publication; it does not merge a development phase into `main`.

## Planned / outside the implemented scope

Workflow history retention/compaction, operator handling for uncertain checkpoints, transactional domain-event delivery, timezone-aware schedules, shared/distributed AI quota enforcement, stronger upload isolation and a full tenant model are follow-up work. Full accounting/tax, multi-currency settlement, recursive BOM planning and unconstrained autonomous agents are not implemented. [Roadmap](ROADMAP.md).

## Verification boundary

The last complete Phase 11.8 run passed **714 backend cases**, including three explicitly enabled local-model cases; the broad browser run passed **119** and skipped **four** profile/opt-in cases. Frontend unit tests passed **22**, script tests **32**, and the fixed evaluation gate **33**. These dated results come from preserved output and [VERIFICATION.md](../VERIFICATION.md); they are not newly measured performance benchmarks. This documentation change does not rerun business-write acceptance or live inference.
