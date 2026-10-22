# AI agents and trusted ERP operations

[Architecture diagrams](architecture/README.md) · [Current feature status](FEATURE_STATUS.md) · [Provider setup](LOCAL_OLLAMA.md)

## What an agent means here

Agents are specialized typed application services inside the .NET monolith. A bounded coordinator chooses a supported workflow, executes sequential steps and records private metadata. It does not delegate unrestricted credentials to autonomous model processes. Current user authorization remains authoritative even when a model suggests the next operation.

| Service | Responsibility | Authority |
| --- | --- | --- |
| Sales | Completed-order metrics, comparison periods and product rankings | Existing fixed report tools; all arithmetic in the backend |
| Inventory | Current balances, completed demand and policy-based replenishment | Typed read capability; no stock movement or purchase write |
| Order Draft | Extract intent and resolve actual active customer/product/warehouse references and prices | Read-only preparation; missing or ambiguous references require clarification |
| Knowledge | Authorized document retrieval and verified literal excerpts | Read-only source context; no ERP tool or write access |
| Coordinator | Fixed routing, budgets, cancellation, source composition and safe summary | Cannot grant permissions or approve an order |

The original Assistant exposes nine registered read-only reports: sales summary, top products, order-status summary, low-stock products, zero-stock products, pseudonymous top customers, inventory overview, pending orders and stock-risk estimates. Specialized agents additionally use explicit typed reads for facts those reports do not expose, such as resolved draft references. This does not turn the reporting registry into a write API.

## Supported collaborations

- **Sales → Inventory → Summary:** three-month sales plus current replenishment analysis; no order is created.
- **Sales → Inventory → Purchase proposal:** supported procurement mode prepares proposals from real product/supplier references; absent/ambiguous references are reported, not invented.
- **Inventory → Knowledge:** current stock facts remain separate from cited policy excerpts; missing evidence is explicit.
- **Order Draft → Proposal → Human Approval:** exact private proposal, current-data revalidation, idempotent existing business transaction and verified receipt.

See [the actual orchestration and approval diagrams](architecture/AGENT_WORKFLOWS.md). The Agent Center has bounded request/step/tool/model limits; the Visual Workflow Designer has a separate typed execution policy and immutable versioning. Neither is an unrestricted general-purpose agent runtime.

## Grounding and provenance

Numeric facts come from ERP queries/C# calculations. Report paths and periods are attached by trusted application code, not model-generated URLs. Model explanations, deterministic reports, Mock output and insufficient-evidence results carry distinct provenance. Invalid provider output can receive bounded recovery; unsafe tool names or malformed arguments never become executable operations. Model text is rendered through the existing safe Markdown subset without executing HTML or arbitrary links.

Replenishment uses a minimum/threshold policy and constant historical demand; it is not a predictive forecast. Completed-order sales value is USD demo sales, not a financial accounting revenue measure. [Reporting definitions](REPORTING.md).

## Approval and observability

AI preparation may persist a pending proposal, but this is not an ERP order. Authorized users review exact changes and approve separately. Fingerprints, expiry, current identity checks and idempotency prevent stale or duplicated writes through the supported path. [Full lifecycle](SECURE_WORKFLOWS.md).

Execution IDs, safe step/tool categories, available usage, timings and outcomes support investigation. Private business facts can appear in authorized report/run history; telemetry does not capture prompts, raw documents, hidden reasoning or secrets. The Quality Dashboard's completed-request rate is an operational metric, not model factual accuracy. [Observability and evaluations](AI_QUALITY.md).

Existing detailed phase documents remain available: [original intelligent agents](INTELLIGENT_AGENTS.md), [response reliability](AI_RESPONSE_RELIABILITY.md), [secure proposals](SECURE_WORKFLOWS.md), [knowledge](ENTERPRISE_RAG.md), [visual workflows](VISUAL_WORKFLOWS.md).
