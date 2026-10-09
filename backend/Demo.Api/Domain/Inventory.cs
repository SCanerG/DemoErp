namespace Demo.Api.Domain;

public sealed class Inventory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int QuantityOnHand { get; set; }
    public int MinimumStockLevel { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum MovementType { StockIn, StockOut, AdjustmentIncrease, AdjustmentDecrease, OrderDeduction, OrderCancellationReturn }
public enum AdjustmentDirection { Increase, Decrease }

public sealed class InventoryMovement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public MovementType MovementType { get; set; }
    public int Quantity { get; set; }
    public int QuantityBefore { get; set; }
    public int QuantityAfter { get; set; }
    // References are deliberately limited to Manual (null) and Order (real FK).
    public string ReferenceType { get; set; } = "Manual";
    public Guid? ReferenceId { get; set; }
    public Order? ReferenceOrder { get; set; }
    public string Reason { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;
}
