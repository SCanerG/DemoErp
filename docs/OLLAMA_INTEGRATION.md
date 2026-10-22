> Phase 11.3: [Intelligent agents and Agent Control Center](INTELLIGENT_AGENTS.md) adds typed orchestration, read-only analysis, resolved non-submitted drafts and private step metadata.

# Phase 11.2 â€” Local Ollama integration

[README](../README.md) Â· [OpenAI provider](OPENAI_INTEGRATION.md) Â· [AI security](AI_SECURITY.md)

## Architecture

React `/api/ai/chat` â†’ authenticated .NET runtime â†’ IAiModelClient â†’ Ollama native `/api/chat` â†’ existing validated reporting tools â†’ JSON answer + server sources. Provider selection is server configuration, never a browser-supplied URL. OpenAI and Development Mock remain available. The UI shows the selected provider and offers All reports or Inventory Agent.

Ollama is Development-only and accepts exactly `http://127.0.0.1:11434`. Redirects and proxies are disabled. The backend must run natively on the same Windows host. Container loopback is not Windows loopback; this release intentionally does not accept `host.docker.internal` or require exposing Ollama to the network. Keep Ollama's listener on loopback; do not set OLLAMA_HOST=0.0.0.0. No model pulls are performed by the API.

OLLAMA_MODEL defaults to qwen3:8b. OPENAI_MODEL and OPENAI_API_KEY do not influence this provider. No cloud fallback is attempted. Request timeout, retries, per-user quotas, at most two tool rounds/five calls (Ollama uses one native batch, then constrained final output), bounded payloads and owner-only execution/history rules remain shared. Native usage is prompt_eval_count + eval_count; it is a local token count, not a cloud billing estimate. Thinking is disabled, streaming is disabled, context is bounded at 8192 and output at the configured 100â€“2000 tokens. Response bodies are capped at 128 KiB. Raw provider errors and reasoning are not logged or returned.

Native tool selection runs without a constrained answer grammar; after one native tool batch (or when no tools are offered), the adapter sends the existing answer/language JSON schema. Final content is always validated locally. Native tool outputs use tool_name; server-generated unique call IDs are used for internal matching/audit. The model cannot execute code. The shared executor enforces strict arguments, current role/security version, nine allowlisted read-only reports and typed bounded results before dispatch. Model text is not evidence; source links only come from successful server tool calls.

## Agents

- `agent: "reporting"` (default) offers all nine existing read-only tools.
- `agent: "inventory"` offers only overview, low stock, out of stock and stock risk. The server rejects even valid noninventory tools in this mode. Authorization is the existing shared BusinessRead policy, not tenant filtering.
- `POST /api/ai/order-drafts` accepts `{ "customerId": "UUID", "lines": [{ "productId": "UUID", "quantity": 2 }] }`. This deterministic preparation endpoint returns DraftOnly, persisted=false, requiresHumanReview=true. It does not invoke the model, create an order, verify reference existence, quote prices, reserve stock or calculate totals. IDs and quantities require human review in the normal order flow. It has no OrderService or EF dependency. It is not an executable model tool. Metadata-only preparation audit uses provider DraftOnly, model deterministic-preparation, zero provider calls/tokens and an owner-only execution ID; no draft content is persisted. Existing nine reporting tools are unchanged.
- IRagRetriever.RetrieveAuthorized and IEmbeddingClient are future contracts only. PostgreSQL/pgvector ingestion, embeddings, document permissions and retrieval are not implemented or registered; no extension or migration is added.

## Windows PowerShell startup

Prerequisites: Docker Desktop with Linux engine running, Node.js 22+, .NET SDK 10 and Ollama with qwen3:8b already installed. If this working directory contains `.tools/dotnet/dotnet.exe`, that local SDK can be used instead of installing another SDK. These commands use an isolated database project; they do not replace existing demo volumes. A new database initially has no business records. Use the normal ERP screens to add data, or use an existing database connection deliberately.

From the repository root in terminal 1:

```powershell
Invoke-RestMethod http://127.0.0.1:11434/api/tags | Select-Object -ExpandProperty models | Select-Object name
docker compose --env-file .env.example -p demoerp-local-ollama -f docker-compose.yml -f docker-compose.ollama-local.yml up -d --wait postgres
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://127.0.0.1:5101'
$env:ConnectionStrings__Default = 'Host=127.0.0.1;Port=55433;Database=catalog;Username=catalog;Password=development-only-db-password'
# If you deliberately override POSTGRES_DB/USER/PASSWORD in this session, use those values above.
$env:Jwt__Secret = 'local-development-only-signing-key-change-before-sharing'
$env:Jwt__Issuer = 'catalog-api'
$env:Jwt__Audience = 'catalog-web'
$env:Cors__Origins__0 = 'http://127.0.0.1:3021'
$env:AiAssistant__Enabled = 'true'
$env:AiAssistant__Provider = 'Ollama'
$env:OLLAMA_ENDPOINT = 'http://127.0.0.1:11434'
$env:OLLAMA_MODEL = 'qwen3:8b'
$env:AiAssistant__TimeoutSeconds = '120'
$env:AiAssistant__MaxOutputTokens = '2000'
$env:AiAssistant__MaxRetries = '0'
# Only for a new, empty local database; credentials stay in this process.
$env:Bootstrap__Enabled = 'true'
$env:Bootstrap__Name = 'Local administrator'
$env:Bootstrap__Email = Read-Host 'Local ERP administrator email'
$env:Bootstrap__Password = [System.Net.NetworkCredential]::new('', (Read-Host 'Local ERP administrator password' -AsSecureString)).Password
$dotnet = if (Test-Path '.tools/dotnet/dotnet.exe') { (Resolve-Path '.tools/dotnet/dotnet.exe').Path } else { 'dotnet' }
& $dotnet run --project backend/Demo.Api --no-launch-profile
```

