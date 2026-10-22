using System.Diagnostics;
using System.Security.Claims;
using Demo.Api.Data;
using Demo.Api.Errors;
using Microsoft.EntityFrameworkCore;
namespace Demo.Api.AI.Knowledge;

public interface IKnowledgeRetriever {
    Task<(List<KnowledgePassage> Passages,EmbeddingBatch Identity,long LatencyMs)> Search(string question,Guid? document,ClaimsPrincipal user,CancellationToken ct);
    Task Recheck(IReadOnlyList<KnowledgePassage> passages,ClaimsPrincipal user,CancellationToken ct);
}
public sealed class KnowledgeRankRow {public Guid Id{get;set;} public double Score{get;set;} }
public sealed class KnowledgeRetriever(AppDbContext db,KnowledgeOptions options,IDocumentAuthorization access,
    IKnowledgeEmbeddingProvider embeddings,IKnowledgeVectorStore vectors,Quality.QualityTelemetry? telemetry=null):IKnowledgeRetriever,IRagRetriever
{
    public async Task<IReadOnlyList<RagPassage>> RetrieveAuthorized(RagQuery query,ClaimsPrincipal user,CancellationToken ct)=>
        (await Search(query.Text,null,user,ct)).Passages.Take(Math.Clamp(query.Limit,1,options.TopK)).Select(p=>new RagPassage(p.DocumentId.ToString(),p.Text,p.Score)).ToList();
    public async Task<(List<KnowledgePassage> Passages,EmbeddingBatch Identity,long LatencyMs)> Search(string question,Guid? document,ClaimsPrincipal user,CancellationToken ct) {
        using var span=telemetry?.Begin("retrieval","hybrid");
        try {var result=await SearchCore(question,document,user,ct);span?.Retrieved(result.Passages.Count);span?.Context(result.Passages.Sum(p=>p.Text.Length));return result;}
        catch(Exception error){span?.Fail(error);throw;}
    }
    private async Task<(List<KnowledgePassage> Passages,EmbeddingBatch Identity,long LatencyMs)> SearchCore(string question,Guid? document,ClaimsPrincipal user,CancellationToken ct) {
        options.Check();var owner=await access.Read(user,ct);if(question.Length is <2 or >2000)throw new BusinessException("aiInvalidRequest");
        if(document is Guid id)await access.Get(id,user,false,ct);
        if(!await vectors.Available(ct))throw new BusinessException("knowledgeVectorUnavailable",503);
        var watch=Stopwatch.StartNew();var identity=await embeddings.Embed(new[]{"search_query: "+question},ct);
        if(OllamaEmbeddings.Validate(identity.Vectors,1)!=identity.Dimensions||identity.Model!=options.EmbeddingModel)throw new BusinessException("knowledgeEmbeddingInvalid",502);
        var vector=PgKnowledgeVectorStore.Vector(identity.Vectors[0]);var digest=identity.Digest;var dimensions=identity.Dimensions;var model=identity.Model;
        // MATERIALIZED authorization set is shared by BOTH ranking branches before similarity or keyword ranking.
        var ranked=await db.Database.SqlQuery<KnowledgeRankRow>($"""
            WITH allowed AS MATERIALIZED (
              SELECT c."Id",c."Text",c."Embedding" FROM "KnowledgeChunks" c JOIN "KnowledgeDocuments" d ON d."Id"=c."DocumentId"
              WHERE (d."OwnerId"={owner} OR d."Scope"='workspace') AND d."Status"='indexed' AND c."Version"=d."Version"
                AND ({document}::uuid IS NULL OR d."Id"={document}) AND c."Model"={model} AND c."ModelDigest"={digest} AND c."Dimensions"={dimensions} AND c."Embedding" IS NOT NULL
            ), v AS (
              SELECT "Id",row_number() OVER(ORDER BY "Embedding" <=> CAST({vector} AS vector),"Id") AS rank
              FROM allowed WHERE 1-("Embedding" <=> CAST({vector} AS vector)) >= 0.5 ORDER BY "Embedding" <=> CAST({vector} AS vector),"Id" LIMIT 20
            ), k AS (
              SELECT "Id",row_number() OVER(ORDER BY ts_rank_cd(to_tsvector('simple',"Text"),plainto_tsquery('simple',{question})) DESC,"Id") AS rank
              FROM allowed WHERE to_tsvector('simple',"Text") @@ plainto_tsquery('simple',{question}) LIMIT 20
            ) SELECT "Id",sum(1.0/(60+rank))::double precision AS "Score" FROM (SELECT * FROM v UNION ALL SELECT * FROM k) x GROUP BY "Id" ORDER BY "Score" DESC,"Id" LIMIT {options.TopK*2}
            """).ToListAsync(ct);
        var result=new List<KnowledgePassage>();var length=0;var seen=new HashSet<string>();
        foreach(var rank in ranked) {
            var row=await (from c in db.Set<KnowledgeChunk>().AsNoTracking() join d in access.Visible(owner).AsNoTracking() on c.DocumentId equals d.Id
                where c.Id==rank.Id&&d.Status=="indexed"&&c.Version==d.Version select new{Chunk=c,d.FileName}).SingleOrDefaultAsync(ct);
            if(row is null||!seen.Add(row.Chunk.Text)||length+row.Chunk.Text.Length>options.ContextCharacters)continue;
            result.Add(new(row.Chunk.Id,row.Chunk.DocumentId,row.Chunk.Version,row.FileName,row.Chunk.Reference,row.Chunk.Text,rank.Score));length+=row.Chunk.Text.Length;
            if(result.Count>=options.TopK)break;
        }
        await Recheck(result,user,ct);return(result,identity,watch.ElapsedMilliseconds);
    }
    public async Task Recheck(IReadOnlyList<KnowledgePassage> passages,ClaimsPrincipal user,CancellationToken ct) {
        var owner=await access.Read(user,ct);
        foreach(var p in passages)if(!await (from c in db.Set<KnowledgeChunk>() join d in access.Visible(owner) on c.DocumentId equals d.Id
            where c.Id==p.ChunkId&&c.DocumentId==p.DocumentId&&c.Version==p.Version&&d.Version==p.Version&&d.Status=="indexed"&&c.Text==p.Text select c.Id).AnyAsync(ct))throw new BusinessException("knowledgeSourcesChanged",409);
    }
}
public interface IKnowledgeContextBuilder { string Build(IReadOnlyList<KnowledgePassage> passages); }
public sealed class KnowledgeContextBuilder:IKnowledgeContextBuilder
{
    public string Build(IReadOnlyList<KnowledgePassage> passages)=>System.Text.Json.JsonSerializer.Serialize(passages.Select((p,i)=>new{source=i+1,text=p.Text}),AiAssistantService.Json);
}
