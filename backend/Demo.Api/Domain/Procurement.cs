namespace Demo.Api.Domain;

public sealed class Supplier
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string SupplierCode { get; set; } = "";
    public string Name { get; set; } = "";
    public string ContactPerson { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Address { get; set; } = "";
    public string TaxNumber { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
    public List<PurchaseOrder> PurchaseOrders { get; set; } = [];
}
public enum PurchaseOrderStatus { Draft, Submitted, PartiallyReceived, Received, Cancelled }
public sealed class PurchaseOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string PurchaseOrderNumber { get; set; } = "";
    public Guid SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;
    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;
    public DateTimeOffset OrderDate { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpectedDeliveryDate { get; set; }
    public decimal TotalAmount { get; set; }
    public string Notes { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;
    public List<PurchaseOrderItem> Items { get; set; } = [];
    public List<GoodsReceipt> Receipts { get; set; } = [];
}
public sealed class PurchaseOrderItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PurchaseOrderId { get; set; }
    public PurchaseOrder PurchaseOrder { get; set; } = null!;
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int QuantityOrdered { get; set; }
    public int QuantityReceived { get; set; }
    public decimal UnitCost { get; set; }
    public decimal LineTotal { get; set; }
}
public sealed class GoodsReceipt
{
    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ReceiptNumber { get; set; } = "";
    public Guid PurchaseOrderId { get; set; }
    public PurchaseOrder PurchaseOrder { get; set; } = null!;
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Notes { get; set; } = "";
    public Guid CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid IdempotencyKey { get; set; }
    public int FingerprintVersion { get; set; } = 2;
    public string PayloadFingerprint { get; set; } = "";
    public List<GoodsReceiptItem> Items { get; set; } = [];
}
public sealed class GoodsReceiptItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GoodsReceiptId { get; set; }
    public GoodsReceipt GoodsReceipt { get; set; } = null!;
    public Guid PurchaseOrderItemId { get; set; }
    public PurchaseOrderItem PurchaseOrderItem { get; set; } = null!;
    public int QuantityReceived { get; set; }
}
