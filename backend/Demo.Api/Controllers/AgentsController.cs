using Demo.Api.AI.Agents;
using Demo.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Demo.Api.Controllers;
[ApiController,Route("api/ai/agents"),Authorize(Policy=AccessPolicies.BusinessRead),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class AgentsController(AgentOrchestrator orchestrator,AgentRunStore runs,AgentHealth health,WorkflowProposals proposals,Demo.Api.AI.AiAssistantOptions options):ControllerBase
{
    [HttpGet("status")]
    public async Task<ActionResult<AgentProviderStatus>> Status(CancellationToken ct)=>Ok(await health.Read(ct));
    [HttpGet("runs")]
    public async Task<ActionResult<List<AgentRunSummary>>> List(CancellationToken ct)=>Ok(await runs.List(User,ct));
    [HttpGet("runs/{id:guid}")]
    public async Task<ActionResult<AgentRunStatus>> Run(Guid id,CancellationToken ct)=>Ok(await runs.Read(id,User,ct));
    [HttpPost("runs"),RequestSizeLimit(1000)]
    public async Task<IActionResult> Start(AgentStartRequest request,CancellationToken ct) {
        var id=await orchestrator.Start(request,User,HttpContext.TraceIdentifier,ct);return CreatedAtAction(nameof(Run),new{id},new{id});
    }
    [HttpPost("runs/{id:guid}/execute"),RequestSizeLimit(12000)]
    public async Task<ActionResult<AgentResult>> Execute(Guid id,AgentInput input,CancellationToken ct)=>Ok(await orchestrator.Execute(id,input,User,ct));
    [HttpGet("runs/{id:guid}/proposals")]
    public async Task<ActionResult<List<ProposalView>>> Proposals(Guid id,CancellationToken ct)=>Ok(await proposals.List(id,User,ct));
    [HttpPost("proposals/{id:guid}/approve"),Authorize(Policy=AccessPolicies.OrderManage),RequestSizeLimit(1000)]
    public async Task<ActionResult<ProposalView>> Approve(Guid id,ProposalApproval input,CancellationToken ct)=>Ok(await proposals.Approve(id,input,User,ct));
    [HttpPost("proposals/{id:guid}/cancel")]
    public async Task<ActionResult<ProposalView>> CancelProposal(Guid id,CancellationToken ct)=>Ok(await proposals.Cancel(id,User,ct));
    [HttpPost("runs/{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id,CancellationToken ct){await runs.Cancel(id,User,ct);return NoContent();}
    [HttpPost("runs/{id:guid}/recover")]
    public async Task<IActionResult> Recover(Guid id,CancellationToken ct){await runs.Recover(id,User,options.TimeoutSeconds,ct);return NoContent();}
    [HttpPost("runs/{id:guid}/retry")]
    public async Task<IActionResult> Retry(Guid id,CancellationToken ct){
        var previous=await runs.Read(id,User,ct);
        if(previous.Execution.Outcome is "success" or "running" or "queued")return Conflict(new{code="aiWorkflowNotRecoverable"});
        var ancestor=previous;var depth=0;
        while(ancestor.RetryOf is Guid parent){if(++depth>=2)return Conflict(new{code="aiAgentLimit"});ancestor=await runs.Read(parent,User,ct);}
        var next=await orchestrator.Start(new(previous.Mode),User,HttpContext.TraceIdentifier,ct);await runs.LinkRetry(next,id,ct);
        return CreatedAtAction(nameof(Run),new{id=next},new{id=next});
    }
}
