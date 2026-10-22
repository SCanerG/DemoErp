# AI response reliability

The React assistant and Agent Control Center call authenticated .NET endpoints. The backend selects the provider, authorizes each allowlisted reporting tool, validates its arguments and typed output, and validates the final explanation before returning it to React. Ollama stays at `http://127.0.0.1:11434`; the browser never calls it directly.

## Confirmed findings

* The former Ollama adapter added native tool-selection instructions even to the tool-free final JSON phase. These instructions conflicted with the final answer contract.
* It required optional completion/usage fields and rejected null tool-call collections, JSON-encoded argument objects, complete JSON fences and leading complete Qwen reasoning blocks. Protocol regression tests now distinguish these valid variations from malformed output.
* A real Qwen3:8b collaboration request exhausted its 2,000 output-token budget twice. The new diagnostics recorded `output_truncated`; the bounded recovery returned an explicitly identified deterministic report rather than claiming a generated AI explanation. The observed two attempts took approximately 111 seconds together.
* React previously displayed answer strings as plain text, so Markdown delimiters were visible.

The exact raw response from the user's earlier incident is unavailable. The findings establish concrete compatibility bugs and a reproduced truncation case; they do not establish that every historical error had the same cause.

## Compatibility and recovery

Native tool selection and schema-constrained final JSON are separate phases. `think:false` and `stream:false` remain explicit. Separate `message.thinking` is never displayed. Only a complete leading `<think>...</think>` wrapper or a complete enclosing JSON fence is normalized; incomplete reasoning, malformed JSON and wrong schemas remain invalid. Missing usage is unknown, not a fabricated measured count. Native argument objects and bounded JSON-encoded objects go through the same existing strict tool registry. Unknown fields, wrong types, unauthorized tools and SQL/shell requests are never repaired into executable arguments.

Tool selection can receive one additional attempt for a bounded allowlist of empty/incomplete response failures before any tool dispatch. Tool-call shapes, unsafe arguments and authorization failures are never repaired. Exhaustion returns `aiToolSelectionFailed` with a useful clarification message. The existing agent model-call budget still applies to every attempt.

The final explanation can independently receive one additional attempt after an invalid/incomplete output. Tools are not replayed. After two failed final validations, a deterministic template uses already verified typed ERP facts. Timeouts, context limits, authentication failures, forbidden tools and attempted secret disclosure are not converted into a successful fallback. Existing cancellation, execution budgets, tool limits, authorization rechecks and audit ownership still apply.

The repair explicitly asks for a short qualitative explanation without restating numeric quantities. This prevents repeatedly retrying a rounded or differently formatted figure that failed numerical provenance checks; authoritative values remain visible in the typed UI/source reports.

Responses expose `responseKind`: `model`, `fallback`, `deterministic` or `mock`. Both screens label fallback explicitly. Persisted fallback content has a fixed identifying prefix, so reopening history preserves its provenance without a schema migration. Known deterministic guidance/no-data templates are identified as well.

Exact current-count questions such as “Kaç sipariş beklemede?” or “How many orders are pending?” use a small exact-phrase routing table and the existing `get_pending_orders` tool. Both the assistant's All reports mode and Agent Center's Automatic mode return a verified `deterministic` count, including zero, with an audited source and no LLM call. This is deliberately not labelled a generated model explanation. The normal executor still performs authorization and argument validation. Date-filtered, compound, ambiguous and write requests do not match this shortcut. Inventory-only mode is not expanded to read orders. The assistant remains limited to the implemented ERP reports, not arbitrary general questions.

Final summaries must use verified facts, explain implications naturally and avoid raw field dumps. An additional conservative numerical-value check rejects figures absent from the verified facts and understands Turkish/English decimal conventions. It does **not** prove that a model associated every value with the correct entity or interpreted every fact correctly; the typed tables and source reports remain authoritative. Written-out numbers and semantic claims still require human review. The check can conservatively fall back when a valid figure uses an unexpected format.

## Rendering and diagnostics

The shared React renderer supports paragraphs, line breaks, headings, bold/italic, lists and escaped code blocks. It does not parse HTML or use `dangerouslySetInnerHTML`. Model-supplied links/images render as labels and cannot fetch resources or navigate. Existing server-provided allowlisted report links remain available. This is a deliberately bounded Markdown subset, not a complete Markdown engine.

Safe error codes distinguish unavailable provider (`aiProviderUnavailable`), unavailable model (`aiModelUnavailable`), failed validation (`aiInvalidResponse`), rejected tools/arguments (`aiUnknownTool`, `aiInvalidArguments`, `aiForbidden`), incomplete output (`aiIncompleteResponse`), timeout (`aiTimeout`) and context limit (`aiContextLimit`). Ollama normally loads an installed model on demand; a missing model is distinct from a temporarily busy request.

Backend metadata logs include provider, configured model, execution ID, outcome/error category, validation subtype, latency and known output-token counts. Tool logs include execution ID, fixed tool name, outcome and duration. Final provenance is logged separately. Failed attempts contribute known token usage to execution accounting. No prompts, ERP rows, tool arguments, reasoning, model bodies or credentials are added to these diagnostics. Context character limits remain conservative guards rather than an exact model tokenizer.

## Windows execution

Keep the existing database settings and JWT secret in your local PowerShell session. Stop/restart the old backend so it loads the updated code. No re-bootstrap or new business migration is needed for this reliability change.

```powershell
Set-Location (Join-Path $env:USERPROFILE 'source\DemoErp')
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\start-ollama-backend.ps1
```

In a second terminal:

```powershell
Set-Location (Join-Path $env:USERPROFILE 'source\DemoErp\frontend')
$env:VITE_API_BASE_URL = 'http://127.0.0.1:5101/api'
npm.cmd ci
node node_modules/vite/bin/vite.js --host 127.0.0.1 --port 3021 --strictPort
```

