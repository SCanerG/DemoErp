using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Demo.Api.Errors;
namespace Demo.Api.AI.Knowledge;

public sealed class LocalModelGate
{
    public static LocalModelGate Shared { get; } = new();
    private readonly SemaphoreSlim semaphore;
    private readonly int capacity;
    private int pending;
    public int Concurrency { get; }
    public int QueueLimit { get; }
    public int Pending => Volatile.Read(ref pending);
    public LocalModelGate(int concurrency = 1, int queueLimit = 8)
    {
        if (concurrency is < 1 or > 2 || queueLimit is < 1 or > 32) throw new InvalidOperationException("Invalid local inference limits.");
        Concurrency = concurrency; QueueLimit = queueLimit; capacity = concurrency + queueLimit; semaphore = new(concurrency, concurrency);
    }
    public async Task<IDisposable> Enter(CancellationToken ct)
    {
        if (Interlocked.Increment(ref pending) > capacity) { Interlocked.Decrement(ref pending); throw new BusinessException("aiBusy", 503); }
        try { await semaphore.WaitAsync(ct); return new Lease(this); }
        catch { Interlocked.Decrement(ref pending); throw; }
    }
    private sealed class Lease(LocalModelGate owner) : IDisposable
    {
        private int disposed;
        public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) == 0) { owner.semaphore.Release(); Interlocked.Decrement(ref owner.pending); } }
    }
}
public interface IKnowledgeEmbeddingProvider {
    Task<EmbeddingBatch> Embed(IReadOnlyList<string> texts,CancellationToken ct);
}
public sealed class OllamaEmbeddings(KnowledgeOptions options,HttpClient http,ILogger<OllamaEmbeddings> logger,LocalModelGate? gate=null,Quality.QualityTelemetry? telemetry=null):IKnowledgeEmbeddingProvider,IEmbeddingClient
{
    public async Task<ReadOnlyMemory<float>> Embed(string text,CancellationToken ct)=>(await Embed(new[]{text},ct)).Vectors[0];
    public async Task<EmbeddingBatch> Embed(IReadOnlyList<string> texts,CancellationToken ct) {
        using var span=telemetry?.Begin("embedding","local-embedding");span?.Provider("Ollama",options.EmbeddingModel);span?.Context(texts.Sum(t=>t.Length));
        try {return await EmbedCore(texts,ct);}catch(Exception error){span?.Fail(error);throw;}
    }
    private async Task<EmbeddingBatch> EmbedCore(IReadOnlyList<string> texts,CancellationToken ct) {
        options.Check();if(texts.Count is <1 or >8||texts.Any(t=>t.Length is <1 or >2500))throw new BusinessException("knowledgeEmbeddingInput");
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));var token=deadline.Token;
        var watch=Stopwatch.StartNew();using var lease=await (gate??LocalModelGate.Shared).Enter(token);
        try {
            var tags=await Json(await http.GetAsync("http://127.0.0.1:11434/api/tags",HttpCompletionOption.ResponseHeadersRead,token),token);
            using(tags) {
                var models=tags.RootElement.GetProperty("models").EnumerateArray().Where(m=>m.GetProperty("name").GetString()==options.EmbeddingModel||m.GetProperty("name").GetString()==options.EmbeddingModel+":latest").ToList();
                if(models.Count!=1)throw new BusinessException("knowledgeEmbeddingModelMissing",503);
                var digest=models[0].GetProperty("digest").GetString()!;
                for(var attempt=0;;attempt++)try {
                    using var request=new HttpRequestMessage(HttpMethod.Post,"http://127.0.0.1:11434/api/embed"){Content=JsonContent.Create(new{model=options.EmbeddingModel,input=texts,truncate=false,keep_alive="5m",options=new{num_gpu=0}})};
                    using var data=await Json(await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token),token);
                    var actualModel=data.RootElement.GetProperty("model").GetString();
                    if(actualModel!=options.EmbeddingModel&&actualModel!=options.EmbeddingModel+":latest")throw new BusinessException("knowledgeIndexMismatch",409);
                    var vectors=data.RootElement.GetProperty("embeddings").EnumerateArray().Select(a=>a.EnumerateArray().Select(n=>n.GetSingle()).ToArray()).ToList();
                    var dims=Validate(vectors,texts.Count);
                    using var current=await Json(await http.GetAsync("http://127.0.0.1:11434/api/tags",HttpCompletionOption.ResponseHeadersRead,token),token);
                    if(!current.RootElement.GetProperty("models").EnumerateArray().Any(m=>(m.GetProperty("name").GetString()==options.EmbeddingModel||m.GetProperty("name").GetString()==options.EmbeddingModel+":latest")&&m.GetProperty("digest").GetString()==digest))throw new BusinessException("knowledgeIndexMismatch",409);
                    logger.LogInformation("Knowledge embedding model {Model} dimensions {Dimensions} batch {Batch} latencyMs {Latency}",options.EmbeddingModel,dims,texts.Count,watch.ElapsedMilliseconds);
                    return new(options.EmbeddingModel,digest,dims,vectors,watch.ElapsedMilliseconds);
                } catch(BusinessException ex)when(attempt==0&&ex.Code=="knowledgeEmbeddingUnavailable"){await Task.Delay(200,token);}
            }
        }catch(OperationCanceledException)when(!ct.IsCancellationRequested){throw new BusinessException("knowledgeTimeout",504);}
        catch(HttpRequestException){throw new BusinessException("knowledgeEmbeddingUnavailable",503);}
        catch(JsonException){throw new BusinessException("knowledgeEmbeddingInvalid",502);}
        catch(Exception ex)when(ex is KeyNotFoundException or InvalidOperationException or FormatException or OverflowException){throw new BusinessException("knowledgeEmbeddingInvalid",502);}
    }
    public static int Validate(List<float[]> vectors,int expected) {
        var dim=vectors.FirstOrDefault()?.Length??0;
        if(vectors.Count!=expected||dim is <1 or >4096||vectors.Any(v=>v.Length!=dim||v.Any(n=>!float.IsFinite(n))||v.All(n=>n==0)))throw new BusinessException("knowledgeEmbeddingInvalid",502);
        return dim;
    }
    private static async Task<JsonDocument> Json(HttpResponseMessage response,CancellationToken ct) {
        using(response){if(!response.IsSuccessStatusCode)throw new BusinessException(response.StatusCode==System.Net.HttpStatusCode.NotFound?"knowledgeEmbeddingModelMissing":"knowledgeEmbeddingUnavailable",503);
            await using var stream=await response.Content.ReadAsStreamAsync(ct);using var buffer=new MemoryStream();var bytes=new byte[8192];int read;
            while((read=await stream.ReadAsync(bytes,ct))>0){if(buffer.Length+read>1024*1024)throw new BusinessException("knowledgeEmbeddingInvalid",502);buffer.Write(bytes,0,read);}
            return JsonDocument.Parse(buffer.ToArray(),new JsonDocumentOptions{MaxDepth=8});}
    }
}
