# Roadmap and implementation boundaries

[Verified inventory](FEATURE_STATUS.md) · [Executed checks](../VERIFICATION.md)

## Completed in the reviewed source

| Milestone | Scope |
| --- | --- |
| ERP foundations | Catalog/customers, server-priced orders, authentication, roles, immutable application audit and reporting |
| Supply and production | Warehouse stock/transfers, suppliers, purchasing/receipts, single-level BOMs and production lifecycle |
| AI foundation | Provider abstraction, fixed authorized read tools, bounded outputs, private optional history and explicit provenance |
| Local agents | Actual Ollama integration, specialized Sales/Inventory/Order Draft/Knowledge services and bounded collaboration |
| Controlled business operations | Durable pending proposals, human review, expiry/fingerprint/current-data validation, idempotent business execution |
| Knowledge | Local embeddings, authorized hybrid retrieval and verified literal citations |
| Operations | Deterministic proactive checks, durable runs and deduplicated private alerts |
| Quality | Safe runtime observations, fixed synthetic evaluation dataset/gate and Quality Dashboard |
| Visual automation | Versioned acyclic graphs, fixed node handlers, schedules, fixture simulation, recovery, private history and approval checkpoints |

“Completed” describes the implemented demonstration scope, not production readiness or public-branch parity.

## Implemented with remaining verification

- **OpenAI:** adapter and intercepted transport tests are implemented; successful live Responses inference is not verified.
- **Remote delivery:** CI is configured; current remote results and published parity must be checked for the exact commit after an owner-controlled publication.
- **Hosted demo:** no functioning public DemoERP URL or hosted local-Ollama inference was verified in this review.
- **Visual coverage:** authentic ERP, agent and designer assets exist; refreshed Knowledge, Operations and Quality screenshots remain absent.

## In progress

No new runtime phase is marked in progress without implementation evidence. The documentation showcase is prepared on `docs/professional-agentic-erp-showcase` for review; no publication, merge or deployment is automatic.

## Planned priorities

1. **Operational retention:** workflow history compaction/export and an explicit retention policy.
2. **Recovery tooling:** operator review for uncertain side-effect checkpoints and clearer safe-resume guidance.
3. **Event delivery:** transactional domain-event/outbox design instead of inferring all events from polling windows.
4. **Scheduling:** timezone-aware calendars and user-visible UTC conversion before broader scheduling support.
5. **Deployment hardening:** shared quota/spend controls, controlled migrations, backup/restore drills and reviewed HTTPS/identity operations.
6. **Document isolation:** separate hostile-file processing and broader retrieval relevance evaluations.
7. **Usability:** additional nontechnical-user, keyboard and screen-reader trials; refresh missing authentic captures without using business data.

Multi-tenancy, full accounting/tax, recursive BOM planning and high-throughput distributed execution require separate domain/security design. Arbitrary AI SQL/shell execution and bypassing human approval are not roadmap shortcuts.
