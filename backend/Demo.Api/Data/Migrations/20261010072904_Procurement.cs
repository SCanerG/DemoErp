using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Demo.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Procurement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryMovements_Delta",
                table: "InventoryMovements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryMovements_Reference",
                table: "InventoryMovements");

            migrationBuilder.CreateSequence(
                name: "GoodsReceiptNumbers");

            migrationBuilder.CreateSequence(
                name: "PurchaseOrderNumbers");

            migrationBuilder.AddColumn<Guid>(
                name: "GoodsReceiptId",
                table: "InventoryMovements",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Suppliers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    ContactPerson = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Address = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    TaxNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Suppliers", x => x.Id);
                    table.CheckConstraint("CK_Suppliers_Code", "\"SupplierCode\" ~ '^[A-Z0-9][A-Z0-9_-]{0,39}$'");
                    table.CheckConstraint("CK_Suppliers_Name", "length(btrim(\"Name\")) > 0");
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OrderDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpectedDeliveryDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrders", x => x.Id);
                    table.CheckConstraint("CK_PurchaseOrders_Status", "\"Status\" IN ('Draft','Submitted','PartiallyReceived','Received','Cancelled')");
                    table.CheckConstraint("CK_PurchaseOrders_Total", "\"TotalAmount\" >= 0");
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GoodsReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uuid", nullable: false),
                    PayloadFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoodsReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GoodsReceipts_PurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GoodsReceipts_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrderItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuantityOrdered = table.Column<int>(type: "integer", nullable: false),
                    QuantityReceived = table.Column<int>(type: "integer", nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    LineTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrderItems", x => x.Id);
                    table.CheckConstraint("CK_PurchaseOrderItems_Cost", "\"UnitCost\" >= 0 AND \"LineTotal\" = \"QuantityOrdered\" * \"UnitCost\"");
                    table.CheckConstraint("CK_PurchaseOrderItems_Quantities", "\"QuantityOrdered\" > 0 AND \"QuantityReceived\" >= 0 AND \"QuantityReceived\" <= \"QuantityOrdered\"");
                    table.ForeignKey(
                        name: "FK_PurchaseOrderItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderItems_PurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GoodsReceiptItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GoodsReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuantityReceived = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoodsReceiptItems", x => x.Id);
                    table.CheckConstraint("CK_GoodsReceiptItems_Quantity", "\"QuantityReceived\" > 0");
                    table.ForeignKey(
                        name: "FK_GoodsReceiptItems_GoodsReceipts_GoodsReceiptId",
                        column: x => x.GoodsReceiptId,
                        principalTable: "GoodsReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GoodsReceiptItems_PurchaseOrderItems_PurchaseOrderItemId",
                        column: x => x.PurchaseOrderItemId,
                        principalTable: "PurchaseOrderItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_GoodsReceiptId_ProductId",
                table: "InventoryMovements",
                columns: new[] { "GoodsReceiptId", "ProductId" },
                unique: true,
                filter: "\"GoodsReceiptId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryMovements_Delta",
                table: "InventoryMovements",
                sql: "(\"MovementType\" IN ('StockIn','AdjustmentIncrease','OrderCancellationReturn','PurchaseReceipt') AND \"QuantityAfter\"::bigint = \"QuantityBefore\"::bigint + \"Quantity\") OR (\"MovementType\" IN ('StockOut','AdjustmentDecrease','OrderDeduction') AND \"QuantityAfter\"::bigint = \"QuantityBefore\"::bigint - \"Quantity\")");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryMovements_Reference",
                table: "InventoryMovements",
                sql: "(\"ReferenceType\" = 'Manual' AND \"ReferenceId\" IS NULL AND \"GoodsReceiptId\" IS NULL AND \"MovementType\" IN ('StockIn','StockOut','AdjustmentIncrease','AdjustmentDecrease')) OR (\"ReferenceType\" = 'Order' AND \"ReferenceId\" IS NOT NULL AND \"GoodsReceiptId\" IS NULL AND \"MovementType\" IN ('OrderDeduction','OrderCancellationReturn')) OR (\"ReferenceType\" = 'Receipt' AND \"ReferenceId\" IS NULL AND \"GoodsReceiptId\" IS NOT NULL AND \"MovementType\" = 'PurchaseReceipt')");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptItems_GoodsReceiptId_PurchaseOrderItemId",
                table: "GoodsReceiptItems",
                columns: new[] { "GoodsReceiptId", "PurchaseOrderItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptItems_PurchaseOrderItemId",
                table: "GoodsReceiptItems",
                column: "PurchaseOrderItemId");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceipts_CreatedByUserId_IdempotencyKey",
                table: "GoodsReceipts",
                columns: new[] { "CreatedByUserId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceipts_PurchaseOrderId_ReceivedAt",
                table: "GoodsReceipts",
                columns: new[] { "PurchaseOrderId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceipts_ReceiptNumber",
                table: "GoodsReceipts",
                column: "ReceiptNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderItems_ProductId",
                table: "PurchaseOrderItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderItems_PurchaseOrderId_ProductId",
                table: "PurchaseOrderItems",
                columns: new[] { "PurchaseOrderId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_CreatedByUserId",
                table: "PurchaseOrders",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_OrderDate_Id",
                table: "PurchaseOrders",
                columns: new[] { "OrderDate", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_PurchaseOrderNumber",
                table: "PurchaseOrders",
                column: "PurchaseOrderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_Status_OrderDate",
                table: "PurchaseOrders",
                columns: new[] { "Status", "OrderDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_SupplierId",
                table: "PurchaseOrders",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_SupplierCode",
                table: "Suppliers",
                column: "SupplierCode",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryMovements_GoodsReceipts_GoodsReceiptId",
                table: "InventoryMovements",
                column: "GoodsReceiptId",
                principalTable: "GoodsReceipts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                CREATE FUNCTION immutable_goods_receipt() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'Posted goods receipts are immutable' USING ERRCODE = '23514'; END; $$;
                CREATE TRIGGER goods_receipts_immutable BEFORE UPDATE OR DELETE ON "GoodsReceipts"
                  FOR EACH ROW EXECUTE FUNCTION immutable_goods_receipt();
                CREATE TRIGGER goods_receipt_items_immutable BEFORE UPDATE OR DELETE ON "GoodsReceiptItems"
                  FOR EACH ROW EXECUTE FUNCTION immutable_goods_receipt();
                CREATE FUNCTION check_receipt_item_order() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF NOT EXISTS (SELECT 1 FROM "GoodsReceipts" r JOIN "PurchaseOrderItems" i ON i."PurchaseOrderId" = r."PurchaseOrderId"
                     WHERE r."Id" = NEW."GoodsReceiptId" AND i."Id" = NEW."PurchaseOrderItemId") THEN
                    RAISE EXCEPTION 'Receipt item belongs to another order' USING ERRCODE = '23514';
                  END IF;
                  RETURN NEW;
                END; $$;
                CREATE TRIGGER goods_receipt_item_order BEFORE INSERT ON "GoodsReceiptItems"
                  FOR EACH ROW EXECUTE FUNCTION check_receipt_item_order();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER goods_receipts_immutable ON "GoodsReceipts";
                DROP TRIGGER goods_receipt_items_immutable ON "GoodsReceiptItems";
                DROP TRIGGER goods_receipt_item_order ON "GoodsReceiptItems";
                DROP FUNCTION immutable_goods_receipt();
                DROP FUNCTION check_receipt_item_order();
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_InventoryMovements_GoodsReceipts_GoodsReceiptId",
                table: "InventoryMovements");

            migrationBuilder.DropTable(
                name: "GoodsReceiptItems");

            migrationBuilder.DropTable(
                name: "GoodsReceipts");

            migrationBuilder.DropTable(
                name: "PurchaseOrderItems");

            migrationBuilder.DropTable(
                name: "PurchaseOrders");

            migrationBuilder.DropTable(
                name: "Suppliers");

            migrationBuilder.DropIndex(
                name: "IX_InventoryMovements_GoodsReceiptId_ProductId",
                table: "InventoryMovements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryMovements_Delta",
                table: "InventoryMovements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryMovements_Reference",
                table: "InventoryMovements");

            migrationBuilder.DropColumn(
                name: "GoodsReceiptId",
                table: "InventoryMovements");

            migrationBuilder.DropSequence(
                name: "GoodsReceiptNumbers");

            migrationBuilder.DropSequence(
                name: "PurchaseOrderNumbers");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryMovements_Delta",
                table: "InventoryMovements",
                sql: "(\"MovementType\" IN ('StockIn','AdjustmentIncrease','OrderCancellationReturn') AND \"QuantityAfter\"::bigint = \"QuantityBefore\"::bigint + \"Quantity\") OR (\"MovementType\" IN ('StockOut','AdjustmentDecrease','OrderDeduction') AND \"QuantityAfter\"::bigint = \"QuantityBefore\"::bigint - \"Quantity\")");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryMovements_Reference",
                table: "InventoryMovements",
                sql: "(\"ReferenceType\" = 'Manual' AND \"ReferenceId\" IS NULL AND \"MovementType\" IN ('StockIn','StockOut','AdjustmentIncrease','AdjustmentDecrease')) OR (\"ReferenceType\" = 'Order' AND \"ReferenceId\" IS NOT NULL AND \"MovementType\" IN ('OrderDeduction','OrderCancellationReturn'))");
        }
    }
}
