# Agent orchestration and approval

[Diagram index](README.md) · [Agent guide](../AI_AGENTS.md)

## Agent orchestration

```mermaid
sequenceDiagram
    actor User
    participant UI as Agent Control Center
    participant API as Authenticated .NET API
    participant Coord as AgentOrchestrator
    participant Sales as Sales Agent / fixed reporting tools
    participant Stock as Inventory Agent / typed reads
    participant DB as PostgreSQL
    participant Model as Configured model
    User->>UI: Analyze three-month sales and replenishment
    UI->>API: Start and execute authorized run
    API->>Coord: Current user and bounded input
    Coord->>Coord: Fixed collaboration route / budgets
    Coord->>Sales: Recheck access and query period
    Sales->>DB: Parameterized reporting reads
    DB-->>Sales: Backend-calculated sales facts
    Sales-->>Coord: Typed facts and actual sources
    Coord->>Stock: Recheck access and query stock/demand
    Stock->>DB: Authorized typed reads
    DB-->>Stock: Current balances and completed demand
    Stock-->>Coord: Policy-based recommendation
    Coord->>Model: Bounded verified facts for explanation
    Model-->>Coord: Provider-independent answer
    Coord->>Coord: Validate / bounded repair / labelled fallback
    Coord-->>UI: Facts, provenance, sources and step metadata
    UI-->>User: Separate verified tables and interpretation
```

Execution is sequential and bounded, not open-ended inter-agent chat. Some explicit requests use deterministic routing; supported ambiguous intents use one restricted routing tool. Sales and Inventory calculations are backend operations, not model arithmetic. The summary model may fail or produce unsafe output; validation and a clearly labelled deterministic fallback protect factual presentation. Model text remains an interpretation requiring source review.

## Secure order approval

```mermaid
sequenceDiagram
    actor User
    participant UI as React review controls
    participant Agent as Order / procurement draft path
    participant Proposal as WorkflowProposals
    participant Business as Existing ERP service
    participant DB as PostgreSQL
    User->>Agent: Request supported draft
    Agent->>Agent: Resolve active references and current prices
    Agent->>Proposal: Prepare exact pending facts
    Proposal->>DB: Persist private proposal, fingerprint and expiry
    Proposal-->>UI: Exact changes and pending state
    Note over UI,DB: No ERP order exists merely because a proposal exists
    User->>UI: Review checkbox and explicit approval click
    UI->>Proposal: Authenticated approval with idempotency key
    Proposal->>Proposal: Recheck current identity, scope, expiry and fingerprint
    Proposal->>Business: Revalidate business facts and execute once
    Business->>DB: Existing transaction: order / related audit
    DB-->>Business: Actual saved result
    Business-->>Proposal: Verified outcome
    Proposal-->>UI: Saved receipt or safe rejection
    Note over Proposal,DB: Replay returns the original result. Mismatched replay is rejected.
```

Sales approvals create a Pending sales order; order confirmation and stock deduction remain distinct business operations. Purchase proposals create the supported purchase document through procurement services; they do not auto-receive stock. Changed prices/references/permissions invalidate stale approval. The visual workflow's approved-result node verifies the saved result and never substitutes for human approval.

Source anchors: [orchestrator](../../backend/Demo.Api/AI/Agents/AgentOrchestrator.cs), [plan validation](../../backend/Demo.Api/AI/Agents/AgentPlanner.cs), [proposal service](../../backend/Demo.Api/AI/Agents/WorkflowProposals.cs), [business services](../../backend/Demo.Api/Services/BusinessServices.cs). [Detailed proposal lifecycle and limits](../SECURE_WORKFLOWS.md).
