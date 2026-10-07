using Demo.Api.Contracts;
using Demo.Api.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Demo.Api.Controllers;
[ApiController, Authorize, Route("api/orders")]
public sealed class OrdersController(OrderService service, IValidator<OrderRequest> validator, IValidator<OrderStatusRequest> statusValidator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<OrderSummary>>> List(CancellationToken ct) => Ok(await service.List(ct));
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderResponse>> Get(Guid id, CancellationToken ct)
    { var value = await service.Get(id, ct); return value is null ? Problem(statusCode: 404, title: "Record not found") : Ok(value); }
    [HttpPost]
    public async Task<ActionResult<OrderResponse>> Create(OrderRequest request, CancellationToken ct)
    { await validator.ValidateAndThrowAsync(request, ct); var value = await service.Create(request, ct); return CreatedAtAction(nameof(Get), new { id = value.Id }, value); }
    [HttpPut("{id:guid}/status")]
    public async Task<ActionResult<OrderResponse>> Status(Guid id, OrderStatusRequest request, CancellationToken ct)
    { await statusValidator.ValidateAndThrowAsync(request, ct); var value = await service.ChangeStatus(id, request.Status, ct); return value is null ? Problem(statusCode: 404, title: "Record not found") : Ok(value); }
}