Bootstrap never promotes an existing user. If using an existing database, omit bootstrap variables and sign in normally. Startup applies existing committed migrations. Back up important databases before changing application versions; never delete their volumes to fix startup.

From repository root in terminal 2:

```powershell
Set-Location frontend
$env:VITE_API_BASE_URL = 'http://127.0.0.1:5101/api'
npm.cmd ci
node node_modules/vite/bin/vite.js --host 127.0.0.1 --port 3021 --strictPort
```

Open http://127.0.0.1:3021, sign in, open AI Assistant, select Inventory Agent and ask a stock question. Cancel and safe error UI remain unchanged. Existing saved conversations are historical context and do not establish current inventory.

## PowerShell script execution policy

If PowerShell reports that running scripts is disabled, `npm` may resolve to blocked npm.ps1; use `npm.cmd` as shown above. To start the backend helper without a permanent policy change, set ConnectionStrings__Default and Jwt__Secret as above, then run this from the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\start-ollama-backend.ps1 -Bootstrap
```

This option applies only to the child PowerShell process; centrally enforced Group Policy can still override it. Alternatively use the inline native dotnet startup commands above, which do not require the helper script. Do not change LocalMachine or CurrentUser policy for this setup.

Keep backend, frontend and live test in three separate terminal windows. A foreground Vite or dotnet process occupies its terminal; pasted PowerShell commands there are not executed by the shell. Press Ctrl+C before reusing that window. Run each command on its own line; do not append the live test environment settings to the Vite command.

## Opt-in live acceptance test

From repository root in terminal 3, while terminal 1 is running:

```powershell
$env:API_URL = 'http://127.0.0.1:5101'
$env:OLLAMA_LIVE_SMOKE = 'true'
$env:AI_LIVE_EMAIL = Read-Host 'Existing authorized ERP email'
$env:AI_LIVE_PASSWORD = [System.Net.NetworkCredential]::new('', (Read-Host 'ERP password' -AsSecureString)).Password
try { node scripts/ai-ollama-smoke.mjs } finally {
  Remove-Item Env:AI_LIVE_PASSWORD -ErrorAction SilentlyContinue
  Remove-Item Env:OLLAMA_LIVE_SMOKE -ErrorAction SilentlyContinue
}
```

This logs in, reads actual inventory, calls the Inventory Agent through DemoERP, checks the low-stock source and owner-only audit metadata/correlation/tokens. It never seeds business data. An empty inventory result is valid and is reported explicitly. It prints metadata only, not passwords or report rows. Default invocation without OLLAMA_LIVE_SMOKE=true skips with no network calls. Passing transport/metadata checks does not certify every model statement; compare the answer with the source report.

An additional automated live test uses a disposable Testcontainers PostgreSQL database with synthetic inventory and a real loopback Ollama model. Docker must be running:

```powershell
$dotnet = if (Test-Path '.tools/dotnet/dotnet.exe') { (Resolve-Path '.tools/dotnet/dotnet.exe').Path } else { 'dotnet' }
$env:OLLAMA_LIVE_SMOKE = 'true'
try { & $dotnet test --filter 'FullyQualifiedName~Live_Ollama_through' } finally { Remove-Item Env:OLLAMA_LIVE_SMOKE }
```

Offline regression checks (no model call):

```powershell
& $dotnet test
node --test scripts/tests/ai-live-smoke.test.mjs
node scripts/ai-ollama-smoke.mjs
Set-Location frontend
npm.cmd run lint
npm.cmd test
npm.cmd run build
```

## Errors and limitations

Connection failure â†’ aiProviderUnavailable; missing model â†’ aiModelUnavailable; rate limit â†’ aiProviderRateLimit; incomplete generation â†’ aiIncompleteResponse; malformed output â†’ aiInvalidResponse. Timeout and cancellation use the shared safe categories. Install the model manually if missing. A configured status means valid settings, not a successful model connection. RTX 4060/8 GB suitability is established by the user's installed model and actual runs, not a guaranteed latency or concurrency promise. No GPU tuning or Ollama listener settings are changed.

See the Phase 11.2 section in [VERIFICATION](../VERIFICATION.md) for actual executed checks and remaining limitations.
