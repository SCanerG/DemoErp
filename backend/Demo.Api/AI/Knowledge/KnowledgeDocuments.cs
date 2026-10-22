using System.Security.Claims;
using System.Security.Cryptography;
using Demo.Api.Data;
using Demo.Api.Domain;
using Demo.Api.Errors;
using Demo.Api.Services;
using Microsoft.EntityFrameworkCore;
namespace Demo.Api.AI.Knowledge;

public interface IDocumentAuthorization {
    Task<Guid> Read(ClaimsPrincipal user,CancellationToken ct);
    IQueryable<KnowledgeDocument> Visible(Guid user);
    Task<KnowledgeDocument> Get(Guid id,ClaimsPrincipal user,bool write,CancellationToken ct);
}
public sealed class DocumentAuthorization(AppDbContext db,AiToolExecutor access):IDocumentAuthorization {
    public async Task<Guid> Read(ClaimsPrincipal user,CancellationToken ct){await access.Recheck(user,AccessPolicies.BusinessRead,ct);return Guid.Parse(user.FindFirstValue("sub")!);}
    public IQueryable<KnowledgeDocument> Visible(Guid user)=>db.Set<KnowledgeDocument>().Where(d=>d.OwnerId==user||d.Scope=="workspace");
    public async Task<KnowledgeDocument> Get(Guid id,ClaimsPrincipal user,bool write,CancellationToken ct) {
        var owner=await Read(user,ct);var d=await Visible(owner).AsNoTracking().SingleOrDefaultAsync(d=>d.Id==id,ct)??throw new BusinessException("knowledgeNotFound",404);
        if(write){await access.Recheck(user,AccessPolicies.BusinessWrite,ct);if(d.OwnerId!=owner||d.Scope=="workspace"&&user.FindFirstValue("role")!="Admin")throw new BusinessException("aiForbidden",403);}
        return d;
    }
}
public interface IKnowledgeIngestion {
    Task<DocumentView> Upload(Stream input,string filename,string scope,Guid? id,ClaimsPrincipal user,CancellationToken ct);
}
public sealed class KnowledgeDocuments(AppDbContext db,KnowledgeOptions options,IDocumentAuthorization access,AiToolExecutor authorization,IDocumentExtractor extractor):IKnowledgeIngestion
{
    public async Task<DocumentView> Upload(Stream input,string filename,string scope,Guid? id,ClaimsPrincipal user,CancellationToken ct) {
        options.Check();await authorization.Recheck(user,AccessPolicies.BusinessWrite,ct);var owner=await access.Read(user,ct);
        if(scope is not("private" or "workspace")||scope=="workspace"&&user.FindFirstValue("role")!="Admin")throw new BusinessException("aiForbidden",403);
        filename=Path.GetFileName(filename.Replace('\\','/'));
        if(filename.Length is <1 or >160||filename.Any(char.IsControl)||Path.GetExtension(filename).ToLowerInvariant() is not(".txt" or ".md" or ".pdf" or ".docx"))throw new BusinessException("knowledgeUnsupportedFormat");
        using var memory=new MemoryStream();var bytes=new byte[8192];int count;
        while((count=await input.ReadAsync(bytes,ct))>0){if(memory.Length+count>options.MaxFileBytes)throw new BusinessException("knowledgeFileSize",413);memory.Write(bytes,0,count);}
        var data=memory.ToArray();var ocr=false;
        try{extractor.Extract(data,filename,ct);}catch(BusinessException ex)when(ex.Code=="knowledgeOcrRequired"){ocr=true;}
        // Bounded retention per owner, including prior versions. Originals remain in private DB storage, never wwwroot.
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await db.Users.FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE \"Id\"={owner} FOR UPDATE").AsNoTracking().SingleAsync(ct);
        await authorization.Recheck(user,AccessPolicies.BusinessWrite,ct);
        var retained=await (from v in db.Set<KnowledgeVersion>() join d in db.Set<KnowledgeDocument>() on v.DocumentId equals d.Id where d.OwnerId==owner select (long)v.Content.Length).SumAsync(ct);
        if(retained+data.Length>50L*1024*1024)throw new BusinessException("knowledgeDocumentLimit");
        KnowledgeDocument doc;
        if(id is Guid existing){await access.Get(existing,user,true,ct);doc=await db.Set<KnowledgeDocument>().FromSqlInterpolated($"SELECT * FROM \"KnowledgeDocuments\" WHERE \"Id\"={existing} FOR UPDATE").SingleAsync(ct);if(doc.Scope!=scope)throw new BusinessException("knowledgeScopeImmutable");if(doc.Version>=20)throw new BusinessException("knowledgeDocumentLimit");doc.Version++;}
        else {if(await db.Set<KnowledgeDocument>().CountAsync(d=>d.OwnerId==owner,ct)>=100)throw new BusinessException("knowledgeDocumentLimit");doc=new(){OwnerId=owner,Scope=scope};db.Add(doc);}
        doc.FileName=filename;doc.UpdatedAt=DateTimeOffset.UtcNow;doc.Status=ocr?"ocrRequired":"queued";
        await Invalidate(doc.Id,ct);
        db.Add(new KnowledgeVersion{DocumentId=doc.Id,Number=doc.Version,Content=data,Hash=Convert.ToHexString(SHA256.HashData(data)),FileName=filename});
        db.Add(new KnowledgeJob{DocumentId=doc.Id,Version=doc.Version,State=ocr?"ocrRequired":"queued",Error=ocr?"knowledgeOcrRequired":null});await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return await View(doc.Id,user,ct);
    }
    public async Task<DocumentView> Reindex(Guid id,ClaimsPrincipal user,CancellationToken ct) {
        options.Check();await access.Get(id,user,true,ct);await using var tx=await db.Database.BeginTransactionAsync(ct);
        var d=await db.Set<KnowledgeDocument>().FromSqlInterpolated($"SELECT * FROM \"KnowledgeDocuments\" WHERE \"Id\"={id} FOR UPDATE").SingleAsync(ct);
        await Invalidate(id,ct);d.Status="queued";d.UpdatedAt=DateTimeOffset.UtcNow;db.Add(new KnowledgeJob{DocumentId=id,Version=d.Version});await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return await View(id,user,ct);
    }
    private async Task Invalidate(Guid id,CancellationToken ct) {
        await db.Set<KnowledgeChunk>().Where(c=>c.DocumentId==id).ExecuteDeleteAsync(ct);
        await db.Set<KnowledgeJob>().Where(j=>j.DocumentId==id&&(j.State=="queued"||j.State=="processing")).ExecuteUpdateAsync(s=>s.SetProperty(j=>j.State,"cancelled").SetProperty(j=>j.EndedAt,DateTimeOffset.UtcNow),ct);
    }
    public async Task Delete(Guid id,ClaimsPrincipal user,CancellationToken ct) {
        options.Check();var d=await access.Get(id,user,true,ct);db.Remove(d);await db.SaveChangesAsync(ct);
    }
    public async Task<DocumentView> View(Guid id,ClaimsPrincipal user,CancellationToken ct) {
        var d=await access.Get(id,user,false,ct);var job=await db.Set<KnowledgeJob>().AsNoTracking().Where(j=>j.DocumentId==id).OrderByDescending(j=>j.CreatedAt).FirstOrDefaultAsync(ct);
        var index=await db.Set<KnowledgeChunk>().AsNoTracking().Where(c=>c.DocumentId==id&&c.Version==d.Version).Select(c=>new{c.Model,c.Dimensions}).FirstOrDefaultAsync(ct);
        var canManage=d.OwnerId.ToString()==user.FindFirstValue("sub")&&(user.FindFirstValue("role") is "Admin" or "Manager")&&(d.Scope!="workspace"||user.FindFirstValue("role")=="Admin");
        return new(d.Id,d.FileName,d.Scope,d.Version,d.Status,d.UpdatedAt,job?.Progress??0,job?.Error,await db.Set<KnowledgeChunk>().CountAsync(c=>c.DocumentId==id&&c.Version==d.Version,ct),canManage,index?.Model,index?.Dimensions);
    }
    public async Task<List<DocumentView>> List(ClaimsPrincipal user,CancellationToken ct) {
        options.Check();var owner=await access.Read(user,ct);var admin=user.FindFirstValue("role")=="Admin";var writer=admin||user.FindFirstValue("role")=="Manager";
        return await access.Visible(owner).AsNoTracking().OrderByDescending(d=>d.UpdatedAt).Take(100).Select(d=>new DocumentView(d.Id,d.FileName,d.Scope,d.Version,d.Status,d.UpdatedAt,
            db.Set<KnowledgeJob>().Where(j=>j.DocumentId==d.Id).OrderByDescending(j=>j.CreatedAt).Select(j=>j.Progress).FirstOrDefault(),
            db.Set<KnowledgeJob>().Where(j=>j.DocumentId==d.Id).OrderByDescending(j=>j.CreatedAt).Select(j=>j.Error).FirstOrDefault(),
            db.Set<KnowledgeChunk>().Count(c=>c.DocumentId==d.Id&&c.Version==d.Version),d.OwnerId==owner&&writer&&(d.Scope!="workspace"||admin),
            db.Set<KnowledgeChunk>().Where(c=>c.DocumentId==d.Id&&c.Version==d.Version).Select(c=>c.Model).FirstOrDefault(),
            db.Set<KnowledgeChunk>().Where(c=>c.DocumentId==d.Id&&c.Version==d.Version).Select(c=>(int?)c.Dimensions).FirstOrDefault())).ToListAsync(ct);
    }
    public async Task<List<VersionView>> Versions(Guid id,ClaimsPrincipal user,CancellationToken ct) {
        await access.Get(id,user,false,ct);return await db.Set<KnowledgeVersion>().AsNoTracking().Where(v=>v.DocumentId==id).OrderByDescending(v=>v.Number).Select(v=>new VersionView(v.Number,v.FileName,v.Hash,v.CreatedAt)).Take(100).ToListAsync(ct);
    }
    public async Task Cancel(Guid id,ClaimsPrincipal user,CancellationToken ct) {
        await access.Get(id,user,true,ct);await using var tx=await db.Database.BeginTransactionAsync(ct);
        var d=await db.Set<KnowledgeDocument>().FromSqlInterpolated($"SELECT * FROM \"KnowledgeDocuments\" WHERE \"Id\"={id} FOR UPDATE").SingleAsync(ct);
        await Invalidate(id,ct);d.Status="cancelled";await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
}
