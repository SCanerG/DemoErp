using System.Globalization;
using Demo.Api.Data;
using Demo.Api.Errors;
using Microsoft.EntityFrameworkCore;
namespace Demo.Api.AI.Knowledge;

public interface IKnowledgeVectorStore {
    Task<bool> Available(CancellationToken ct);
    Task SaveVector(Guid chunk,float[] vector,CancellationToken ct);
}
public sealed class PgKnowledgeVectorStore(AppDbContext db):IKnowledgeVectorStore {
    public Task<bool> Available(CancellationToken ct)=>db.Database.SqlQueryRaw<bool>("SELECT EXISTS(SELECT 1 FROM pg_extension WHERE extname='vector') AND EXISTS(SELECT 1 FROM information_schema.columns WHERE table_name='KnowledgeChunks' AND column_name='Embedding') AS \"Value\"").SingleAsync(ct);
    public static string Vector(float[] vector)=>"["+string.Join(',',vector.Select(v=>v.ToString("R",CultureInfo.InvariantCulture)))+"]";
    public async Task SaveVector(Guid chunk,float[] vector,CancellationToken ct){await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"KnowledgeChunks\" SET \"Embedding\"=CAST({Vector(vector)} AS vector) WHERE \"Id\"={chunk}",ct);}
}
public sealed class KnowledgeIndexer(AppDbContext db,KnowledgeOptions options,IDocumentExtractor extractor,IKnowledgeChunker chunker,
    IKnowledgeEmbeddingProvider embeddings,IKnowledgeVectorStore vectors,ILogger<KnowledgeIndexer> logger)
{
    public async Task<bool> ProcessNext(CancellationToken ct) {
        if(!options.Enabled)return false;
        var stale=DateTimeOffset.UtcNow.AddMinutes(-6);
        await db.Set<KnowledgeJob>().Where(j=>j.State=="processing"&&j.StartedAt<stale&&j.Attempts<3).ExecuteUpdateAsync(s=>s.SetProperty(j=>j.State,"queued").SetProperty(j=>j.Lease,(string?)null),ct);
        await db.Set<KnowledgeJob>().Where(j=>j.State=="processing"&&j.StartedAt<stale&&j.Attempts>=3).ExecuteUpdateAsync(s=>s.SetProperty(j=>j.State,"failed").SetProperty(j=>j.Error,"knowledgeInterrupted"),ct);
        await db.Set<KnowledgeDocument>().Where(d=>d.Status=="processing"&&db.Set<KnowledgeJob>().Any(j=>j.DocumentId==d.Id&&j.Version==d.Version&&j.State=="failed"&&j.Error=="knowledgeInterrupted")
            &&!db.Set<KnowledgeJob>().Any(j=>j.DocumentId==d.Id&&j.Version==d.Version&&(j.State=="queued"||j.State=="processing")))
            .ExecuteUpdateAsync(s=>s.SetProperty(d=>d.Status,"failed"),ct);
        var id=await db.Set<KnowledgeJob>().Where(j=>j.State=="queued").OrderBy(j=>j.CreatedAt).Select(j=>(Guid?)j.Id).FirstOrDefaultAsync(ct);if(id is null)return false;
        var lease=Guid.NewGuid().ToString("N");
        if(await db.Set<KnowledgeJob>().Where(j=>j.Id==id&&j.State=="queued").ExecuteUpdateAsync(s=>s.SetProperty(j=>j.State,"processing").SetProperty(j=>j.Lease,lease).SetProperty(j=>j.StartedAt,DateTimeOffset.UtcNow).SetProperty(j=>j.Attempts,j=>j.Attempts+1),ct)!=1)return true;
        var job=await db.Set<KnowledgeJob>().AsNoTracking().SingleAsync(j=>j.Id==id,ct);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromMinutes(5));var token=timeout.Token;
        try {
            if(!await vectors.Available(token))throw new BusinessException("knowledgeVectorUnavailable",503);
            var doc=await db.Set<KnowledgeDocument>().AsNoTracking().SingleOrDefaultAsync(d=>d.Id==job.DocumentId&&d.Version==job.Version,token);if(doc is null)return true;
            var owner=await db.Users.AsNoTracking().SingleAsync(u=>u.Id==doc.OwnerId,token);
            if(!owner.IsActive||owner.Role==Domain.UserRole.Viewer||doc.Scope=="workspace"&&owner.Role!=Domain.UserRole.Admin)throw new BusinessException("aiForbidden",403);
            await db.Set<KnowledgeDocument>().Where(d=>d.Id==doc.Id&&d.Version==job.Version).ExecuteUpdateAsync(s=>s.SetProperty(d=>d.Status,"processing"),token);
            var version=await db.Set<KnowledgeVersion>().AsNoTracking().SingleAsync(v=>v.DocumentId==doc.Id&&v.Number==job.Version,token);
            var chunks=chunker.Split(extractor.Extract(version.Content,version.FileName,token));
            var pending=new List<(KnowledgeChunk Row,float[] Vector)>();EmbeddingBatch? identity=null;
            foreach(var batch in chunks.Chunk(4)) {
                token.ThrowIfCancellationRequested();
                if(!await db.Set<KnowledgeJob>().AnyAsync(j=>j.Id==id&&j.State=="processing"&&j.Lease==lease,token))return true;
                var embedded=await embeddings.Embed(batch.Select(c=>"search_document: "+c.Text).ToArray(),token);
                if(OllamaEmbeddings.Validate(embedded.Vectors,batch.Length)!=embedded.Dimensions||embedded.Model!=options.EmbeddingModel||embedded.Digest.Length is <1 or >128)throw new BusinessException("knowledgeEmbeddingInvalid",502);
                if(identity is not null&&(identity.Digest!=embedded.Digest||identity.Model!=embedded.Model||identity.Dimensions!=embedded.Dimensions))throw new BusinessException("knowledgeIndexMismatch",409);
                identity=embedded;
                for(var i=0;i<batch.Length;i++)pending.Add((new(){DocumentId=doc.Id,Version=job.Version,Position=batch[i].Position,Text=batch[i].Text,Reference=batch[i].Reference,Model=embedded.Model,ModelDigest=embedded.Digest,Dimensions=embedded.Dimensions},embedded.Vectors[i]));
                await db.Set<KnowledgeJob>().Where(j=>j.Id==id&&j.Lease==lease).ExecuteUpdateAsync(s=>s.SetProperty(j=>j.Progress,pending.Count*90/chunks.Count),token);
            }
            await using var tx=await db.Database.BeginTransactionAsync(token);
            var authorizedOwner=await db.Users.FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE \"Id\"={doc.OwnerId} FOR SHARE").AsNoTracking().SingleAsync(token);
            if(!authorizedOwner.IsActive||authorizedOwner.Role==Domain.UserRole.Viewer||doc.Scope=="workspace"&&authorizedOwner.Role!=Domain.UserRole.Admin)throw new BusinessException("aiForbidden",403);
            var current=await db.Set<KnowledgeDocument>().FromSqlInterpolated($"SELECT * FROM \"KnowledgeDocuments\" WHERE \"Id\"={doc.Id} FOR UPDATE").SingleOrDefaultAsync(token);
            if(current is null||current.Version!=job.Version||!await db.Set<KnowledgeJob>().AnyAsync(j=>j.Id==id&&j.State=="processing"&&j.Lease==lease,token))return true;
            await db.Set<KnowledgeChunk>().Where(c=>c.DocumentId==doc.Id).ExecuteDeleteAsync(token);
            db.AddRange(pending.Select(c=>c.Row));await db.SaveChangesAsync(token);
            foreach(var pair in pending)await vectors.SaveVector(pair.Row.Id,pair.Vector,token);
            current.Status="indexed";current.UpdatedAt=DateTimeOffset.UtcNow;await db.SaveChangesAsync(token);
            await db.Set<KnowledgeJob>().Where(j=>j.Id==id&&j.Lease==lease).ExecuteUpdateAsync(s=>s.SetProperty(j=>j.State,"indexed").SetProperty(j=>j.Progress,100).SetProperty(j=>j.Error,(string?)null).SetProperty(j=>j.EndedAt,DateTimeOffset.UtcNow),token);
            await tx.CommitAsync(token);logger.LogInformation("Knowledge indexed document {DocumentId} version {Version} chunks {Count} dimensions {Dimensions}",doc.Id,job.Version,pending.Count,identity?.Dimensions);
        } catch(Exception ex) {
            var category=ex is BusinessException b?b.Code:ex is OperationCanceledException?"knowledgeCancelled":"knowledgeProcessingFailed";
            logger.LogWarning("Knowledge indexing job {JobId} category {Category}",id,category);
            db.ChangeTracker.Clear();using var final=new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var state=category=="knowledgeOcrRequired"?"ocrRequired":"failed";
            if(await db.Set<KnowledgeJob>().Where(j=>j.Id==id&&j.State=="processing"&&j.Lease==lease).ExecuteUpdateAsync(s=>s.SetProperty(j=>j.State,state).SetProperty(j=>j.Error,category).SetProperty(j=>j.EndedAt,DateTimeOffset.UtcNow),final.Token)==1)
                await db.Set<KnowledgeDocument>().Where(d=>d.Id==job.DocumentId&&d.Version==job.Version).ExecuteUpdateAsync(s=>s.SetProperty(d=>d.Status,state),final.Token);
        }
        return true;
    }
}
public sealed class KnowledgeWorker(IServiceScopeFactory scopes,KnowledgeOptions options,ILogger<KnowledgeWorker> logger):BackgroundService {
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        if(!options.Enabled)return;
        while(!stoppingToken.IsCancellationRequested)try {
            await using var scope=scopes.CreateAsyncScope();var work=await scope.ServiceProvider.GetRequiredService<KnowledgeIndexer>().ProcessNext(stoppingToken);
            if(!work)await Task.Delay(1000,stoppingToken);
        }catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){return;}
        catch(Exception){logger.LogWarning("Knowledge worker temporarily unavailable");await Task.Delay(3000,stoppingToken);}
    }
}
