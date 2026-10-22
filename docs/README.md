# Documentation

[Project overview](../README.md) · [Türkçe](../README.tr.md)

Start with the current guides below. Phase documents preserve implementation decisions and dated evidence; a statement about an earlier phase is not necessarily the current feature boundary. [Verified feature inventory](FEATURE_STATUS.md) and [repository review](REPOSITORY_REVIEW.md) distinguish source implementation, local verification and publication.

## Current guides

| Topic | Guide |
| --- | --- |
| First run | [Getting started](GETTING_STARTED.md) · [Windows installation, Turkish](INSTALLATION.tr.md) |
| Architecture | [Overview](ARCHITECTURE.md) · [Seven detailed diagrams](architecture/README.md) |
| AI services | [Agent responsibilities and workflows](AI_AGENTS.md) · [Local Ollama](LOCAL_OLLAMA.md) · [OpenAI Responses](OPENAI_INTEGRATION.md) |
| Knowledge | [RAG guide](RAG.md) · [Ingestion, retrieval, access and limitations](ENTERPRISE_RAG.md) |
| Automation | [Proactive monitoring](PROACTIVE_OPERATIONS.md) · [Visual Workflow Designer](VISUAL_WORKFLOWS.md) |
| Quality | [Testing](TESTING.md) · [AI evaluations and observability](AI_QUALITY.md) · [Executed results](../VERIFICATION.md) |
| Security | [Application security](SECURITY.md) · [Authorization](AUTHORIZATION.md) · [Audit](AUDIT_LOGGING.md) · [Human-approved proposals](SECURE_WORKFLOWS.md) |
| Development/hosting | [Development](DEVELOPMENT.md) · [Deployment requirements](DEPLOYMENT.md) · [UI decisions](UI_DESIGN.md) |
| Data and API | [Database](DATABASE.md) · [HTTP API](API.md) · [Reporting definitions](REPORTING.md) |
| Status/publication | [Feature status](FEATURE_STATUS.md) · [Roadmap](ROADMAP.md) · [Repository review and metadata](REPOSITORY_REVIEW.md) |
| Screenshots | [Authentic application gallery and provenance](assets/screenshots/README.md) |

## Business modules

[Inventory](INVENTORY.md) · [Warehouses and transfers](WAREHOUSE_MANAGEMENT.md) · [Procurement](PROCUREMENT.md) · [Manufacturing](MANUFACTURING.md) · [Reporting](REPORTING.md) · [Measured SQL plans](PERFORMANCE.md)

Business facts retain their original definitions: sales value uses completed orders and stored prices, stock is a current snapshot, the demo currency is USD, and replenishment is a bounded policy rather than a forecast.

## Preserved engineering history

| Phase | Original design and verification notes |
| --- | --- |
| Initial assistant / AI foundation | [Original assistant architecture](AI_ARCHITECTURE.md), [foundation](AI_FOUNDATION.md), [AI security history](AI_SECURITY.md) |
| 11.1 / 11.2 | [OpenAI](OPENAI_INTEGRATION.md), [original local Ollama integration](OLLAMA_INTEGRATION.md) |
| 11.3 / reliability | [Intelligent agents](INTELLIGENT_AGENTS.md), [baseline inspection](AGENTS_BASELINE.md), [response reliability](AI_RESPONSE_RELIABILITY.md) |
| 11.4 / 11.5 | [Secure proposals](SECURE_WORKFLOWS.md), [enterprise knowledge](ENTERPRISE_RAG.md) |
| 11.6 / 11.7 / 11.8 | [Monitoring](PROACTIVE_OPERATIONS.md), [quality](AI_QUALITY.md), [visual workflows](VISUAL_WORKFLOWS.md) |

Historical verification reports, migration history, SQL measurements and earlier screenshot assets are preserved. No production deployment, external collector, tenant boundary or current remote CI success is inferred from a local phase report.
