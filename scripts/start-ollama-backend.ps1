param([switch]$Bootstrap)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://127.0.0.1:5101'
if (-not $env:ConnectionStrings__Default) { throw 'Set ConnectionStrings__Default for your local PostgreSQL database first. See docs/OLLAMA_INTEGRATION.md.' }
if (-not $env:Jwt__Secret) { throw 'Set Jwt__Secret (at least 32 bytes) first. See docs/OLLAMA_INTEGRATION.md.' }
$env:Jwt__Issuer = 'catalog-api'
$env:Jwt__Audience = 'catalog-web'
$env:Cors__Origins__0 = 'http://127.0.0.1:3021'
$env:AiAssistant__Enabled = 'true'
$env:AiAssistant__Provider = 'Ollama'
$env:OLLAMA_ENDPOINT = 'http://127.0.0.1:11434'
if (-not $env:OLLAMA_MODEL) { $env:OLLAMA_MODEL = 'qwen3:8b' }
$env:AiAssistant__TimeoutSeconds = '120'
$env:AiAssistant__MaxOutputTokens = '2000'
$env:AiAssistant__MaxRetries = '0'
$env:Bootstrap__Enabled = if ($Bootstrap) { 'true' } else { 'false' }
if ($Bootstrap) {
    $env:Bootstrap__Name = 'Local administrator'
    $env:Bootstrap__Email = Read-Host 'Local ERP administrator email'
    $env:Bootstrap__Password = [System.Net.NetworkCredential]::new('', (Read-Host 'Local ERP administrator password' -AsSecureString)).Password
}
$dotnet = if (Test-Path '.tools/dotnet/dotnet.exe') { (Resolve-Path '.tools/dotnet/dotnet.exe').Path } else { 'dotnet' }
try { & $dotnet run --project backend/Demo.Api --no-launch-profile; exit $LASTEXITCODE }
finally { Remove-Item Env:Bootstrap__Password -ErrorAction SilentlyContinue }
