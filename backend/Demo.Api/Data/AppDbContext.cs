using Demo.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Demo.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, IHttpContextAccessor? http = null) : DbContext(options)
{
    public DbSet<BillOfMaterials> BillOfMaterials => Set<BillOfMaterials>();
    public DbSet<BillOfMaterialsItem> BillOfMaterialsItems => Set<BillOfMaterialsItem>();
    public DbSet<ProductionOrder> ProductionOrders => Set<ProductionOrder>();
    public DbSet<ProductionOrderMaterial> ProductionOrderMaterials => Set<ProductionOrderMaterial>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<WarehouseInventory> WarehouseInventories => Set<WarehouseInventory>();
    public DbSet<StockTransfer> StockTransfers => Set<StockTransfer>();
    public DbSet<StockTransferItem> StockTransferItems => Set<StockTransferItem>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Inventory> Inventories => Set<Inventory>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderItem> PurchaseOrderItems => Set<PurchaseOrderItem>();
    public DbSet<GoodsReceipt> GoodsReceipts => Set<GoodsReceipt>();
    public DbSet<GoodsReceiptItem> GoodsReceiptItems => Set<GoodsReceiptItem>();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Explicit allowlists: never serialize whole entities, request bodies or secrets.
        ChangeTracker.DetectChanges();
        var actor = http?.HttpContext?.User;
        Guid? actorId = Guid.TryParse(actor?.FindFirst("sub")?.Value, out var parsed) ? parsed : null;
        var entries = ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray();
        foreach (var entry in entries)
        {
            string[] fields = entry.Entity switch
            {
                Demo.Api.AI.Knowledge.KnowledgeDocument => ["OwnerId", "Scope", "Version", "Status", "UpdatedAt"],
                Demo.Api.AI.Monitoring.MonitoringRule => ["Kind", "Version", "Enabled", "Settings"],
                Demo.Api.AI.Agents.AiOrderProposal => ["ExecutionId", "Kind", "State", "Fingerprint", "ExpiresAt", "ResultId", "ResultNumber", "ResultStatus", "CompletedAt", "ErrorCategory"],
                Product => ["Name", "Price", "IsActive", "CategoryId", "ProductType"],
                Domain.BillOfMaterials => ["ProductId", "Version", "BaseQuantity", "IsActive", "ActivatedAt"],
                ProductionOrder => ["ProductionOrderNumber", "ProductId", "BillOfMaterialsId", "PlannedQuantity", "ProducedQuantity", "SourceWarehouseId", "DestinationWarehouseId", "Status", "StartedAt", "CompletedAt", "StartedByUserId", "CompletedByUserId"],
                Category => ["Name", "IsActive"],
                Customer => ["Name", "IsActive"],
                Order => ["WarehouseId", "Status", "CustomerId", "TotalAmount", "OrderNumber", "CompletedAt"],
                User => ["Name", "Email", "Role", "IsActive"],
                WarehouseInventory => ["WarehouseId", "ProductId", "MinimumStockLevel"],
                Warehouse => ["Code", "Name", "Location", "IsActive", "IsDefault"],
                StockTransfer => ["TransferNumber", "SourceWarehouseId", "DestinationWarehouseId", "Status", "CompletedAt", "CompletedByUserId"],
                Supplier => ["SupplierCode", "Name", "IsActive"],
                PurchaseOrder => ["PurchaseOrderNumber", "SupplierId", "Status", "ExpectedDeliveryDate", "TotalAmount"],
                GoodsReceipt => ["ReceiptNumber", "PurchaseOrderId", "WarehouseId"],
                _ => []
            };
            if (fields.Length == 0) continue;
            if (entry.Entity is WarehouseInventory && entry.State == EntityState.Added) continue;
            var changed = fields.Where(f => entry.State != EntityState.Modified || entry.Property(f).IsModified).ToArray();
            var action = entry.State == EntityState.Added ? "Create" : entry.State == EntityState.Deleted ? "Delete" : "Update";
            var entityId = (Guid)entry.Property("Id").CurrentValue!;
            if (entry.Entity is User && entry.State == EntityState.Modified)
                action = entry.Property("Role").IsModified ? "RoleChange" : entry.Property("IsActive").IsModified
                    ? (bool)entry.Property("IsActive").CurrentValue! ? "Activate" : "Deactivate" : action;
            if (entry.Entity is Order && entry.State == EntityState.Modified && entry.Property("Status").IsModified)
                action = (OrderStatus)entry.Property("Status").CurrentValue! switch
                { OrderStatus.Confirmed => "OrderConfirm", OrderStatus.Completed => "OrderComplete", OrderStatus.Cancelled => "OrderCancel", _ => "Update" };
            if (entry.Entity is Warehouse && entry.State == EntityState.Modified)
                action = entry.Property("IsDefault").IsModified ? "WarehouseDefaultChange" : entry.Property("IsActive").IsModified ? (bool)entry.Property("IsActive").CurrentValue! ? "Activate" : "Deactivate" : action;
            if (entry.Entity is StockTransfer transfer && entry.State == EntityState.Modified)
                action = entry.Property("Status").IsModified ? transfer.Status == TransferStatus.Completed ? "TransferComplete" : "TransferCancel" : "Update";
            if (entry.Entity is Supplier && entry.State == EntityState.Modified && entry.Property("IsActive").IsModified)
                action = (bool)entry.Property("IsActive").CurrentValue! ? "Activate" : "Deactivate";
            if (entry.Entity is PurchaseOrder && entry.State == EntityState.Modified)
                action = entry.Property("Status").IsModified ? (PurchaseOrderStatus)entry.Property("Status").CurrentValue! switch
                { PurchaseOrderStatus.Submitted => "PurchaseSubmit", PurchaseOrderStatus.Cancelled => "PurchaseCancel", _ => "PurchaseReceive" } : "Update";
            if (entry.Entity is GoodsReceipt) action = "GoodsReceiptPost";
            if (entry.Entity is Domain.BillOfMaterials && entry.State == EntityState.Modified && entry.Property("IsActive").IsModified)
                action = (bool)entry.Property("IsActive").CurrentValue! ? "BomActivate" : "BomDeactivate";
            if (entry.Entity is ProductionOrder po && entry.State == EntityState.Modified && entry.Property("Status").IsModified)
                action = ("Production" + po.Status) switch { "ProductionReleased" => "ProductionRelease", "ProductionInProgress" => "ProductionStart", "ProductionCompleted" => "ProductionComplete", _ => "ProductionCancel" };
            object? extra = null;
            // Metadata-only edits still produce an audit, without copying contact details/notes.
            if (entry.Entity is Supplier or PurchaseOrder or Warehouse or StockTransfer or Domain.BillOfMaterials or ProductionOrder && entry.State == EntityState.Modified && changed.Length == 0)
                extra = new { metadataChanged = true };
            if (entry.Entity is StockTransfer completed && action == "TransferComplete")
                extra = new { products = completed.Items.Select(i => new { i.ProductId, i.Quantity }).ToArray() };
            if (entry.Entity is WarehouseInventory inventory)
            {
                var movement = entries.Select(e => e.Entity).OfType<InventoryMovement>().SingleOrDefault(m => m.ProductId == inventory.ProductId && m.WarehouseId == inventory.WarehouseId && m.ReferenceType == "Manual");
                if (movement is not null)
                {
                    action = movement.MovementType is MovementType.StockIn ? "StockIn" : movement.MovementType is MovementType.StockOut ? "StockOut" : "StockAdjustment";
                    extra = new { inventory.WarehouseId, movementId = movement.Id, direction = movement.MovementType.ToString() };
                }
                else if (changed.Length > 0) { action = "MinimumLevelChange"; extra = new { inventory.WarehouseId }; }
                else continue; // Order quantity history belongs to InventoryMovements and Order audit.
                entityId = inventory.ProductId;
            }
            if (changed.Length == 0 && extra is null && entry.Entity is not (Product or Category or Customer)) continue;
            object? Value(string field, bool old) => (old ? entry.Property(field).OriginalValue : entry.Property(field).CurrentValue) is Enum value ? value.ToString()
                : old ? entry.Property(field).OriginalValue : entry.Property(field).CurrentValue;
            var before = entry.State == EntityState.Added ? null : changed.ToDictionary(f => char.ToLowerInvariant(f[0]) + f[1..], f => Value(f, true));
            var after = entry.State == EntityState.Deleted ? null : changed.ToDictionary(f => char.ToLowerInvariant(f[0]) + f[1..], f => Value(f, false));
            if (extra is not null) after!["operation"] = extra;
            AuditLogs.Add(new AuditLog
            {
                UserId = actorId, UserName = actor?.FindFirst("name")?.Value ?? "System / public registration",
                Action = action, EntityName = entry.Entity is WarehouseInventory ? "WarehouseInventory" : entry.Metadata.ClrType.Name,
                EntityId = entityId, OldValues = before is null ? null : System.Text.Json.JsonSerializer.Serialize(before),
                NewValues = after is null ? null : System.Text.Json.JsonSerializer.Serialize(after),
                Description = action, CorrelationId = http?.HttpContext?.TraceIdentifier
            });
        }
        return base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        AiModel.Configure(model);
        Demo.Api.AI.Knowledge.KnowledgeDbModel.Configure(model);
        Demo.Api.AI.Monitoring.MonitoringModel.Configure(model);
        Demo.Api.AI.Quality.QualityModel.Configure(model);
        Demo.Api.AI.Workflows.WorkflowModel.Configure(model);
        Demo.Api.AI.Agents.WorkflowProposals.Configure(model);
        ProcurementModel.Configure(model);
        WarehouseModel.Configure(model);
        ManufacturingModel.Configure(model);
        model.HasSequence<long>("OrderNumbers");
        model.Entity<AuditLog>(entity =>
        {
            entity.Property(x => x.UserName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Action).HasMaxLength(40).IsRequired();
            entity.Property(x => x.EntityName).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(200).IsRequired();
            entity.Property(x => x.CorrelationId).HasMaxLength(200);
            entity.Property(x => x.OldValues).HasColumnType("jsonb");
            entity.Property(x => x.NewValues).HasColumnType("jsonb");
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.CreatedAt, x.Id });
            entity.HasIndex(x => new { x.UserId, x.CreatedAt });
            entity.HasIndex(x => new { x.EntityName, x.EntityId });
            entity.HasIndex(x => new { x.Action, x.CreatedAt });
        });
        model.Entity<Inventory>(entity =>
        {
            entity.HasOne(x => x.Product).WithOne(x => x.Inventory).HasForeignKey<Inventory>(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => x.ProductId).IsUnique();
            entity.ToView("Inventories");
        });
        model.Entity<InventoryMovement>(entity =>
        {
            entity.Property(x => x.MovementType).HasConversion<string>().HasMaxLength(30);
            entity.Property(x => x.ReferenceType).HasMaxLength(10).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
            entity.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ReferenceOrder).WithMany().HasForeignKey(x => x.ReferenceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ReferenceReceipt).WithMany().HasForeignKey(x => x.GoodsReceiptId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.GoodsReceiptId, x.ProductId }).IsUnique().HasFilter("\"GoodsReceiptId\" IS NOT NULL");
            entity.HasIndex(x => new { x.ProductId, x.CreatedAt });
            entity.HasIndex(x => new { x.ReferenceId, x.ProductId, x.MovementType }).IsUnique().HasFilter("\"ReferenceType\" = 'Order'");
            entity.ToTable(t =>
            {
                t.HasCheckConstraint("CK_InventoryMovements_Quantity", "\"Quantity\" > 0 AND \"QuantityBefore\" >= 0 AND \"QuantityAfter\" >= 0");
                t.HasCheckConstraint("CK_InventoryMovements_Delta", "(\"MovementType\" IN ('StockIn','AdjustmentIncrease','OrderCancellationReturn','PurchaseReceipt','TransferIn','ProductionOutput') AND \"QuantityAfter\"::bigint = \"QuantityBefore\"::bigint + \"Quantity\") OR (\"MovementType\" IN ('StockOut','AdjustmentDecrease','OrderDeduction','TransferOut','ProductionConsumption') AND \"QuantityAfter\"::bigint = \"QuantityBefore\"::bigint - \"Quantity\")");
                t.HasCheckConstraint("CK_InventoryMovements_Reference", "\"ProductionOrderId\" IS NULL AND (\"StockTransferId\" IS NULL AND ((\"ReferenceType\" = 'Manual' AND \"ReferenceId\" IS NULL AND \"GoodsReceiptId\" IS NULL AND \"MovementType\" IN ('StockIn','StockOut','AdjustmentIncrease','AdjustmentDecrease')) OR (\"ReferenceType\" = 'Order' AND \"ReferenceId\" IS NOT NULL AND \"GoodsReceiptId\" IS NULL AND \"MovementType\" IN ('OrderDeduction','OrderCancellationReturn')) OR (\"ReferenceType\" = 'Receipt' AND \"ReferenceId\" IS NULL AND \"GoodsReceiptId\" IS NOT NULL AND \"MovementType\" = 'PurchaseReceipt')) OR (\"ReferenceType\" = 'Transfer' AND \"StockTransferId\" IS NOT NULL AND \"ReferenceId\" IS NULL AND \"GoodsReceiptId\" IS NULL AND \"MovementType\" IN ('TransferOut','TransferIn'))) OR (\"ProductionOrderId\" IS NOT NULL AND \"ReferenceType\" = 'Production' AND \"ReferenceId\" IS NULL AND \"GoodsReceiptId\" IS NULL AND \"StockTransferId\" IS NULL AND \"MovementType\" IN ('ProductionConsumption','ProductionOutput'))");
                t.HasCheckConstraint("CK_InventoryMovements_Reason", "length(btrim(\"Reason\")) > 0");
            });
        });
        model.Entity<User>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(254).IsRequired();
            entity.HasIndex(x => x.Email).IsUnique();
            entity.Property(x => x.PasswordHash).IsRequired();
            entity.Property(x => x.Role).HasConversion<string>().HasMaxLength(20).HasDefaultValue(UserRole.Viewer);
            entity.Property(x => x.IsActive).HasDefaultValue(true);
            entity.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Users_Role", "\"Role\" IN ('Admin','Manager','Viewer')");
                t.HasCheckConstraint("CK_Users_SecurityVersion", "\"SecurityVersion\" >= 0");
            });
        });
        model.Entity<Product>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.HasOne(x => x.Category).WithMany(x => x.Products).HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.Description).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.Price).HasPrecision(12, 2);
            entity.ToTable(t => t.HasCheckConstraint("CK_Products_Price", "\"Price\" >= 0"));
        });
        model.Entity<Category>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(2000).IsRequired();
        });
        model.Entity<Customer>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(254).IsRequired();
            entity.Property(x => x.Phone).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Address).HasMaxLength(1000).IsRequired();
        });
        model.Entity<Order>(entity =>
        {
            entity.Property(x => x.OrderNumber).HasMaxLength(50).IsRequired();
            entity.HasIndex(x => x.OrderNumber).IsUnique();
            entity.HasIndex(x => x.CompletedAt).HasFilter("\"Status\" = 'Completed' AND \"CompletedAt\" IS NOT NULL");
            entity.HasIndex(x => new { x.OrderDate, x.Id });
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.TotalAmount).HasPrecision(18, 2);
            entity.HasOne(x => x.Customer).WithMany(x => x.Orders).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(t => { t.HasCheckConstraint("CK_Orders_Total", "\"TotalAmount\" >= 0"); t.HasCheckConstraint("CK_Orders_Status", "\"Status\" IN ('Pending','Confirmed','Completed','Cancelled')"); });
        });
        model.Entity<OrderItem>(entity =>
        {
            entity.Property(x => x.UnitPrice).HasPrecision(12, 2);
            entity.Property(x => x.LineTotal).HasPrecision(18, 2);
            entity.HasOne(x => x.Order).WithMany(x => x.Items).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Product).WithMany(x => x.OrderItems).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(t => { t.HasCheckConstraint("CK_OrderItems_Quantity", "\"Quantity\" > 0"); t.HasCheckConstraint("CK_OrderItems_Prices", "\"UnitPrice\" >= 0 AND \"LineTotal\" = \"Quantity\" * \"UnitPrice\""); });
        });
    }
}
