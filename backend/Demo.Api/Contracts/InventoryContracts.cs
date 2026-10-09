using System.Text.Json.Serialization;
using Demo.Api.Domain;
using FluentValidation;

namespace Demo.Api.Contracts;

public sealed record InventoryResponse(Guid Id, Guid ProductId, string ProductName, string CategoryName, bool IsActive,
    int QuantityOnHand, int MinimumStockLevel, DateTimeOffset UpdatedAt);
public sealed record MovementResponse(Guid Id, Guid ProductId, MovementType MovementType, int Quantity, int QuantityBefore,
    int QuantityAfter, string ReferenceType, Guid? ReferenceId, string? OrderNumber, string Reason, DateTimeOffset CreatedAt,
    Guid CreatedByUserId, string PerformedBy);
public sealed record StockRequest(int Quantity, string Reason);
public sealed record AdjustmentRequest([property: JsonRequired] AdjustmentDirection Direction, int Quantity, string Reason);
public sealed record MinimumLevelRequest([property: JsonRequired] int MinimumStockLevel);

public sealed class StockValidator : AbstractValidator<StockRequest>
{
    public StockValidator() { RuleFor(x => x.Quantity).GreaterThan(0); RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000); }
}
public sealed class AdjustmentValidator : AbstractValidator<AdjustmentRequest>
{
    public AdjustmentValidator() { RuleFor(x => x.Direction).IsInEnum(); RuleFor(x => x.Quantity).GreaterThan(0); RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000); }
}
public sealed class MinimumLevelValidator : AbstractValidator<MinimumLevelRequest>
{
    public MinimumLevelValidator() { RuleFor(x => x.MinimumStockLevel).GreaterThanOrEqualTo(0); }
}
