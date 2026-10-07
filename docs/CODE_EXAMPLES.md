# Selected code examples / Seçilmiş kod örnekleri

These sanitized excerpts document the patterns used by the private application. Names and domain details are reduced deliberately; the complete implementation is not published.

## Protected Minimal API endpoint

```csharp
inventory.MapPost("/items", async (
    CreateProductCardRequest request,
    IProductCardService service,
    CancellationToken cancellationToken) =>
{
    var result = await service.CreateAsync(request, cancellationToken);
    return Results.Created($"/api/inventory/items/{result.Id}", result);
})
.RequireAuthorization("InventoryWrite")
.Produces<ProductCardDto>(StatusCodes.Status201Created)
.ProducesProblem(StatusCodes.Status400BadRequest)
.ProducesProblem(StatusCodes.Status401Unauthorized)
.ProducesProblem(StatusCodes.Status403Forbidden);
```

This keeps HTTP concerns at the endpoint boundary while the application service owns the use case and the domain owns business invariants.

## Request contract and validation boundary

```csharp
public sealed record CreateProductCardRequest(
    string Code,
    string Name,
    ProductKind Kind,
    string Unit,
    bool IsLotTracked,
    bool IsBarcodeTracked);

public static IReadOnlyDictionary<string, string[]> Validate(
    CreateProductCardRequest request)
{
    var errors = new Dictionary<string, string[]>();

    if (string.IsNullOrWhiteSpace(request.Code))
        errors[nameof(request.Code)] = ["Code is required."];

    if (request.Code is { Length: > 32 })
        errors[nameof(request.Code)] = ["Code cannot exceed 32 characters."];

    if (string.IsNullOrWhiteSpace(request.Unit))
        errors[nameof(request.Unit)] = ["Unit is required."];

    return errors;
}
```

Transport validation fails early. Stock, usage and lifecycle rules are still enforced by the domain/application layer so they cannot be bypassed by a different client.

## JWT and role policy configuration

```csharp
services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

services.AddAuthorization(options =>
{
    options.AddPolicy("InventoryWrite", policy =>
        policy.RequireRole("Administrator", "InventoryManager"));
});
```

The browser guard is not treated as a security boundary. Every protected operation is authorized again by the backend.

## Stable error contract

```csharp
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var feature = context.Features.Get<IExceptionHandlerFeature>();
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred.",
            Instance = context.Request.Path
        };

        problem.Extensions["traceId"] = context.TraceIdentifier;
        await Results.Problem(problem).ExecuteAsync(context);
    });
});
```

Production logging retains diagnostic detail; API consumers receive a predictable Problem Details response without stack traces or secrets.

## Refresh-token lifecycle

```text
issue random refresh token
  -> store SHA-256 digest, expiry and session metadata
  -> send raw token only as HttpOnly + SameSite cookie
  -> rotate on refresh
  -> revoke the previous digest
  -> reject replayed or expired tokens
  -> revoke the current session on logout
```

The complete token service, persistence model and security configuration remain private.