Opt-in authenticated live test, from the repository root in a third terminal, using an existing local ERP account:

```powershell
$env:API_URL = 'http://127.0.0.1:5101'
$env:AI_LIVE_EMAIL = Read-Host 'ERP test email'
$env:AI_LIVE_PASSWORD = [System.Net.NetworkCredential]::new('', (Read-Host 'ERP test password' -AsSecureString)).Password
$env:OLLAMA_LIVE_SMOKE = 'true'
try { node scripts/ai-ollama-smoke.mjs }
finally {
  Remove-Item Env:AI_LIVE_PASSWORD -ErrorAction SilentlyContinue
  Remove-Item Env:OLLAMA_LIVE_SMOKE -ErrorAction SilentlyContinue
}
```

The live test prints safe metadata including `responseKind`. A verified provider connection with `fallback` does not mean a model explanation passed validation. For the four-agent acceptance test, use the same local credentials and API URL, set `AI_AGENTS_LIVE_SMOKE=true`, then run `node scripts/ai-agents-live-smoke.mjs`. Run it against an isolated database without concurrent writers: it compares business state before/after and never seeds data itself.

Regression commands (requires .NET 10, Node and Docker for disposable PostgreSQL):

```powershell
.\.tools\dotnet\dotnet.exe test --project backend/Demo.Api.Tests/Demo.Api.Tests.csproj -c Reliability
npm.cmd --prefix frontend run lint
npm.cmd --prefix frontend test
npm.cmd --prefix frontend run build
node --test scripts/tests/*.test.mjs
```

Use `dotnet` instead of the bundled path when .NET 10 is installed globally. The real backend test is additionally opt-in with `OLLAMA_LIVE_SMOKE=true` and filter `--filter 'FullyQualifiedName~Live_Ollama_through'`; it uses synthetic data in disposable PostgreSQL and calls the actual local model.

## Order intent and summary semantics follow-up (2026-10-21)

The screenshot's Turkish `sipariş taslağı` was not matched by the `taslak` substring: Turkish changes the final k to ğ. An explicitly selected Sales mode also overrode the requested workflow. Automatic order planning previously offered sales and inventory routes, allowing the model to select an unrelated report.

Order routing now recognizes the inflection, limits the planner schema to order or clarification for recognized draft requests, and validates the returned route independently. Explicit incompatible modes return `aiAgentModeMismatch` before any model or reporting tool executes. Selecting an example resets the UI mode to Automatic. Missing or ambiguous customer/product identities request clarification; drafts remain unpersisted and never submit orders.

The previous numeric validator checked whether a number existed anywhere in verified facts. It could therefore miss attaching USD amounts to TL or another product's stock. `AiSummarySemantics` adds currency checks for the USD reporting contract and product-associated stock checks to both chat and agent summaries. Rejected summaries use the existing bounded repair and explicitly labelled fallback path. Provider instructions also preserve currency and product associations. Authorization and tool argument checks are unchanged.

These are conservative checks for supported stock phrases and currencies, not a complete proof of every natural-language claim. Unrecognized wording can still escape a semantic check; ambiguous product references can cause a fallback. Typed ERP facts remain the authoritative source. The assistant still has a bounded reporting/draft scope, not arbitrary ERP capabilities.

Additional changed files: `backend/Demo.Api/AI/AiSummarySemantics.cs`, `backend/Demo.Api/AI/Agents/AgentPlanner.cs`, `backend/Demo.Api.Tests/AiIntentTests.cs`. Existing orchestration, chat, Ollama instructions, Agent Center, translations and browser tests were updated. Actual follow-up results are recorded at the top of `VERIFICATION.md`.

## Changed files

Paths are relative to the repository root.

| Area | Files |
| --- | --- |
| Ollama protocol mapping and bounded initial selection | `backend/Demo.Api/AI/OllamaModelClient.cs`, `backend/Demo.Api/AI/AiToolSelection.cs` |
| Final validation, recovery, verified templates | `backend/Demo.Api/AI/AiAnswerComposer.cs`, `backend/Demo.Api/AI/Agents/AgentFallback.cs` |
| Chat and agent execution | `backend/Demo.Api/AI/AiAssistantService.cs`, `backend/Demo.Api/AI/Agents/AgentOrchestrator.cs` |
| Contracts and history provenance | `backend/Demo.Api/AI/AiContracts.cs`, `backend/Demo.Api/AI/Agents/AgentContracts.cs`, `backend/Demo.Api/AI/AiConversationStore.cs`, `backend/Demo.Api/AI/AiBoundaries.cs` |
| Safe diagnostics and DI | `backend/Demo.Api/AI/AiResponseDiagnostics.cs`, `backend/Demo.Api/AI/AiExecutionJournal.cs`, `backend/Demo.Api/Program.cs` |
| Shared safe rendering | `frontend/src/AiResponseText.tsx`, `frontend/src/pages/AiAssistant.tsx`, `frontend/src/pages/AgentCenter.tsx`, `frontend/src/translations.ts` |
| Regression and live tests | `backend/Demo.Api.Tests/AiReliabilityTests.cs`, `backend/Demo.Api.Tests/AiSelectionTests.cs`, `frontend/src/AiResponseText.test.tsx`, `frontend/tests/ai.spec.ts`, `frontend/tests/ai-ollama.spec.ts`, `frontend/tests/agent-center.spec.ts`, `frontend/tests/states.spec.ts` (controlled old-session race fixture only) |
| Live metadata reporting | `scripts/ai-live-smoke.mjs`, `scripts/ai-agents-live-smoke.mjs` |
| Documentation and actual results | `docs/AI_RESPONSE_RELIABILITY.md`, `VERIFICATION.md` |
