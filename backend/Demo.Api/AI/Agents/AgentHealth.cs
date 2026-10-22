using System.Net.Http.Json;
using System.Text.Json;
namespace Demo.Api.AI.Agents;
// Metadata-only probe; never generates text, downloads a model or reads a cloud credential.
public sealed class AgentHealth(AiAssistantOptions options,HttpClient http)
{
    private readonly SemaphoreSlim gate=new(1,1); private string health="unknown";private DateTimeOffset checkedAt=DateTimeOffset.MinValue;
    public async Task<AgentProviderStatus> Read(CancellationToken ct)
    {
        string State(string state)=>!options.Enabled?"disabled":!options.Configured?"notConfigured":state;
        if(options.Provider!="Ollama"||!options.Enabled||!options.Configured)
            return new(options.Enabled,options.Configured,options.Provider,options.Model,options.Provider=="Mock",State(options.Provider=="Mock"?"mock":"configured"),options.OllamaContextWindow);
        await gate.WaitAsync(ct);
        try {
            if(DateTimeOffset.UtcNow-checkedAt>TimeSpan.FromSeconds(15)) {
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(3));
                try {
                    using var response=await http.PostAsJsonAsync(options.OllamaEndpoint+"/api/show",new {model=options.Model},timeout.Token);
                    if(response.IsSuccessStatusCode) {
                        await response.Content.LoadIntoBufferAsync(262144,timeout.Token);
                        using var document=await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(timeout.Token),cancellationToken:timeout.Token);
                        health=document.RootElement.TryGetProperty("capabilities",out var capabilities)&&capabilities.EnumerateArray().Any(x=>x.GetString()=="tools")?"ready":"toolsUnavailable";
                    } else health=(int)response.StatusCode==404?"modelMissing":"unavailable";
                }
                catch(OperationCanceledException)when(!ct.IsCancellationRequested){health="unavailable";}
                catch(Exception ex)when(ex is HttpRequestException or JsonException or InvalidOperationException){health="unavailable";}
                checkedAt=DateTimeOffset.UtcNow;
            }
            return new(options.Enabled,options.Configured,options.Provider,options.Model,false,health,options.OllamaContextWindow);
        } finally {gate.Release();}
    }
}
