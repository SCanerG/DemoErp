using System.Net;
using System.Text;
using Demo.Api.AI.Knowledge;
using Demo.Api.Errors;
using Microsoft.Extensions.Logging.Abstractions;
namespace Demo.Api.Tests;
public sealed class KnowledgeEmbeddingTests
{
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> work):HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>work(request,ct);
    }
    private static HttpResponseMessage Json(string json,HttpStatusCode code=HttpStatusCode.OK)=>new(code){Content=new StringContent(json,Encoding.UTF8,"application/json")};
    [Fact]
    public async Task Embedding_transport_checks_model_identity_batch_and_actual_dimensions() {
        var count=0;
        using var http=new HttpClient(new Handler(async(request,ct)=> {
            count++;Assert.Equal("127.0.0.1",request.RequestUri!.Host);Assert.Equal(11434,request.RequestUri.Port);
            if(request.RequestUri.AbsolutePath.EndsWith("tags"))return Json("{\"models\":[{\"name\":\"nomic-embed-text:latest\",\"digest\":\"actual-digest\"}]}");
            var body=await request.Content!.ReadAsStringAsync(ct);Assert.Contains("\"truncate\":false",body);Assert.Contains("\"num_gpu\":0",body);
            return Json("{\"model\":\"nomic-embed-text\",\"embeddings\":[[1,2,3],[4,5,6]]}");
        }));
        var result=await new OllamaEmbeddings(new(){Enabled=true},http,NullLogger<OllamaEmbeddings>.Instance).Embed(new[]{"a","b"},TestContext.Current.CancellationToken);
        Assert.Equal(3,result.Dimensions);Assert.Equal("actual-digest",result.Digest);Assert.Equal(3,count);
    }
    [Fact]
    public async Task Missing_model_is_a_safe_error_without_downloading_or_embedding() {
        var count=0;using var http=new HttpClient(new Handler((_,_)=>{count++;return Task.FromResult(Json("{\"models\":[]}"));}));
        var error=await Assert.ThrowsAsync<BusinessException>(()=>new OllamaEmbeddings(new(){Enabled=true},http,NullLogger<OllamaEmbeddings>.Instance).Embed(new[]{"test"},TestContext.Current.CancellationToken));
        Assert.Equal("knowledgeEmbeddingModelMissing",error.Code);Assert.Equal(1,count);
    }
    [Fact]
    public async Task Transient_embedding_failure_is_retried_at_most_once() {
        var count=0;using var http=new HttpClient(new Handler((request,_)=>{if(request.RequestUri!.AbsolutePath.EndsWith("tags"))return Task.FromResult(Json("{\"models\":[{\"name\":\"nomic-embed-text\",\"digest\":\"d\"}]}"));count++;return Task.FromResult(Json("{}",HttpStatusCode.ServiceUnavailable));}));
        await Assert.ThrowsAsync<BusinessException>(()=>new OllamaEmbeddings(new(){Enabled=true},http,NullLogger<OllamaEmbeddings>.Instance).Embed(new[]{"test"},TestContext.Current.CancellationToken));Assert.Equal(2,count);
    }
    [Fact]
    public async Task Embedding_cancellation_stops_the_active_HTTP_transport() {
        var entered=false;
        using var stop=new CancellationTokenSource();using var http=new HttpClient(new Handler(async(_,ct)=>{entered=true;stop.Cancel();await Task.Delay(10000,ct);return Json("{}");}));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new OllamaEmbeddings(new(){Enabled=true},http,NullLogger<OllamaEmbeddings>.Instance).Embed(new[]{"test"},stop.Token));
        Assert.True(entered);
    }
}
