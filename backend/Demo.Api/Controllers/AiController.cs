using Demo.Api.AI;
using Demo.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Demo.Api.Controllers;

[ApiController, Route("api/ai"), Authorize(Policy = AccessPolicies.BusinessRead)]
public sealed class AiController(AiAssistantService assistant) : ControllerBase
{
    [HttpGet("status")]
    public ActionResult<AiStatus> Status() => Ok(assistant.Status());
    [HttpPost("chat"), RequestSizeLimit(16000)]
    public async Task<ActionResult<AiChatResponse>> Chat(AiChatRequest request, CancellationToken ct) =>
        Ok(await assistant.Chat(request, User, HttpContext.TraceIdentifier, ct));
}
