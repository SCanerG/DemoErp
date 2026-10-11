using Demo.Api.Contracts;
using Demo.Api.Data;
using Demo.Api.Domain;
using Demo.Api.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace Demo.Api.Controllers;

[ApiController, Authorize(Policy = AccessPolicies.BusinessRead), Route("api/warehouses")]
public sealed class WarehousesController(WarehouseService service, InventoryService inventory, AppDbContext db,
    IValidator<WarehouseRequest> validator, IValidator<StockRequest> stockValidator,
    IValidator<AdjustmentRequest> adjustmentValidator, IValidator<MinimumLevelRequest> minimumValidator) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> List([FromQuery] WarehouseFilter f, CancellationToken ct) => Ok(await service.List(f, ct));
    [HttpGet("{id:guid}")] public async Task<IActionResult> Get(Guid id, CancellationToken ct) => await service.Get(id, ct) is {} value ? Ok(value) : NotFound();
    [HttpPost, Authorize(Policy = AccessPolicies.UserManage)]
    public async Task<IActionResult> Create(WarehouseRequest request, CancellationToken ct) { await validator.ValidateAndThrowAsync(request, ct); var value = await service.Save(null, request, ct); return CreatedAtAction(nameof(Get), new {id=value!.Id}, value); }
    [HttpPut("{id:guid}"), Authorize(Policy = AccessPolicies.UserManage)]
    public async Task<IActionResult> Update(Guid id, WarehouseRequest request, CancellationToken ct) { await validator.ValidateAndThrowAsync(request, ct); return await service.Save(id, request, ct) is {} value ? Ok(value) : NotFound(); }
    [HttpPut("{id:guid}/status"), Authorize(Policy = AccessPolicies.UserManage)]
    public async Task<IActionResult> Status(Guid id, WarehouseStatusRequest request, CancellationToken ct) => await service.Status(id, request.IsActive, ct) is {} value ? Ok(value) : NotFound();
    [HttpPost("{id:guid}/set-default"), Authorize(Policy = AccessPolicies.UserManage)]
    public async Task<IActionResult> Default(Guid id, CancellationToken ct) => await service.SetDefault(id, ct) is {} value ? Ok(value) : NotFound();
    [HttpGet("{id:guid}/inventory")] public async Task<IActionResult> Stock(Guid id, [FromQuery] WarehouseFilter f, CancellationToken ct) => Ok(await service.Stock(id, f, ct));
    [HttpGet("{id:guid}/inventory/{productId:guid}")] public async Task<IActionResult> Product(Guid id, Guid productId, CancellationToken ct) => await service.ProductStock(id, productId, ct) is {} value ? Ok(value) : NotFound();
    [HttpGet("/api/products/{productId:guid}/warehouse-stock")] public async Task<IActionResult> ProductWarehouses(Guid productId, [FromQuery] WarehouseFilter f, CancellationToken ct) => Ok(await service.ProductWarehouses(productId, f, ct));
    [HttpGet("/api/reports/warehouses")] public async Task<IActionResult> Report([FromQuery] WarehouseFilter f, CancellationToken ct) => Ok(await service.Report(f, ct));
    [HttpGet("{id:guid}/movements")]
    public async Task<IActionResult> Movements(Guid id, [FromQuery] WarehouseFilter f, CancellationToken ct)
    {
        f.Validate(); if (await service.Get(id, ct) is null) return NotFound();
        var q = db.InventoryMovements.AsNoTracking().Where(m => m.WarehouseId == id);
        if (!string.IsNullOrWhiteSpace(f.Search)) q = q.Where(m => m.Product.Name.ToLower().Contains(f.Search.Trim().ToLower()));
        if (f.CategoryId.HasValue) q = q.Where(m => m.Product.CategoryId == f.CategoryId);
        return Ok(await ReportingService.Page(q.OrderByDescending(m => m.CreatedAt).ThenBy(m => m.Id).Select(m => new MovementResponse(m.Id, m.ProductId, m.MovementType, m.Quantity, m.QuantityBefore, m.QuantityAfter,
            m.ReferenceType, m.ReferenceId, m.ReferenceOrder == null ? null : m.ReferenceOrder.OrderNumber, m.Reason, m.CreatedAt, m.CreatedByUserId, m.CreatedByUser.Name,
            m.GoodsReceiptId, m.ReferenceReceipt == null ? null : m.ReferenceReceipt.ReceiptNumber, m.WarehouseId, m.Warehouse.Name, m.StockTransferId, m.StockTransfer == null ? null : m.StockTransfer.TransferNumber, m.ProductionOrderId, m.ProductionOrder == null ? null : m.ProductionOrder.ProductionOrderNumber)), new() { Page=f.Page, PageSize=f.PageSize }, ct));
    }
    [HttpPost("{id:guid}/inventory/{productId:guid}/stock-in"), Authorize(Policy = AccessPolicies.InventoryManage)]
    public async Task<IActionResult> In(Guid id, Guid productId, StockRequest request, CancellationToken ct) { await stockValidator.ValidateAndThrowAsync(request, ct); await inventory.Change(productId, request.Quantity, MovementType.StockIn, InventoryController.Actor(User), request.Reason, ct, id); return Ok(await service.ProductStock(id, productId, ct)); }
    [HttpPost("{id:guid}/inventory/{productId:guid}/stock-out"), Authorize(Policy = AccessPolicies.InventoryManage)]
    public async Task<IActionResult> Out(Guid id, Guid productId, StockRequest request, CancellationToken ct) { await stockValidator.ValidateAndThrowAsync(request, ct); await inventory.Change(productId, request.Quantity, MovementType.StockOut, InventoryController.Actor(User), request.Reason, ct, id); return Ok(await service.ProductStock(id, productId, ct)); }
    [HttpPost("{id:guid}/inventory/{productId:guid}/adjust"), Authorize(Policy = AccessPolicies.InventoryManage)]
    public async Task<IActionResult> Adjust(Guid id, Guid productId, AdjustmentRequest request, CancellationToken ct) { await adjustmentValidator.ValidateAndThrowAsync(request, ct); await inventory.Change(productId, request.Quantity, request.Direction == AdjustmentDirection.Increase ? MovementType.AdjustmentIncrease : MovementType.AdjustmentDecrease, InventoryController.Actor(User), request.Reason, ct, id); return Ok(await service.ProductStock(id, productId, ct)); }
    [HttpPut("{id:guid}/inventory/{productId:guid}/minimum-level"), Authorize(Policy = AccessPolicies.InventoryManage)]
    public async Task<IActionResult> Minimum(Guid id, Guid productId, MinimumLevelRequest request, CancellationToken ct) { await minimumValidator.ValidateAndThrowAsync(request, ct); await inventory.Minimum(productId, request.MinimumStockLevel, ct, id); return Ok(await service.ProductStock(id, productId, ct)); }
}
public sealed record WarehouseStatusRequest(bool IsActive);

