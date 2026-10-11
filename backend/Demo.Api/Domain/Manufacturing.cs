namespace Demo.Api.Domain;
public enum ProductType { RawMaterial, FinishedGood }
public enum ProductionStatus { Draft, Released, InProgress, Completed, Cancelled }
public sealed class BillOfMaterials
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public string Version { get; set; } = "";
    public string Name { get; set; } = "";
    public int BaseQuantity { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;
    public List<BillOfMaterialsItem> Items { get; set; } = [];
}
public sealed class BillOfMaterialsItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BillOfMaterialsId { get; set; }
    public BillOfMaterials BillOfMaterials { get; set; } = null!;
    public Guid MaterialProductId { get; set; }
    public Product MaterialProduct { get; set; } = null!;
    public int QuantityRequired { get; set; }
}
public sealed class ProductionOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ProductionOrderNumber { get; set; } = "";
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public Guid BillOfMaterialsId { get; set; }
    public BillOfMaterials BillOfMaterials { get; set; } = null!;
    public int PlannedQuantity { get; set; }
    public int ProducedQuantity { get; set; }
    public Guid SourceWarehouseId { get; set; }
    public Warehouse SourceWarehouse { get; set; } = null!;
    public Guid DestinationWarehouseId { get; set; }
    public Warehouse DestinationWarehouse { get; set; } = null!;
    public ProductionStatus Status { get; set; }
    public DateTimeOffset? PlannedStartDate { get; set; }
    public DateTimeOffset? PlannedEndDate { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string Notes { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;
    public Guid? StartedByUserId { get; set; }
    public User? StartedByUser { get; set; }
    public Guid? CompletedByUserId { get; set; }
    public User? CompletedByUser { get; set; }
    public List<ProductionOrderMaterial> Materials { get; set; } = [];
}
public sealed class ProductionOrderMaterial
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductionOrderId { get; set; }
    public ProductionOrder ProductionOrder { get; set; } = null!;
    public Guid MaterialProductId { get; set; }
    public Product MaterialProduct { get; set; } = null!;
    public int QuantityRequired { get; set; }
    public int QuantityConsumed { get; set; }
}
