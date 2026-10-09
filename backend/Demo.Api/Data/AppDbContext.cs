using Demo.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Demo.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, IHttpContextAccessor? http = null) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Inventory> Inventories => Set<Inventory>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

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
                Product => ["Name", "Price", "IsActive", "CategoryId"],
                Category => ["Name", "IsActive"],
                Customer => ["Name", "IsActive"],
                Order => ["Status", "CustomerId", "TotalAmount", "OrderNumber", "CompletedAt"],
                User => ["Name", "Email", "Role", "IsActive"],
                Inventory => ["MinimumStockLevel"],
                _ => []
            };
            if (fields.Length == 0) continue;
            if (entry.Entity is Inventory && entry.State == EntityState.Added) continue;
            var changed = fields.Where(f => entry.State != EntityState.Modified || entry.Property(f).IsModified).ToArray();
            var action = entry.State == EntityState.Added ? "Create" : entry.State == EntityState.Deleted ? "Delete" : "Update";
            var entityId = (Guid)entry.Property("Id").CurrentValue!;
            if (entry.Entity is User && entry.State == EntityState.Modified)
                action = entry.Property("Role").IsModified ? "RoleChange" : entry.Property("IsActive").IsModified
                    ? (bool)entry.Property("IsActive").CurrentValue! ? "Activate" : "Deactivate" : action;
            if (entry.Entity is Order && entry.State == EntityState.Modified && entry.Property("Status").IsModified)
                action = (OrderStatus)entry.Property("Status").CurrentValue! switch
                { OrderStatus.Confirmed => "OrderConfirm", OrderStatus.Completed => "OrderComplete", OrderStatus.Cancelled => "OrderCancel", _ => "Update" };
            object? extra = null;
            if (entry.Entity is Inventory inventory)
            {
                var movement = entries.Select(e => e.Entity).OfType<InventoryMovement>().SingleOrDefault(m => m.ProductId == inventory.ProductId && m.ReferenceType == "Manual");
                if (movement is not null)
                {
                    action = movement.MovementType is MovementType.StockIn ? "StockIn" : movement.MovementType is MovementType.StockOut ? "StockOut" : "StockAdjustment";
                    extra = new { movementId = movement.Id, direction = movement.MovementType.ToString() };
                }
                else if (changed.Length > 0) action = "MinimumLevelChange";
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
                Action = action, EntityName = entry.Entity is Inventory ? "Inventory" : entry.Metadata.ClrType.Name,
                EntityId = entityId, OldValues = before is null ? null : System.Text.Json.JsonSerializer.Serialize(before),
                NewValues = after is null ? null : System.Text.Json.JsonSerializer.Serialize(after),
                Description = action, CorrelationId = http?.HttpContext?.TraceIdentifier
            });
        }
        return base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
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
            entity.ToTable(t => t.HasCheckConstraint("CK_Inventories_Levels", "\"QuantityOnHand\" >= 0 AND \"MinimumStockLevel\" >= 0"));
        });
        model.Entity<InventoryMovement>(entity =>
        {
            entity.Property(x => x.MovementType).HasConversion<string>().HasMaxLength(30);
            entity.Property(x => x.ReferenceType).HasMaxLength(10).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
            entity.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ReferenceOrder).WithMany().HasForeignKey(x => x.ReferenceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.ProductId, x.CreatedAt });
            entity.HasIndex(x => new { x.ReferenceId, x.ProductId, x.MovementType }).IsUnique().HasFilter("\"ReferenceType\" = 'Order'");
            entity.ToTable(t =>
            {
                t.HasCheckConstraint("CK_InventoryMovements_Quantity", "\"Quantity\" > 0 AND \"QuantityBefore\" >= 0 AND \"QuantityAfter\" >= 0");
                t.HasCheckConstraint("CK_InventoryMovements_Delta", "(\"MovementType\" IN ('StockIn','AdjustmentIncrease','OrderCancellationReturn') AND \"QuantityAfter\"::bigint = \"QuantityBefore\"::bigint + \"Quantity\") OR (\"MovementType\" IN ('StockOut','AdjustmentDecrease','OrderDeduction') AND \"QuantityAfter\"::bigint = \"QuantityBefore\"::bigint - \"Quantity\")");
                t.HasCheckConstraint("CK_InventoryMovements_Reference", "(\"ReferenceType\" = 'Manual' AND \"ReferenceId\" IS NULL AND \"MovementType\" IN ('StockIn','StockOut','AdjustmentIncrease','AdjustmentDecrease')) OR (\"ReferenceType\" = 'Order' AND \"ReferenceId\" IS NOT NULL AND \"MovementType\" IN ('OrderDeduction','OrderCancellationReturn'))");
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
