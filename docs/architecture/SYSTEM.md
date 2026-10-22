# System and provider boundaries

[Diagram index](README.md) · [Source overview](../ARCHITECTURE.md)

## System architecture

```mermaid
flowchart LR
    BROWSER["React browser UI<br/>TR / EN · role-aware navigation"] -->|"JSON / Bearer JWT"| API["ASP.NET Core API"]
    API --> SECURITY["Current user / role / SecurityVersion<br/>authorization and validation"]
    SECURITY --> ERP["Scoped ERP business services"]
    SECURITY --> AI["Bounded assistant and agent coordinator"]
    AI --> READ["Fixed reporting tools<br/>typed reads / authorized retrieval"]
    AI <--> PROVIDER["IAiModelClient"]
    PROVIDER --> LOCAL["Ollama on numeric loopback"]
    PROVIDER --> EXTERNAL["Optional OpenAI Responses"]
    PROVIDER --> MOCK["Development Mock"]
    AI --> PENDING["Private pending proposal"]
    PENDING --> REVIEW["Explicit authorized human approval"]
    REVIEW --> ERP
    READ --> DB[("PostgreSQL<br/>Optional pgvector")]
    ERP --> DB
    WORKERS["In-process indexing / monitoring / workflow workers"] --> DB
    API -.-> META["Private execution and quality metadata"]
    META --> DB
```

The browser never receives provider credentials or a direct Ollama connection. Stock/document/audit writes share the existing transactional business services. Local Compose has three services: PostgreSQL, the API and an unprivileged Nginx frontend. Native Windows AI development uses a host API and Vite with explicit loopback/CORS settings. No public deployment or cloud inference topology is assumed.

## Backend layers

```mermaid
flowchart TD
    HTTP["Controllers<br/>HTTP status · request limits · authorization policies"] --> CONTRACTS["Typed contracts / FluentValidation"]
    CONTRACTS --> APP["Services and bounded AI application services"]
    APP --> DOMAIN["Domain entities and business state rules"]
    APP --> DATA["AppDbContext / EF Core mappings / migrations"]
    DOMAIN --> DATA
    DATA --> PG[("PostgreSQL constraints<br/>transactions · row/advisory locks")]
    APP --> AUDIT["Allowlisted business audit + AI execution journal"]
    AUDIT --> DATA
    ERRORS["Safe exception handling / ProblemDetails"] -.-> HTTP
```

These are directories within `backend/Demo.Api`, not separate deployable assemblies. `Services/` owns existing ERP transactions; `AI/Agents`, `AI/Knowledge`, `AI/Monitoring`, `AI/Quality` and `AI/Workflows` add bounded capabilities without replacing them. EF Core is the data access layer; there is no invented repository/CQRS or distributed transaction infrastructure.

## Provider abstraction

```mermaid
flowchart TD
    CALLER["Assistant / orchestrator / optional workflow drafting"] --> RUNNER["IAiModelClient<br/>shared typed messages and model turns"]
    RUNNER --> OBS["ObservedModelClient<br/>safe metadata / available token counts"]
    OBS --> OA["OpenAiModelClient<br/>official SDK · Responses · store=false"]
    OBS --> OL["OllamaModelClient<br/>local chat / native function calls"]
    OBS --> MK["Development Mock client<br/>explicit mock provenance"]
    OA --> VALIDATE["Strict call / answer validation"]
    OL --> VALIDATE
    MK --> VALIDATE
    VALIDATE --> APP["Application authorization and trusted dispatch"]
    EMBED["IKnowledgeEmbeddingProvider"] --> OE["Local Ollama embeddings"]
```

The chat provider abstraction is separate from the embedding abstraction. OpenAI and Ollama preserve provider-independent contracts; validation does not authorize an operation by itself. Current identity, fixed tool dispatch and business revalidation remain backend responsibilities. Known provider-specific output differences are handled by adapters and bounded repair paths, with explicit fallback provenance.

`AiAssistantOptions` permits Ollama/Mock only in Development and restricts the Ollama endpoint to `http://127.0.0.1:11434`. OpenAI uses the configured supported model ID and backend-only key. Successful live OpenAI generation is not verified; local Qwen generation is. [Provider configuration](../LOCAL_OLLAMA.md) · [Responses implementation](../OPENAI_INTEGRATION.md).

Source anchors: [Program](../../backend/Demo.Api/Program.cs), [services](../../backend/Demo.Api/Services/BusinessServices.cs), [contracts](../../backend/Demo.Api/AI/AiContracts.cs), [options](../../backend/Demo.Api/AI/AiAssistantOptions.cs), [data context](../../backend/Demo.Api/Data/AppDbContext.cs).
