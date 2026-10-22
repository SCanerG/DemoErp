using Demo.Api.AI.Agents;
using Demo.Api.AI.Workflows;
using Demo.Api.Data;
using Demo.Api.Errors;
using Demo.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Demo.Api.Controllers;

[ApiController,Route("api/workflows"),Authorize(Policy=AccessPolicies.BusinessRead),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None),RequestSizeLimit(65000)]
public sealed class WorkflowsController(AppDbContext db,WorkflowService workflows,WorkflowOptions options,WorkflowDrafting drafting):ControllerBase
{
    [HttpGet("catalog")]
    public async Task<IActionResult> Catalog(CancellationToken ct){await workflows.Check(User,false,ct);return Ok(new{options.Enabled,nodes=WorkflowRegistry.All,templates=new[]{"blank","criticalInventory","overduePurchase","weeklySales","inventoryPolicy"}.Select(key=>new{key,graph=WorkflowRegistry.Template(key)}),maxNodes=25,maxConcurrentExecutions=1,scope="owner-private",timeZone="UTC"});}
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct){await workflows.Check(User,false,ct);var owner=WorkflowService.Owner(User);return Ok(await db.Set<WorkflowDefinition>().AsNoTracking().Where(w=>w.OwnerId==owner).OrderByDescending(w=>w.UpdatedAt).Take(100).Select(w=>new{w.Id,w.Name,w.Description,w.Revision,w.PublishedVersion,w.Enabled,w.Archived,w.UpdatedAt,w.NextRunAt}).ToListAsync(ct));}
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Read(Guid id,CancellationToken ct){var row=await workflows.Own(id,User,false,ct);return Ok(new{workflow=row,graph=WorkflowModel.Read<WorkflowGraph>(row.Draft),versions=await db.Set<WorkflowVersion>().AsNoTracking().Where(v=>v.WorkflowId==id).OrderByDescending(v=>v.PublishedAt).Take(50).Select(v=>new{v.Id,v.Number,v.PublishedAt}).ToListAsync(ct)});}
    [HttpPost,Authorize(Policy=AccessPolicies.BusinessWrite)]
    public async Task<IActionResult> Create(WorkflowSave input,CancellationToken ct){var row=await workflows.Save(null,input,User,ct);return Created($"/api/workflows/{row.Id}",row);}
    [HttpGet("{id:guid}/versions/{number:int}")]
    public async Task<IActionResult> Version(Guid id,int number,CancellationToken ct){await workflows.Own(id,User,false,ct);var version=await db.Set<WorkflowVersion>().AsNoTracking().SingleOrDefaultAsync(v=>v.WorkflowId==id&&v.Number==number,ct)??throw WorkflowService.Missing();return Ok(new{version.Number,version.PublishedAt,graph=WorkflowModel.Read<WorkflowGraph>(version.Graph)});}
    [HttpPut("{id:guid}"),Authorize(Policy=AccessPolicies.BusinessWrite)]
    public async Task<IActionResult> Save(Guid id,WorkflowSave input,CancellationToken ct)=>Ok(await workflows.Save(id,input,User,ct));
    [HttpPost("{id:guid}/validate"),Authorize(Policy=AccessPolicies.BusinessWrite)]
    public async Task<IActionResult> Validate(Guid id,CancellationToken ct){var row=await workflows.Own(id,User,false,ct);var nodes=WorkflowRegistry.Validate(WorkflowModel.Read<WorkflowGraph>(row.Draft));return Ok(new{valid=true,nodes=nodes.Count,requiresHumanApproval=nodes.Any(n=>n.Type=="action.propose"),scope="owner-private"});}
    [HttpPost("{id:guid}/publish"),Authorize(Policy=AccessPolicies.BusinessWrite)]
    public async Task<IActionResult> Publish(Guid id,WorkflowCommand input,CancellationToken ct)=>Ok(await workflows.Publish(id,input,User,ct));
    [HttpPost("{id:guid}/activation"),Authorize(Policy=AccessPolicies.BusinessWrite)]
    public async Task<IActionResult> Activate(Guid id,WorkflowCommand input,CancellationToken ct)=>Ok(await workflows.Change(id,input,User,false,ct));
    [HttpPost("{id:guid}/archive"),Authorize(Policy=AccessPolicies.BusinessWrite)]
    public async Task<IActionResult> Archive(Guid id,WorkflowCommand input,CancellationToken ct)=>Ok(await workflows.Change(id,input,User,true,ct));
    [HttpPost("{id:guid}/runs"),Authorize(Policy=AccessPolicies.BusinessWrite)]
    public async Task<IActionResult> Run(Guid id,WorkflowRunRequest input,CancellationToken ct){var run=await workflows.Enqueue(id,input,User,ct);return Accepted(new{run.Id,run.State,run.Mode,run.VersionId});}
    [HttpGet("{id:guid}/runs")]
    public async Task<IActionResult> Runs(Guid id,CancellationToken ct){await workflows.Own(id,User,false,ct);return Ok(await db.Set<WorkflowRun>().AsNoTracking().Where(r=>r.WorkflowId==id).OrderByDescending(r=>r.CreatedAt).Take(100).ToListAsync(ct));}
    [HttpGet("runs/{id:guid}")]
    public async Task<IActionResult> Run(Guid id,CancellationToken ct){var run=await workflows.OwnRun(id,User,false,ct);var version=await db.Set<WorkflowVersion>().AsNoTracking().SingleAsync(v=>v.Id==run.VersionId,ct);var nodes=await db.Set<WorkflowNodeRun>().AsNoTracking().Where(n=>n.RunId==id).OrderBy(n=>n.StartedAt).ToListAsync(ct);return Ok(new{run,version=version.Number,nodes=nodes.Select(n=>new{n.NodeId,n.Type,n.State,n.Error,n.Attempts,n.DurationMs,n.StartedAt,n.EndedAt,output=WorkflowModel.Read<WorkflowValue>(n.Output)})});}
    [HttpPost("runs/{id:guid}/cancel"),Authorize(Policy=AccessPolicies.BusinessWrite)]
    public async Task<IActionResult> Cancel(Guid id,CancellationToken ct){await workflows.Cancel(id,User,ct);return NoContent();}
    [HttpPost("runs/{id:guid}/approve"),Authorize(Policy=AccessPolicies.OrderManage)]
    public async Task<IActionResult> Approve(Guid id,ProposalApproval input,CancellationToken ct)=>Ok(await workflows.Approve(id,input,User,ct));
    [HttpGet("notifications")]
    public async Task<IActionResult> Notifications(CancellationToken ct){await workflows.Check(User,false,ct);var owner=WorkflowService.Owner(User);return Ok(await db.Set<WorkflowNotification>().AsNoTracking().Where(n=>n.OwnerId==owner).OrderByDescending(n=>n.CreatedAt).Take(100).ToListAsync(ct));}
    [HttpPost("notifications/{id:guid}/read")]
    public async Task<IActionResult> ReadNotification(Guid id,CancellationToken ct){await workflows.Check(User,false,ct);var owner=WorkflowService.Owner(User);if(await db.Set<WorkflowNotification>().Where(n=>n.Id==id&&n.OwnerId==owner).ExecuteUpdateAsync(s=>s.SetProperty(n=>n.Read,true),ct)!=1)throw WorkflowService.Missing();return NoContent();}
    [HttpPost("ai-draft"),Authorize(Policy=AccessPolicies.BusinessWrite),EnableRateLimiting("quality-evaluation"),RequestSizeLimit(3000)]
    public async Task<IActionResult> AiDraft(WorkflowDraftRequest input,CancellationToken ct)=>Ok(await drafting.Draft(input,User,ct));
}
