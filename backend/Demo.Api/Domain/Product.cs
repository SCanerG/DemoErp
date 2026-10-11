namespace Demo.Api.Domain;

public sealed class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ProductType ProductType { get; set; } = ProductType.RawMaterial;
    public Guid CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public List<OrderItem> OrderItems { get; set; } = [];
    public Inventory Inventory { get; set; } = null!;
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Price { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
