using Demo.Api.Contracts;
using Demo.Api.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Demo.Api.Controllers;
[ApiController, Authorize(Policy = AccessPolicies.BusinessRead), Route("api/categories")]
public sealed class CategoriesController(CategoryService service, IValidator<CategoryRequest> validator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<CategoryResponse>>> List(CancellationToken ct) => Ok(await service.List(ct));
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CategoryResponse>> Get(Guid id, CancellationToken ct)
    { var value = await service.Get(id, ct); return value is null ? Problem(statusCode: 404, title: "Record not found") : Ok(value); }
    [HttpPost]
    [Authorize(Policy = AccessPolicies.BusinessWrite)]
    public async Task<ActionResult<CategoryResponse>> Create(CategoryRequest request, CancellationToken ct)
    { await validator.ValidateAndThrowAsync(request, ct); var value = (await service.Save(null, request, ct))!; return CreatedAtAction(nameof(Get), new { id = value.Id }, value); }
    [HttpPut("{id:guid}")]
    [Authorize(Policy = AccessPolicies.BusinessWrite)]
    public async Task<ActionResult<CategoryResponse>> Update(Guid id, CategoryRequest request, CancellationToken ct)
    { await validator.ValidateAndThrowAsync(request, ct); var value = await service.Save(id, request, ct); return value is null ? Problem(statusCode: 404, title: "Record not found") : Ok(value); }
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AccessPolicies.BusinessDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) => await service.Delete(id, ct) ? NoContent() : Problem(statusCode: 404, title: "Record not found");
}