[ApiController, Authorize(Policy = AccessPolicies.BusinessRead), Route("api/stock-transfers")]
public sealed class StockTransfersController(TransferService service, IValidator<TransferRequest> validator) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> List([FromQuery] TransferFilter f, CancellationToken ct) => Ok(await service.List(f, ct));
    [HttpGet("{id:guid}")] public async Task<IActionResult> Get(Guid id, CancellationToken ct) => await service.Get(id, ct) is {} value ? Ok(value) : NotFound();
    [HttpPost, Authorize(Policy = AccessPolicies.InventoryManage)]
    public async Task<IActionResult> Create(TransferRequest request, CancellationToken ct) { await validator.ValidateAndThrowAsync(request, ct); var value = await service.Save(null, request, InventoryController.Actor(User), ct); return CreatedAtAction(nameof(Get), new {id=value!.Id}, value); }
    [HttpPut("{id:guid}"), Authorize(Policy = AccessPolicies.InventoryManage)]
    public async Task<IActionResult> Update(Guid id, TransferRequest request, CancellationToken ct) { await validator.ValidateAndThrowAsync(request, ct); return await service.Save(id, request, InventoryController.Actor(User), ct) is {} value ? Ok(value) : NotFound(); }
    [HttpPost("{id:guid}/complete"), Authorize(Policy = AccessPolicies.InventoryManage)]
    public async Task<IActionResult> Complete(Guid id, CancellationToken ct) => await service.Transition(id, true, InventoryController.Actor(User), ct) is {} value ? Ok(value) : NotFound();
    [HttpPost("{id:guid}/cancel"), Authorize(Policy = AccessPolicies.InventoryManage)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) => await service.Transition(id, false, InventoryController.Actor(User), ct) is {} value ? Ok(value) : NotFound();
}
