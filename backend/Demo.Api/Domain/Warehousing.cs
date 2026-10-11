namespace Demo.Api.Domain;

public sealed class Warehouse
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Location { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public bool IsDefault { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
public sealed class WarehouseInventory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int QuantityOnHand { get; set; }
    public int MinimumStockLevel { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public enum TransferStatus { Draft, Completed, Cancelled }
public sealed class StockTransfer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TransferNumber { get; set; } = "";
    public Guid SourceWarehouseId { get; set; }
    public Warehouse SourceWarehouse { get; set; } = null!;
    public Guid DestinationWarehouseId { get; set; }
    public Warehouse DestinationWarehouse { get; set; } = null!;
    public TransferStatus Status { get; set; } = TransferStatus.Draft;
    public string Notes { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;
    public Guid? CompletedByUserId { get; set; }
    public User? CompletedByUser { get; set; }
    public List<StockTransferItem> Items { get; set; } = [];
}
public sealed class StockTransferItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StockTransferId { get; set; }
    public StockTransfer StockTransfer { get; set; } = null!;
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int Quantity { get; set; }
}
