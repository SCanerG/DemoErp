using Demo.Api.AI;
using Demo.Api.AI.Knowledge;
using Demo.Api.Data;
using Demo.Api.Errors;
using Demo.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace Demo.Api.Controllers;

[ApiController,Route("api/knowledge"),Authorize(Policy=AccessPolicies.BusinessRead),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class KnowledgeController(KnowledgeOptions options,KnowledgeDocuments documents,KnowledgeAgent agent,
    IDocumentAuthorization access,IKnowledgeVectorStore vectors,AppDbContext db):ControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct){await access.Read(User,ct);return Ok(new{options.Enabled,options.EmbeddingModel,vectorAvailable=options.Enabled&&await vectors.Available(ct),options.MaxFileBytes,options.TopK});}
    [HttpGet("documents")]
    public async Task<ActionResult<List<DocumentView>>> List(CancellationToken ct)=>Ok(await documents.List(User,ct));
    [HttpGet("documents/{id:guid}")]
    public async Task<ActionResult<DocumentView>> Get(Guid id,CancellationToken ct)=>Ok(await documents.View(id,User,ct));
    [HttpGet("documents/{id:guid}/versions")]
    public async Task<ActionResult<List<VersionView>>> Versions(Guid id,CancellationToken ct)=>Ok(await documents.Versions(id,User,ct));
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("knowledge-ingestion")]
    [HttpPost("documents"),Authorize(Policy=AccessPolicies.BusinessWrite),RequestSizeLimit(11534336),RequestFormLimits(MultipartBodyLengthLimit=11534336)]
    public async Task<ActionResult<DocumentView>> Upload(IFormFile file,[FromForm]string scope="private",CancellationToken ct=default) {
        if(file.Length>options.MaxFileBytes)throw new BusinessException("knowledgeFileSize",413);
        await using var stream=file.OpenReadStream();var result=await documents.Upload(stream,file.FileName,scope,null,User,ct);return AcceptedAtAction(nameof(Get),new{id=result.Id},result);
    }
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("knowledge-ingestion")]
    [HttpPut("documents/{id:guid}"),Authorize(Policy=AccessPolicies.BusinessWrite),RequestSizeLimit(11534336),RequestFormLimits(MultipartBodyLengthLimit=11534336)]
    public async Task<ActionResult<DocumentView>> Update(Guid id,IFormFile file,CancellationToken ct) {
        var current=await documents.View(id,User,ct);if(file.Length>options.MaxFileBytes)throw new BusinessException("knowledgeFileSize",413);
        await using var stream=file.OpenReadStream();return Accepted(await documents.Upload(stream,file.FileName,current.Scope,id,User,ct));
    }
    [HttpPost("documents/{id:guid}/reindex"),Authorize(Policy=AccessPolicies.BusinessWrite)]
    public async Task<ActionResult<DocumentView>> Reindex(Guid id,CancellationToken ct)=>Accepted(await documents.Reindex(id,User,ct));
    [HttpPost("documents/{id:guid}/cancel"),Authorize(Policy=AccessPolicies.BusinessWrite)]
    public async Task<IActionResult> Cancel(Guid id,CancellationToken ct){await documents.Cancel(id,User,ct);return NoContent();}
    [HttpDelete("documents/{id:guid}"),Authorize(Policy=AccessPolicies.BusinessWrite)]
    public async Task<IActionResult> Delete(Guid id,CancellationToken ct){await documents.Delete(id,User,ct);return NoContent();}
    [HttpGet("documents/{id:guid}/chunks/{chunk:guid}")]
    public async Task<IActionResult> Preview(Guid id,Guid chunk,CancellationToken ct) {
        var doc=await access.Get(id,User,false,ct);
        var row=await db.Set<KnowledgeChunk>().AsNoTracking().SingleOrDefaultAsync(c=>c.Id==chunk&&c.DocumentId==id&&c.Version==doc.Version&&doc.Status=="indexed",ct)??throw new BusinessException("knowledgeNotFound",404);
        return Ok(new{row.Id,row.Text,row.Reference,row.Version,doc.FileName});
    }
    [HttpPost("chat"),RequestSizeLimit(12000)]
    public async Task<ActionResult<KnowledgeAnswer>> Chat(KnowledgeQuestion question,CancellationToken ct)=>Ok(await agent.Answer(question,User,ct));
}
