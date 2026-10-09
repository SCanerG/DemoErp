using System.Security.Claims;
using Demo.Api.Contracts;
using Demo.Api.Domain;
using Demo.Api.Errors;
using Demo.Api.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Demo.Api.Controllers;

[ApiController, Authorize(Policy = AccessPolicies.BusinessRead), Route("api/inventory")]
public sealed class InventoryController(InventoryService service, IValidator<StockRequest> stockValidator,
    IValidator<AdjustmentRequest> adjustmentValidator, IValidator<MinimumLevelRequest> minimumValidator) : ControllerBase
{
    internal static Guid Actor(ClaimsPrincipal user) => Guid.TryParse(user.FindFirstValue("sub"), out var id)
        ? id : throw new BusinessException("invalidUser", 401);
    [HttpGet]
    public async Task<ActionResult<List<InventoryResponse>>> List(CancellationToken ct) => Ok(await service.List(ct));
    [HttpGet("{productId:guid}")]
    public async Task<ActionResult<InventoryResponse>> Get(Guid productId, CancellationToken ct) =>
        await service.Get(productId, ct) is { } value ? Ok(value) : Problem(statusCode: 404, title: "Record not found");
    [HttpGet("{productId:guid}/movements")]
    public async Task<ActionResult<List<MovementResponse>>> Movements(Guid productId, CancellationToken ct) =>
        await service.Get(productId, ct) is null ? Problem(statusCode: 404, title: "Record not found") : Ok(await service.Movements(productId, ct));
    [HttpPost("{productId:guid}/stock-in")]
    [Authorize(Policy = AccessPolicies.InventoryManage)]
    public async Task<ActionResult<InventoryResponse>> In(Guid productId, StockRequest request, CancellationToken ct)
    { await stockValidator.ValidateAndThrowAsync(request, ct); return Ok(await service.Change(productId, request.Quantity, MovementType.StockIn, Actor(User), request.Reason, ct)); }
    [HttpPost("{productId:guid}/stock-out")]
    [Authorize(Policy = AccessPolicies.InventoryManage)]
    public async Task<ActionResult<InventoryResponse>> Out(Guid productId, StockRequest request, CancellationToken ct)
    { await stockValidator.ValidateAndThrowAsync(request, ct); return Ok(await service.Change(productId, request.Quantity, MovementType.StockOut, Actor(User), request.Reason, ct)); }
    [HttpPost("{productId:guid}/adjust")]
    [Authorize(Policy = AccessPolicies.InventoryManage)]
    public async Task<ActionResult<InventoryResponse>> Adjust(Guid productId, AdjustmentRequest request, CancellationToken ct)
    { await adjustmentValidator.ValidateAndThrowAsync(request, ct); return Ok(await service.Change(productId, request.Quantity, request.Direction == AdjustmentDirection.Increase ? MovementType.AdjustmentIncrease : MovementType.AdjustmentDecrease, Actor(User), request.Reason, ct)); }
    [HttpPut("{productId:guid}/minimum-level")]
    [Authorize(Policy = AccessPolicies.InventoryManage)]
    public async Task<ActionResult<InventoryResponse>> Minimum(Guid productId, MinimumLevelRequest request, CancellationToken ct)
    { await minimumValidator.ValidateAndThrowAsync(request, ct); return Ok(await service.Minimum(productId, request.MinimumStockLevel, ct)); }
}
