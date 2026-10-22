# Getting started

[Türkçe Windows guide](INSTALLATION.tr.md) · [Local Ollama](LOCAL_OLLAMA.md) · [Documentation](README.md)

These commands describe the reviewed local source tree. The public repository may lag the latest local work; check the exact source revision before expecting optional agent/workflow features. [Publication boundary](REPOSITORY_REVIEW.md).

## 1. Prerequisites and clone

| Workflow | Required programs |
| --- | --- |
| Docker application | Git, Docker Desktop with Linux containers, browser |
| Native source development | Above, plus .NET 10 SDK and Node.js 22.13+ with npm |
| Optional local AI | Ollama; tested chat model `qwen3:8b` |
| Optional document knowledge | pgvector-enabled PostgreSQL and local embedding model `nomic-embed-text` |

```powershell
git clone https://github.com/SCanerG/DemoErp.git
Set-Location DemoErp
```

Run subsequent root commands from this cloned directory. Keep the same database project/volume across restarts if you want to retain its data. A fresh installation starts without the prepared demo accounts or business dataset.

## 2. Fast Docker startup

```powershell
docker compose up -d --build --wait
```

Open `http://localhost:3000`; Development Swagger is `http://localhost:5080/swagger`, readiness is `http://localhost:5080/health/ready`. The base configuration starts PostgreSQL, the API and the built frontend. PostgreSQL is internal to the Compose network. Startup applies the repository's migrations with bounded retries. No manual schema creation is required and no AI key is required.

Public registration creates a Viewer. For a local administrator, configure the one-time bootstrap **before first provisioning**, using an ignored environment file or process environment. The backend does not silently promote an existing account. Choose synthetic credentials locally:

```powershell
$env:BOOTSTRAP_ADMIN_ENABLED = 'true'
$env:BOOTSTRAP_ADMIN_NAME = 'Local demo administrator'
$env:BOOTSTRAP_ADMIN_EMAIL = Read-Host 'New local administrator email'
$bootstrapSecret = Read-Host 'New local administrator password (minimum 12 characters)' -AsSecureString
$env:BOOTSTRAP_ADMIN_PASSWORD = [System.Net.NetworkCredential]::new('', $bootstrapSecret).Password
try {
    docker compose up -d --build --wait
    if ($LASTEXITCODE -ne 0) { throw 'Local Compose startup failed.' }
} finally {
    Remove-Item Env:BOOTSTRAP_ADMIN_PASSWORD -ErrorAction SilentlyContinue
}
$env:BOOTSTRAP_ADMIN_ENABLED = 'false'
docker compose up -d --wait backend
```

This provisioning example is for a local synthetic environment; no real password is stored in the guide. Remove any bootstrap password from ignored files after provisioning too. See [exact bootstrap rules](SECURITY.md#one-time-initial-admin-provisioning). Sign in as Admin to create products, customers, warehouses and other sample records through the application.

To customize Compose ports/keys, create `.env` only if it does not already exist, edit it locally and keep it ignored:

```powershell
if (-not (Test-Path -LiteralPath .env)) { Copy-Item .env.example .env }
```

Development passwords/signing values in `.env.example` are public placeholders, not production secrets. Change frontend origin, browser API URL and host ports together. `VITE_API_BASE_URL` is public build-time configuration; never put secrets in `VITE_*` variables. Changing a database password variable does not rotate a password in an existing volume.

## 3. Native API and React development

Use a separate local database/project to avoid changing an existing application. This optional override provides pgvector and publishes PostgreSQL on **loopback 55444**, allowing later knowledge tests without replacing a working database:

```powershell
docker compose --env-file .env.example -p demoerp-source-review -f docker-compose.yml -f docker-compose.workflow-local.yml up -d --wait postgres
```

In an API terminal at the repository root:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ConnectionStrings__Default = 'Host=127.0.0.1;Port=55444;Database=catalog;Username=catalog;Password=development-only-db-password'
$env:Jwt__Secret = 'local-source-development-signing-key-change-before-sharing'
$env:Jwt__Issuer = 'catalog-api'
$env:Jwt__Audience = 'catalog-web'
$env:Cors__Origins__0 = 'http://127.0.0.1:3041'
$env:AiAssistant__Enabled = 'false'
dotnet restore Catalog.slnx
dotnet run --project backend/Demo.Api --no-launch-profile --urls http://127.0.0.1:5121
```

The API applies migrations on startup. For first Admin creation in this native terminal, use `Bootstrap__Enabled`, `Bootstrap__Name`, `Bootstrap__Email` and a locally entered `Bootstrap__Password`; these native names differ from Compose's `BOOTSTRAP_ADMIN_*` mapping. [Security setup](SECURITY.md).

In a second terminal, from the repository root:

```powershell
Set-Location frontend
npm.cmd ci
$env:VITE_API_BASE_URL = 'http://127.0.0.1:5121/api'
node node_modules/vite/bin/vite.js --host 127.0.0.1 --port 3041 --strictPort
```

Open `http://127.0.0.1:3041`. `npm.cmd` avoids Windows PowerShell's `npm.ps1` script-policy issue. Keep both API and frontend terminals running; do not paste test commands into a terminal occupied by a running server. Use Ctrl+C to stop each native process.

## 4. Optional AI, knowledge and automation

- [Local Ollama](LOCAL_OLLAMA.md): exact native settings, model selection and hardware limits.
- [Combined workflow setup](VISUAL_WORKFLOWS.md#windows-local-setup): initial Admin, Ollama, pgvector, frontend and opt-in actual model/API/UI tests.
- [RAG](RAG.md): document authorization, indexing and citation requirements.
- [Proactive operations](PROACTIVE_OPERATIONS.md): report-only scheduled checks and private alerts.

Optional features have separate backend switches. Setting a native option in a shell does not make the base Compose backend forward it automatically. The supplied combined guide deliberately runs the API natively. Keep local Ollama on numeric loopback; do not expose it to the public network.

## 5. Tests and shutdown

```powershell
dotnet test backend/Demo.Api.Tests
node --test scripts/tests/*.test.mjs
Push-Location frontend
try {
    npm.cmd run lint
    npm.cmd run typecheck
    npm.cmd test
    npm.cmd run build
} finally { Pop-Location }
```

Backend tests use their own Testcontainers databases. Browser tests create records and require a disposable configured stack; do not use a database with personal/business data. AI evaluations use fixed synthetic cases; live model tests require explicit opt-in. [Complete test commands](TESTING.md).

Stop the base Compose application with `docker compose stop`. Stop only the separate source-review PostgreSQL with:

```powershell
docker compose --env-file .env.example -p demoerp-source-review -f docker-compose.yml -f docker-compose.workflow-local.yml stop postgres
```

These commands retain volumes. Never add volume deletion to an upgrade or ordinary shutdown. Hosting on the public internet requires additional [deployment controls](DEPLOYMENT.md); this guide establishes a local development environment.
