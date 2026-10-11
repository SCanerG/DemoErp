using Demo.Api.Domain;
using Microsoft.EntityFrameworkCore;
namespace Demo.Api.Data;
internal static class ManufacturingModel
{
    internal static void Configure(ModelBuilder model)
    {
        model.HasSequence<long>("ProductionNumbers");
        model.Entity<Product>().Property(p => p.ProductType).HasConversion<string>().HasMaxLength(20).HasDefaultValue(ProductType.RawMaterial);
        model.Entity<Product>().ToTable(t => t.HasCheckConstraint("CK_Products_Type", "\"ProductType\" IN ('RawMaterial','FinishedGood')"));
        model.Entity<BillOfMaterials>(e => {
            e.Property(x => x.Version).HasMaxLength(40).IsRequired(); e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ProductId, x.Version }).IsUnique();
            e.HasIndex(x => x.ProductId).IsUnique().HasFilter("\"IsActive\"");
            e.HasIndex(x => new { x.CreatedAt, x.Id });
            e.ToTable(t => { t.HasCheckConstraint("CK_BillOfMaterials_Base", "\"BaseQuantity\" > 0 AND length(btrim(\"Version\")) > 0"); t.HasCheckConstraint("CK_BillOfMaterials_Active", "NOT \"IsActive\" OR \"ActivatedAt\" IS NOT NULL"); });
        });
        model.Entity<BillOfMaterialsItem>(e => {
            e.HasOne(x => x.BillOfMaterials).WithMany(x => x.Items).HasForeignKey(x => x.BillOfMaterialsId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.MaterialProduct).WithMany().HasForeignKey(x => x.MaterialProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.BillOfMaterialsId, x.MaterialProductId }).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("CK_BillOfMaterialsItems_Quantity", "\"QuantityRequired\" > 0"));
        });
        model.Entity<ProductionOrder>(e => {
            e.Property(x => x.ProductionOrderNumber).HasMaxLength(50).IsRequired(); e.HasIndex(x => x.ProductionOrderNumber).IsUnique();
            e.Property(x => x.Notes).HasMaxLength(2000).IsRequired(); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.BillOfMaterials).WithMany().HasForeignKey(x => x.BillOfMaterialsId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.SourceWarehouse).WithMany().HasForeignKey(x => x.SourceWarehouseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.DestinationWarehouse).WithMany().HasForeignKey(x => x.DestinationWarehouseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.StartedByUser).WithMany().HasForeignKey(x => x.StartedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CompletedByUser).WithMany().HasForeignKey(x => x.CompletedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.Status, x.CreatedAt }); e.HasIndex(x => new { x.CreatedAt, x.Id });
            e.ToTable(t => {
                t.HasCheckConstraint("CK_ProductionOrders_Quantity", "\"PlannedQuantity\" > 0 AND \"ProducedQuantity\" >= 0 AND \"ProducedQuantity\" <= \"PlannedQuantity\"");
                t.HasCheckConstraint("CK_ProductionOrders_Status", "\"Status\" IN ('Draft','Released','InProgress','Completed','Cancelled')");
                t.HasCheckConstraint("CK_ProductionOrders_Dates", "\"PlannedEndDate\" IS NULL OR \"PlannedStartDate\" IS NULL OR \"PlannedEndDate\" >= \"PlannedStartDate\"");
                t.HasCheckConstraint("CK_ProductionOrders_Start", "(\"Status\" IN ('InProgress','Completed')) = (\"StartedAt\" IS NOT NULL AND \"StartedByUserId\" IS NOT NULL)");
                t.HasCheckConstraint("CK_ProductionOrders_Completion", "(\"Status\" = 'Completed' AND \"ProducedQuantity\" = \"PlannedQuantity\" AND \"CompletedAt\" IS NOT NULL AND \"CompletedByUserId\" IS NOT NULL) OR (\"Status\" <> 'Completed' AND \"ProducedQuantity\" = 0 AND \"CompletedAt\" IS NULL AND \"CompletedByUserId\" IS NULL)");
            });
        });
        model.Entity<ProductionOrderMaterial>(e => {
            e.HasOne(x => x.ProductionOrder).WithMany(x => x.Materials).HasForeignKey(x => x.ProductionOrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.MaterialProduct).WithMany().HasForeignKey(x => x.MaterialProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ProductionOrderId, x.MaterialProductId }).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("CK_ProductionOrderMaterials_Quantity", "\"QuantityRequired\" > 0 AND \"QuantityConsumed\" >= 0 AND \"QuantityConsumed\" <= \"QuantityRequired\""));
        });
        model.Entity<InventoryMovement>(e => {
            e.HasOne(x => x.ProductionOrder).WithMany().HasForeignKey(x => x.ProductionOrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ProductionOrderId, x.ProductId, x.MovementType }).IsUnique().HasFilter("\"ProductionOrderId\" IS NOT NULL");
        });
    }
}
