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

public enum MovementType { StockIn, StockOut, AdjustmentIncrease, AdjustmentDecrease, OrderDeduction, OrderCancellationReturn, PurchaseReceipt, TransferOut, TransferIn, ProductionConsumption, ProductionOutput }
public enum AdjustmentDirection { Increase, Decrease }

public sealed class InventoryMovement
{
    public Guid? ProductionOrderId { get; set; }
    public ProductionOrder? ProductionOrder { get; set; }
    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;
    public Guid? StockTransferId { get; set; }
    public StockTransfer? StockTransfer { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public MovementType MovementType { get; set; }
    public int Quantity { get; set; }
    public int QuantityBefore { get; set; }
    public int QuantityAfter { get; set; }
    // Order and receipt have separate real foreign keys, never a polymorphic database FK.
    public string ReferenceType { get; set; } = "Manual";
    public Guid? ReferenceId { get; set; }
    public Order? ReferenceOrder { get; set; }
    public Guid? GoodsReceiptId { get; set; }
    public GoodsReceipt? ReferenceReceipt { get; set; }
    public string Reason { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;
}
