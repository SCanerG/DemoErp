using Demo.Api.Contracts;
using Demo.Api.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Demo.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/products")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
public sealed class ProductsController(ProductService products, IValidator<ProductRequest> validator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<List<ProductResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ProductResponse>>> List(CancellationToken ct) => Ok(await products.List(ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductResponse>> Get(Guid id, CancellationToken ct)
    {
        var product = await products.Get(id, ct);
        return product is null ? Missing() : Ok(product);
    }

    [HttpPost]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProductResponse>> Create(ProductRequest request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        var product = await products.Create(request, ct);
        return CreatedAtAction(nameof(Get), new { id = product.Id }, product);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProductResponse>> Update(Guid id, ProductRequest request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        var product = await products.Update(id, request, ct);
        return product is null ? Missing() : Ok(product);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        await products.Delete(id, ct) ? NoContent() : Missing();

    private ObjectResult Missing() => Problem(statusCode: 404, title: "Product not found",
        detail: "This product does not exist or has been deleted.");
}
