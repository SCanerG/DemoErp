# Local Ollama and provider configuration

[Getting started](GETTING_STARTED.md) · [Advanced isolated PowerShell setup](VISUAL_WORKFLOWS.md#windows-local-setup)

The current `IAiModelClient` abstraction preserves OpenAI, Ollama and Development Mock. Ollama uses the host's numeric loopback endpoint **`http://127.0.0.1:11434`**; arbitrary hosts/paths are rejected. Ollama and Mock are restricted to Development. Browser code calls the .NET API, never Ollama directly.

## Models and hardware

Actual local inference has been verified with **`qwen3:8b`** through DemoERP. Local knowledge uses **`nomic-embed-text`** and its recorded digest/dimension identity. Models are configurable, but another model's function-call/structured-output behavior must be verified before claiming compatibility.

Install Ollama for Windows using its [official download](https://ollama.com/download/windows), keep the local service running, and use:

```powershell
ollama list
ollama pull qwen3:8b
ollama pull nomic-embed-text
```

Only pull missing models; downloads require storage and network access. The reviewed machine has 32 GB RAM and an RTX 4060 with 8 GB VRAM. This is a tested configuration, not a minimum requirement. Model size, quantization, context, residency, GPU offload and concurrent embedding work affect latency/memory. CPU-only operation or another GPU is not promised to match recorded results. Do not expose the local Ollama port publicly.

## Native backend configuration

Set these in the **backend PowerShell terminal**, in addition to the database/JWT/CORS values from [Getting started](GETTING_STARTED.md):

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:AiAssistant__Enabled = 'true'
$env:AiAssistant__Provider = 'Ollama'
$env:OLLAMA_ENDPOINT = 'http://127.0.0.1:11434'
$env:OLLAMA_MODEL = 'qwen3:8b'
$env:OLLAMA_CONTEXT_WINDOW = '8192'
$env:AiAssistant__TimeoutSeconds = '120'
$env:AiAssistant__MaxOutputTokens = '2000'
$env:AiAssistant__MaxRetries = '0'
```

Restart only that backend after changing settings. Native environment names are not interchangeable with the base Compose `AI_*` mapping. For optional document knowledge, also enable `Knowledge__Enabled` and configure `Knowledge__EmbeddingModel`; pgvector and indexing are separate requirements. Workflow execution and monitoring each have their own opt-in. [Complete combined setup and exact live command](VISUAL_WORKFLOWS.md#tests) · [RAG requirements](RAG.md).

## Provider switching

| Provider | Backend settings | Verification boundary |
| --- | --- | --- |
| Ollama | `AiAssistant__Provider=Ollama`, `OLLAMA_MODEL`, exact local endpoint | Actual Qwen requests verified locally; no paid inference API call |
| OpenAI | `AiAssistant__Provider=OpenAI`, backend-only `OPENAI_API_KEY`, actual supported `OPENAI_MODEL` ID | SDK/transport tests verified; successful live inference is not recorded |
| Mock | `AiAssistant__Provider=Mock` in Development | Clearly labelled deterministic development output; not a model call |

Use canonical API model IDs rather than UI display names. Never place provider keys in a `VITE_*` variable, command output, screenshot or commit. [OpenAI configuration and opt-in test](OPENAI_INTEGRATION.md).

Local inference avoids paid inference API requests but still consumes local hardware resources. Deterministic ERP operations and model-free workflow nodes continue working when Ollama is disabled/unavailable. Optional AI failures are returned as safe categories; fallback output is labelled. Readiness/model listing alone is not a successful model execution. [Actual evidence](../VERIFICATION.md) · [Original integration and troubleshooting](OLLAMA_INTEGRATION.md).
