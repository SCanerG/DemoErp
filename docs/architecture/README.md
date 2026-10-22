# Architecture diagrams

[Project overview](../../README.md) · [Architecture overview](../ARCHITECTURE.md) · [Documentation](../README.md)

DemoERP is one ASP.NET Core API, one React application and PostgreSQL. Modules are directories and scoped services inside a layered monolith. Background jobs use database-backed tables and in-process hosted services; no separate message broker, independent agent servers or external workflow platform is implied.

| Diagram | Document |
| --- | --- |
| 1. System architecture | [System and provider boundaries](SYSTEM.md#system-architecture) |
| 2. Backend layers | [Controllers, services, domain and data](SYSTEM.md#backend-layers) |
| 3. AI provider abstraction | [Chat and embedding contracts](SYSTEM.md#provider-abstraction) |
| 4. Agent orchestration | [Actual Sales + Inventory collaboration](AGENT_WORKFLOWS.md#agent-orchestration) |
| 5. Secure order approval | [Proposal, explicit approval and verified result](AGENT_WORKFLOWS.md#secure-order-approval) |
| 6. RAG ingestion/retrieval | [Authorized local knowledge pipeline](KNOWLEDGE_AUTOMATION.md#rag-pipeline) |
| 7. Workflow execution | [Versioned definitions, queue, worker and recovery](KNOWLEDGE_AUTOMATION.md#visual-workflow-execution) |

All diagrams describe inspected source. [Feature verification](../FEATURE_STATUS.md) separates implemented behavior from partial verification and future work. External OpenAI processing is optional; local Ollama is loopback-only and Development-only in the current provider configuration.
