using Demo.Api.AI;
using Demo.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Demo.Api.Controllers;

[ApiController, Route("api/ai"), Authorize(Policy = AccessPolicies.BusinessRead), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AiController(AiAssistantService assistant, AiConversationStore conversations, AiExecutionJournal journal, OrderDraftAgent drafts) : ControllerBase
{
    [HttpGet("status")]
    public ActionResult<AiStatus> Status() => Ok(assistant.Status());
    [HttpPost("order-drafts"), RequestSizeLimit(16000)]
    public async Task<ActionResult<OrderDraft>> Draft(OrderDraftRequest request, CancellationToken ct) =>
        Ok(await drafts.Prepare(request, User, HttpContext.TraceIdentifier, ct));
    [HttpGet("executions/{id:guid}")]
    public async Task<ActionResult<AiExecutionStatus>> Execution(Guid id, CancellationToken ct) => Ok(await journal.Read(id, User, ct));
    [HttpGet("conversations")]
    public async Task<ActionResult<List<AiConversationSummary>>> List(CancellationToken ct) => Ok(await conversations.List(User, ct));
    [HttpGet("conversations/{id:guid}")]
    public async Task<ActionResult<AiConversationHistory>> History(Guid id, CancellationToken ct) => Ok(await conversations.Read(id, User, ct));
    [HttpDelete("conversations/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await conversations.Delete(id, User, ct); return NoContent(); }
    [HttpPost("chat"), RequestSizeLimit(16000)]
    public async Task<ActionResult<AiChatResponse>> Chat(AiChatRequest request, CancellationToken ct) =>
        Ok(await assistant.Chat(request, User, HttpContext.TraceIdentifier, ct));
}
