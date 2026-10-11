using Demo.Api.Contracts;
using Demo.Api.Data;
using Microsoft.EntityFrameworkCore;
using Demo.Api.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Demo.Api.Controllers;
[ApiController, Authorize(Policy = AccessPolicies.BusinessRead), Route("api/boms")]
public sealed class BomsController(ManufacturingService service, IValidator<BomRequest> validator) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> List([FromQuery] ManufacturingFilter f, CancellationToken ct) => Ok(await service.Boms(f, ct));
    [HttpGet("{id:guid}")] public async Task<IActionResult> Get(Guid id, CancellationToken ct) => await service.Bom(id, ct) is {} r ? Ok(r) : NotFound();
    [HttpPost, Authorize(Policy = AccessPolicies.BusinessWrite)]
    public async Task<IActionResult> Create(BomRequest r, CancellationToken ct) { await validator.ValidateAndThrowAsync(r, ct); var result = await service.SaveBom(null, r, InventoryController.Actor(User), ct); return CreatedAtAction(nameof(Get), new {id = result!.Id}, result); }
    [HttpPut("{id:guid}"), Authorize(Policy = AccessPolicies.BusinessWrite)]
    public async Task<IActionResult> Update(Guid id, BomRequest r, CancellationToken ct) { await validator.ValidateAndThrowAsync(r, ct); return await service.SaveBom(id, r, InventoryController.Actor(User), ct) is {} result ? Ok(result) : NotFound(); }
    [HttpDelete("{id:guid}"), Authorize(Policy = AccessPolicies.BusinessDelete)] public async Task<IActionResult> Delete(Guid id, CancellationToken ct) => await service.DeleteBom(id, ct) ? NoContent() : NotFound();
    [HttpPost("{id:guid}/activate"), Authorize(Policy = AccessPolicies.UserManage)] public async Task<IActionResult> Activate(Guid id, CancellationToken ct) => await service.ActivateBom(id, true, ct) is {} r ? Ok(r) : NotFound();
    [HttpPost("{id:guid}/deactivate"), Authorize(Policy = AccessPolicies.UserManage)] public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct) => await service.ActivateBom(id, false, ct) is {} r ? Ok(r) : NotFound();
}
[ApiController, Authorize(Policy = AccessPolicies.BusinessRead), Route("api/production-orders")]
public sealed class ProductionOrdersController(AppDbContext db, ManufacturingService service, IValidator<ProductionRequest> validator) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> List([FromQuery] ManufacturingFilter f, CancellationToken ct) => Ok(await service.Productions(f, ct));
    [HttpGet("{id:guid}")] public async Task<IActionResult> Get(Guid id, CancellationToken ct) => await service.Production(id, ct) is {} r ? Ok(r) : NotFound();
    [HttpGet("{id:guid}/movements")]
    public async Task<IActionResult> Movements(Guid id, CancellationToken ct)
    {
        if (!await db.ProductionOrders.AnyAsync(o => o.Id == id, ct)) return NotFound();
        return Ok(await db.InventoryMovements.AsNoTracking().Where(m => m.ProductionOrderId == id).OrderBy(m => m.CreatedAt).ThenBy(m => m.Id)
            .Select(m => new MovementResponse(m.Id, m.ProductId, m.MovementType, m.Quantity, m.QuantityBefore, m.QuantityAfter, m.ReferenceType, null, null, m.Reason,
                m.CreatedAt, m.CreatedByUserId, m.CreatedByUser.Name, null, null, m.WarehouseId, m.Warehouse.Name, null, null, m.ProductionOrderId, m.ProductionOrder!.ProductionOrderNumber)).ToListAsync(ct));
    }
    [HttpGet("{id:guid}/material-availability")] public async Task<IActionResult> Availability(Guid id, CancellationToken ct) => Ok(await service.Availability(id, ct));
    [HttpPost("preview")] public async Task<IActionResult> Preview(ProductionRequest r, CancellationToken ct) { await validator.ValidateAndThrowAsync(r, ct); return Ok(await service.Preview(r, ct)); }
    [HttpPost, Authorize(Policy = AccessPolicies.BusinessWrite)]
    public async Task<IActionResult> Create(ProductionRequest r, CancellationToken ct) { await validator.ValidateAndThrowAsync(r, ct); var result = await service.SaveProduction(null, r, InventoryController.Actor(User), ct); return CreatedAtAction(nameof(Get), new {id = result!.Id}, result); }
    [HttpPut("{id:guid}"), Authorize(Policy = AccessPolicies.BusinessWrite)]
    public async Task<IActionResult> Update(Guid id, ProductionRequest r, CancellationToken ct) { await validator.ValidateAndThrowAsync(r, ct); return await service.SaveProduction(id, r, InventoryController.Actor(User), ct) is {} result ? Ok(result) : NotFound(); }
    [HttpPost("{id:guid}/release"), Authorize(Policy = AccessPolicies.BusinessWrite)] public Task<IActionResult> Release(Guid id, CancellationToken ct) => Command(id, "release", ct);
    [HttpPost("{id:guid}/start"), Authorize(Policy = AccessPolicies.InventoryManage)] public Task<IActionResult> Start(Guid id, CancellationToken ct) => Command(id, "start", ct);
    [HttpPost("{id:guid}/complete"), Authorize(Policy = AccessPolicies.InventoryManage)] public Task<IActionResult> Complete(Guid id, CancellationToken ct) => Command(id, "complete", ct);
    [HttpPost("{id:guid}/cancel"), Authorize(Policy = AccessPolicies.BusinessWrite)] public Task<IActionResult> Cancel(Guid id, CancellationToken ct) => Command(id, "cancel", ct);
    private async Task<IActionResult> Command(Guid id, string command, CancellationToken ct) => await service.Transition(id, command, InventoryController.Actor(User), ct) is {} r ? Ok(r) : NotFound();
    [HttpGet("/api/reports/manufacturing")] public async Task<IActionResult> Kpis(CancellationToken ct) => Ok(await service.Kpis(ct));
    [HttpGet("/api/reports/material-requirements")] public async Task<IActionResult> Requirements([FromQuery] ManufacturingFilter f, CancellationToken ct) => Ok(await service.Requirements(f, false, ct));
    [HttpGet("/api/reports/material-shortages")] public async Task<IActionResult> Shortages([FromQuery] ManufacturingFilter f, CancellationToken ct) => Ok(await service.Requirements(f, true, ct));
}
