# AI security and data governance

[Architecture, tool matrix and configuration](AI_ARCHITECTURE.md) · [Application security](SECURITY.md) · [Authorization](AUTHORIZATION.md)

AI is disabled by default. Enabling an external provider sends permitted question
content and selected ERP data outside the application environment. It does not keep
all processing local. The frontend displays this disclosure before requests. A demo
banner is not a replacement for an organization's privacy notice or legal review.

## Enforced boundaries

The model has nine fixed read-only tools, each with an explicit BusinessRead policy.
There is no generic API, SQL, shell, filesystem, URL fetch, user-management, audit
retrieval or mutation tool. Model-generated names map only to the fixed registry
and switch executor. Report filters become LINQ expression parameters, never SQL
text. The executor holds a DbContext for current-session checks and reporting services
for reads; it calls no SaveChanges. Existing business services are not exposed.

JWT middleware validates signature, issuer, audience, expiry and current account
state. Every tool repeats active-user, SecurityVersion and current-role checks and
applies the registered policy. Model arguments cannot select user ID or role, and
unknown/duplicate properties are rejected. Viewer cannot use this surface to invoke
Admin operations. A final recheck rejects delivery after revocation during a provider
wait. These checks reduce revocation races; they cannot retract already transmitted
provider data or make multiple queries one atomic database snapshot.

Schemas use strict OpenAI function calling with additionalProperties=false. Server
validation remains mandatory: schema assistance is not authorization. Tool IDs,
arguments, dates, limits, rounds, result size and answer size are bounded. No client
system message or model choice is trusted. Exact limits appear in AI_ARCHITECTURE.

## Data sent and excluded

The provider receives the user's question, server-owned business instructions,
fixed tool definitions, selected aggregate/list data and metadata. Product and order
display strings are truncated to 120 characters. Customer rankings expose a stable
12-hex-character SHA-256-derived identifier instead of a name or raw ID. This is
pseudonymization, not anonymization: repeated IDs and business patterns can still
be identifying. Tool results exclude customer contacts, hashes, JWTs, secrets,
connection strings, raw audit logs and unused entity fields. Complete tables are not
materialized or forwarded; grouping/ranking/Take occur in PostgreSQL.

User questions can themselves contain sensitive information. The UI warns against
including it; there is no claim of comprehensive input DLP/redaction. Permission to
read ERP reports does not automatically establish permission to share them with an
external provider. Deployment owners must review classification and permitted AI
usage before enabling the feature.

Credentials remain in backend configuration/environment. Docker passes AI settings
only to the backend, with no VITE secret variable. Status exposes booleans only.
The OpenAI endpoint is restricted to the official HTTPS base URL; arbitrary hosts
are rejected before a credential can be sent. Real keys belong in ignored local
configuration or a deployment secret manager. Container environment access is itself
privileged; restrict access and rotate compromised credentials. No real key is needed
for ERP startup, automated tests or UI state verification.

## Prompt injection and answer integrity

Both user messages and catalog strings may contain hostile instructions. Instructions
tell the model to treat them as data, but prompts do not establish a security boundary.
Tests deliberately return unsupported calls following injected user/product text and
verify rejection. Strict dispatch, parameters, current permissions and read-only
reporting are the enforceable protections. They prevent unsupported operations even
if the model follows an injection. Injection may still cause misleading summaries,
irrelevant tool selection within the allowed read surface or disclosure of already
permitted results. Deployment evaluations should cover these residual risks.

All authoritative totals, averages, period comparisons and coverage are computed in
backend reporting queries. Sources come from actual executed tools. The model may
still misquote numbers or interpret nonempty results incorrectly. Zero-result and
no-tool answers use deterministic server text. Users must verify figures in the source
reports. Language-tag validation does not prove the text is linguistically correct.
React renders model text without HTML/Markdown execution; only fixed server report
paths become links. Source periods remain visible, and no model URL is made clickable.

## Failure, quotas and logging

Disabled/missing/invalid provider settings do not block ERP startup. Timeouts,
cancellation, provider throttling/unavailability, malformed answers, denied tools and
query failures become safe ProblemDetails. Provider exception bodies and inner
exceptions are discarded, so the existing unexpected-error logger receives no raw
provider secrets. Requests do not receive automatic SDK or UI retries.

Per-user minute/day counters, bounded inputs/results/output tokens, five total calls,
two tool rounds and a global cancellable deadline limit abuse and cost. Counters are
local to one process and reset on restart; they do not provide multi-instance or
financial guarantees. A gateway/shared limiter and provider spend controls are
appropriate deployment follow-ups, not implemented demo infrastructure.

Operational logs include only request/user IDs, UTC time, provider/model, successfully
executed tool names, duration, outcome and token usage when available. No full prompt,
conversation, raw tool result or business mutation audit event is logged by AI code.
User/request identifiers are still operational personal data and need log access and
retention controls. No chat table is created; the UI keeps at most 20 exchanges in
memory and resets on reload/navigation. Language preference and existing auth session
storage remain separate from conversations.

## Deployment review required before enabling

- Classify which business aggregates, product/order strings and pseudonymous customer metrics may leave the environment.
- Review current provider retention, processing, abuse-monitoring and contractual terms for the actual account and endpoint.
- Verify geographic processing/data residency; no location guarantee is implied here.
- Establish privacy notices, consent or another applicable organizational basis where required.
- Approve organizational AI policy, role eligibility and permitted questions; this demo shares BusinessRead across the three existing roles.
- Configure key access/rotation, quotas, spend alerts, log access and operational retention.
- Evaluate live-provider accuracy, TR/EN responses and adversarial questions with suitable nonproduction data and explicit authorization.

Phase 6 assumes none of these provider retention, residency or compliance guarantees.
Live-provider calls are optional and require explicit authorization. Automated tests
use fake clients and intercepted transport; they establish code boundaries, not model
accuracy or provider governance. RAG remains future work with separate authorization
and document classification requirements.
