using Demo.Api.Contracts;
using Demo.Api.Errors;
using Demo.Api.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Demo.Api.Controllers;

[ApiController, Authorize(Policy = AccessPolicies.BusinessRead), Route("api/suppliers")]
public sealed class SuppliersController(SupplierService service, IValidator<SupplierRequest> validator) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> List([FromQuery] SupplierFilter filter, CancellationToken ct) => Ok(await service.List(filter, ct));
    [HttpGet("{id:guid}")] public async Task<IActionResult> Get(Guid id, CancellationToken ct) => await service.Get(id, ct) is { } value ? Ok(value) : NotFound();
    [HttpPost, Authorize(Policy = AccessPolicies.BusinessWrite)]
    public async Task<IActionResult> Create(SupplierRequest input, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(input, ct); var value = (await service.Save(null, input, ct))!;
        return CreatedAtAction(nameof(Get), new { id = value.Id }, value);
    }
    [HttpPut("{id:guid}"), Authorize(Policy = AccessPolicies.BusinessWrite)]
    public async Task<IActionResult> Update(Guid id, SupplierRequest input, CancellationToken ct)
    { await validator.ValidateAndThrowAsync(input, ct); return await service.Save(id, input, ct) is { } value ? Ok(value) : NotFound(); }
    [HttpPut("{id:guid}/status"), Authorize(Policy = AccessPolicies.BusinessWrite)]
    public async Task<IActionResult> Status(Guid id, SupplierStatusRequest input, CancellationToken ct) => await service.Status(id, input.IsActive, ct) is { } value ? Ok(value) : NotFound();
}

[ApiController, Authorize(Policy = AccessPolicies.BusinessRead), Route("api/purchase-orders")]
public sealed class PurchaseOrdersController(ProcurementService service, IValidator<PurchaseRequest> validator, IValidator<ReceiptRequest> receiptValidator) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> List([FromQuery] PurchaseFilter filter, CancellationToken ct) => Ok(await service.List(filter, ct));
    [HttpGet("{id:guid}")] public async Task<IActionResult> Get(Guid id, CancellationToken ct) => await service.Get(id, ct) is { } value ? Ok(value) : NotFound();
    [HttpPost, Authorize(Policy = AccessPolicies.BusinessWrite)]
    public async Task<IActionResult> Create(PurchaseRequest input, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(input, ct); var value = (await service.Save(null, input, InventoryController.Actor(User), ct))!;
        return CreatedAtAction(nameof(Get), new { id = value.Id }, value);
    }
    [HttpPut("{id:guid}"), Authorize(Policy = AccessPolicies.BusinessWrite)]
    public async Task<IActionResult> Update(Guid id, PurchaseRequest input, CancellationToken ct)
    { await validator.ValidateAndThrowAsync(input, ct); return await service.Save(id, input, InventoryController.Actor(User), ct) is { } value ? Ok(value) : NotFound(); }
    [HttpPost("{id:guid}/submit"), Authorize(Policy = AccessPolicies.BusinessWrite)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken ct) => await service.Transition(id, true, ct) is { } value ? Ok(value) : NotFound();
    [HttpPost("{id:guid}/cancel"), Authorize(Policy = AccessPolicies.BusinessWrite)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) => await service.Transition(id, false, ct) is { } value ? Ok(value) : NotFound();
    [HttpGet("{id:guid}/receipts")]
    public async Task<IActionResult> Receipts(Guid id, CancellationToken ct, int page = 1, int pageSize = 20) => Ok(await service.Receipts(id, page, pageSize, ct));
    [HttpPost("{id:guid}/receipts"), Authorize(Policy = AccessPolicies.BusinessWrite)]
    public async Task<IActionResult> Receive(Guid id, ReceiptRequest input, CancellationToken ct)
    {
        if (Request.Headers["Idempotency-Key"].Count != 1 || !Guid.TryParse(Request.Headers["Idempotency-Key"], out var key) || key == Guid.Empty) throw new BusinessException("receiptKeyRequired");
        await receiptValidator.ValidateAndThrowAsync(input, ct);
        var result = await service.Receive(id, input, InventoryController.Actor(User), key, ct);
        Response.Headers["Idempotency-Replayed"] = result.Replay ? "true" : "false";
        return result.Replay ? Ok(result.Receipt) : CreatedAtAction(nameof(GoodsReceiptsController.Get), "GoodsReceipts", new { id = result.Receipt.Id }, result.Receipt);
    }
}

[ApiController, Authorize(Policy = AccessPolicies.BusinessRead), Route("api/goods-receipts")]
public sealed class GoodsReceiptsController(ProcurementService service) : ControllerBase
{
    [HttpGet("{id:guid}")] public async Task<IActionResult> Get(Guid id, CancellationToken ct) => await service.Receipt(id, ct) is { } value ? Ok(value) : NotFound();
}

[ApiController, Authorize(Policy = AccessPolicies.BusinessRead)]
public sealed class ProcurementReportsController(ProcurementService service) : ControllerBase
{
    [HttpGet("api/reports/procurement")]
    public async Task<IActionResult> Report([FromQuery] PurchaseFilter filter, CancellationToken ct) => Ok(await service.List(filter, ct));
    [HttpGet("api/dashboard/procurement-summary")]
    public async Task<IActionResult> Summary(CancellationToken ct)
    {
        return Ok(await service.Summary(ct));
    }
}
