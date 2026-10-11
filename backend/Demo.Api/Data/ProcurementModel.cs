using Demo.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Demo.Api.Data;

internal static class ProcurementModel
{
    internal static void Configure(ModelBuilder model)
    {
        model.HasSequence<long>("PurchaseOrderNumbers"); model.HasSequence<long>("GoodsReceiptNumbers");
        model.Entity<Supplier>(e => {
            e.Property(x => x.SupplierCode).HasMaxLength(40).IsRequired(); e.HasIndex(x => x.SupplierCode).IsUnique();
            e.Property(x => x.Name).HasMaxLength(150).IsRequired(); e.Property(x => x.ContactPerson).HasMaxLength(150).IsRequired();
            e.Property(x => x.Email).HasMaxLength(254).IsRequired(); e.Property(x => x.Phone).HasMaxLength(40).IsRequired();
            e.Property(x => x.Address).HasMaxLength(1000).IsRequired(); e.Property(x => x.TaxNumber).HasMaxLength(40).IsRequired();
            e.ToTable(t => { t.HasCheckConstraint("CK_Suppliers_Code", "\"SupplierCode\" ~ '^[A-Z0-9][A-Z0-9_-]{0,39}$'"); t.HasCheckConstraint("CK_Suppliers_Name", "length(btrim(\"Name\")) > 0"); });
        });
        model.Entity<PurchaseOrder>(e => {
            e.Property(x => x.PurchaseOrderNumber).HasMaxLength(50).IsRequired(); e.HasIndex(x => x.PurchaseOrderNumber).IsUnique();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.TotalAmount).HasPrecision(18, 2); e.Property(x => x.Notes).HasMaxLength(2000).IsRequired();
            e.HasOne(x => x.Supplier).WithMany(x => x.PurchaseOrders).HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.OrderDate, x.Id }); e.HasIndex(x => new { x.Status, x.OrderDate });
            e.ToTable(t => { t.HasCheckConstraint("CK_PurchaseOrders_Total", "\"TotalAmount\" >= 0");
                t.HasCheckConstraint("CK_PurchaseOrders_Status", "\"Status\" IN ('Draft','Submitted','PartiallyReceived','Received','Cancelled')"); });
        });
        model.Entity<PurchaseOrderItem>(e => {
            e.Property(x => x.UnitCost).HasPrecision(12, 2); e.Property(x => x.LineTotal).HasPrecision(18, 2);
            e.HasOne(x => x.PurchaseOrder).WithMany(x => x.Items).HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.PurchaseOrderId, x.ProductId }).IsUnique();
            e.ToTable(t => { t.HasCheckConstraint("CK_PurchaseOrderItems_Quantities", "\"QuantityOrdered\" > 0 AND \"QuantityReceived\" >= 0 AND \"QuantityReceived\" <= \"QuantityOrdered\"");
                t.HasCheckConstraint("CK_PurchaseOrderItems_Cost", "\"UnitCost\" >= 0 AND \"LineTotal\" = \"QuantityOrdered\" * \"UnitCost\""); });
        });
        model.Entity<GoodsReceipt>(e => {
            e.Property(x => x.ReceiptNumber).HasMaxLength(50).IsRequired(); e.HasIndex(x => x.ReceiptNumber).IsUnique();
            e.Property(x => x.Notes).HasMaxLength(1000).IsRequired(); e.Property(x => x.PayloadFingerprint).HasMaxLength(64).IsRequired();
            e.HasIndex(x => new { x.CreatedByUserId, x.IdempotencyKey }).IsUnique();
            e.HasOne(x => x.PurchaseOrder).WithMany(x => x.Receipts).HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.PurchaseOrderId, x.ReceivedAt });
        });
        model.Entity<GoodsReceiptItem>(e => {
            e.HasOne(x => x.GoodsReceipt).WithMany(x => x.Items).HasForeignKey(x => x.GoodsReceiptId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.PurchaseOrderItem).WithMany().HasForeignKey(x => x.PurchaseOrderItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.GoodsReceiptId, x.PurchaseOrderItemId }).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("CK_GoodsReceiptItems_Quantity", "\"QuantityReceived\" > 0"));
        });
    }
}
