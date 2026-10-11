using Demo.Api.Domain;
using Microsoft.EntityFrameworkCore;
namespace Demo.Api.Data;
internal static class WarehouseModel
{
    internal static void Configure(ModelBuilder model)
    {
        model.HasSequence<long>("TransferNumbers");
        model.Entity<Warehouse>(e => {
            e.Property(x => x.Code).HasMaxLength(40).IsRequired(); e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.Description).HasMaxLength(2000).IsRequired(); e.Property(x => x.Location).HasMaxLength(250).IsRequired();
            e.HasIndex(x => x.Code).IsUnique(); e.HasIndex(x => x.IsDefault).IsUnique().HasFilter("\"IsDefault\""); e.HasIndex(x => new { x.Name, x.Id });
            e.ToTable(t => { t.HasCheckConstraint("CK_Warehouses_Code", "\"Code\" ~ '^[A-Z0-9][A-Z0-9_-]{0,39}$'"); t.HasCheckConstraint("CK_Warehouses_Name", "length(btrim(\"Name\")) > 0"); t.HasCheckConstraint("CK_Warehouses_DefaultActive", "NOT \"IsDefault\" OR \"IsActive\""); });
        });
        model.Entity<WarehouseInventory>(e => {
            e.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.WarehouseId, x.ProductId }).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("CK_WarehouseInventories_Levels", "\"QuantityOnHand\" >= 0 AND \"MinimumStockLevel\" >= 0"));
        });
        model.Entity<StockTransfer>(e => {
            e.Property(x => x.TransferNumber).HasMaxLength(50).IsRequired(); e.HasIndex(x => x.TransferNumber).IsUnique(); e.Property(x => x.Notes).HasMaxLength(2000).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasOne(x => x.SourceWarehouse).WithMany().HasForeignKey(x => x.SourceWarehouseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.DestinationWarehouse).WithMany().HasForeignKey(x => x.DestinationWarehouseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CompletedByUser).WithMany().HasForeignKey(x => x.CompletedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.CreatedAt, x.Id }); e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.ToTable(t => { t.HasCheckConstraint("CK_StockTransfers_Warehouses", "\"SourceWarehouseId\" <> \"DestinationWarehouseId\""); t.HasCheckConstraint("CK_StockTransfers_Status", "\"Status\" IN ('Draft','Completed','Cancelled')");
                t.HasCheckConstraint("CK_StockTransfers_Completion", "(\"Status\" = 'Completed') = (\"CompletedAt\" IS NOT NULL AND \"CompletedByUserId\" IS NOT NULL)"); });
        });
        model.Entity<StockTransferItem>(e => {
            e.HasOne(x => x.StockTransfer).WithMany(x => x.Items).HasForeignKey(x => x.StockTransferId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.StockTransferId, x.ProductId }).IsUnique(); e.ToTable(t => t.HasCheckConstraint("CK_StockTransferItems_Quantity", "\"Quantity\" > 0"));
        });
        model.Entity<Order>().HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<GoodsReceipt>().HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<InventoryMovement>().HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<InventoryMovement>().HasOne(x => x.StockTransfer).WithMany().HasForeignKey(x => x.StockTransferId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<InventoryMovement>().HasIndex(x => new { x.WarehouseId, x.CreatedAt, x.Id });
        model.Entity<InventoryMovement>().HasIndex(x => new { x.StockTransferId, x.ProductId, x.MovementType }).IsUnique().HasFilter("\"StockTransferId\" IS NOT NULL");
    }
}
