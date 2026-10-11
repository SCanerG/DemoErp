using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Demo.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Manufacturing : Migration
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
                name: "ProductionNumbers");

            migrationBuilder.AddColumn<string>(
                name: "ProductType",
                table: "Products",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "RawMaterial");

            migrationBuilder.AddColumn<Guid>(
                name: "ProductionOrderId",
                table: "InventoryMovements",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BillOfMaterials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    BaseQuantity = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ActivatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillOfMaterials", x => x.Id);
                    table.CheckConstraint("CK_BillOfMaterials_Active", "NOT \"IsActive\" OR \"ActivatedAt\" IS NOT NULL");
                    table.CheckConstraint("CK_BillOfMaterials_Base", "\"BaseQuantity\" > 0 AND length(btrim(\"Version\")) > 0");
                    table.ForeignKey(
                        name: "FK_BillOfMaterials_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BillOfMaterials_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BillOfMaterialsItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BillOfMaterialsId = table.Column<Guid>(type: "uuid", nullable: false),
                    MaterialProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuantityRequired = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillOfMaterialsItems", x => x.Id);
                    table.CheckConstraint("CK_BillOfMaterialsItems_Quantity", "\"QuantityRequired\" > 0");
                    table.ForeignKey(
                        name: "FK_BillOfMaterialsItems_BillOfMaterials_BillOfMaterialsId",
                        column: x => x.BillOfMaterialsId,
                        principalTable: "BillOfMaterials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BillOfMaterialsItems_Products_MaterialProductId",
                        column: x => x.MaterialProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductionOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductionOrderNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    BillOfMaterialsId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlannedQuantity = table.Column<int>(type: "integer", nullable: false),
                    ProducedQuantity = table.Column<int>(type: "integer", nullable: false),
                    SourceWarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationWarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PlannedStartDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PlannedEndDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionOrders", x => x.Id);
                    table.CheckConstraint("CK_ProductionOrders_Completion", "(\"Status\" = 'Completed' AND \"ProducedQuantity\" = \"PlannedQuantity\" AND \"CompletedAt\" IS NOT NULL AND \"CompletedByUserId\" IS NOT NULL) OR (\"Status\" <> 'Completed' AND \"ProducedQuantity\" = 0 AND \"CompletedAt\" IS NULL AND \"CompletedByUserId\" IS NULL)");
                    table.CheckConstraint("CK_ProductionOrders_Dates", "\"PlannedEndDate\" IS NULL OR \"PlannedStartDate\" IS NULL OR \"PlannedEndDate\" >= \"PlannedStartDate\"");
                    table.CheckConstraint("CK_ProductionOrders_Quantity", "\"PlannedQuantity\" > 0 AND \"ProducedQuantity\" >= 0 AND \"ProducedQuantity\" <= \"PlannedQuantity\"");
                    table.CheckConstraint("CK_ProductionOrders_Start", "(\"Status\" IN ('InProgress','Completed')) = (\"StartedAt\" IS NOT NULL AND \"StartedByUserId\" IS NOT NULL)");
                    table.CheckConstraint("CK_ProductionOrders_Status", "\"Status\" IN ('Draft','Released','InProgress','Completed','Cancelled')");
                    table.ForeignKey(
                        name: "FK_ProductionOrders_BillOfMaterials_BillOfMaterialsId",
                        column: x => x.BillOfMaterialsId,
                        principalTable: "BillOfMaterials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionOrders_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionOrders_Users_CompletedByUserId",
                        column: x => x.CompletedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionOrders_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionOrders_Users_StartedByUserId",
                        column: x => x.StartedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionOrders_Warehouses_DestinationWarehouseId",
                        column: x => x.DestinationWarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionOrders_Warehouses_SourceWarehouseId",
                        column: x => x.SourceWarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductionOrderMaterials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    MaterialProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuantityRequired = table.Column<int>(type: "integer", nullable: false),
                    QuantityConsumed = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionOrderMaterials", x => x.Id);
                    table.CheckConstraint("CK_ProductionOrderMaterials_Quantity", "\"QuantityRequired\" > 0 AND \"QuantityConsumed\" >= 0 AND \"QuantityConsumed\" <= \"QuantityRequired\"");
                    table.ForeignKey(
                        name: "FK_ProductionOrderMaterials_ProductionOrders_ProductionOrderId",
                        column: x => x.ProductionOrderId,
                        principalTable: "ProductionOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionOrderMaterials_Products_MaterialProductId",
                        column: x => x.MaterialProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Products_Type",
                table: "Products",
                sql: "\"ProductType\" IN ('RawMaterial','FinishedGood')");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_ProductionOrderId_ProductId_MovementType",
                table: "InventoryMovements",
                columns: new[] { "ProductionOrderId", "ProductId", "MovementType" },
                unique: true,
                filter: "\"ProductionOrderId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryMovements_Delta",
                table: "InventoryMovements",
                sql: "(\"MovementType\" IN ('StockIn','AdjustmentIncrease','OrderCancellationReturn','PurchaseReceipt','TransferIn','ProductionOutput') AND \"QuantityAfter\"::bigint = \"QuantityBefore\"::bigint + \"Quantity\") OR (\"MovementType\" IN ('StockOut','AdjustmentDecrease','OrderDeduction','TransferOut','ProductionConsumption') AND \"QuantityAfter\"::bigint = \"QuantityBefore\"::bigint - \"Quantity\")");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryMovements_Reference",
                table: "InventoryMovements",
                sql: "\"ProductionOrderId\" IS NULL AND (\"StockTransferId\" IS NULL AND ((\"ReferenceType\" = 'Manual' AND \"ReferenceId\" IS NULL AND \"GoodsReceiptId\" IS NULL AND \"MovementType\" IN ('StockIn','StockOut','AdjustmentIncrease','AdjustmentDecrease')) OR (\"ReferenceType\" = 'Order' AND \"ReferenceId\" IS NOT NULL AND \"GoodsReceiptId\" IS NULL AND \"MovementType\" IN ('OrderDeduction','OrderCancellationReturn')) OR (\"ReferenceType\" = 'Receipt' AND \"ReferenceId\" IS NULL AND \"GoodsReceiptId\" IS NOT NULL AND \"MovementType\" = 'PurchaseReceipt')) OR (\"ReferenceType\" = 'Transfer' AND \"StockTransferId\" IS NOT NULL AND \"ReferenceId\" IS NULL AND \"GoodsReceiptId\" IS NULL AND \"MovementType\" IN ('TransferOut','TransferIn'))) OR (\"ProductionOrderId\" IS NOT NULL AND \"ReferenceType\" = 'Production' AND \"ReferenceId\" IS NULL AND \"GoodsReceiptId\" IS NULL AND \"StockTransferId\" IS NULL AND \"MovementType\" IN ('ProductionConsumption','ProductionOutput'))");

            migrationBuilder.CreateIndex(
                name: "IX_BillOfMaterials_CreatedAt_Id",
                table: "BillOfMaterials",
                columns: new[] { "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_BillOfMaterials_CreatedByUserId",
                table: "BillOfMaterials",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BillOfMaterials_ProductId",
                table: "BillOfMaterials",
                column: "ProductId",
                unique: true,
                filter: "\"IsActive\"");

            migrationBuilder.CreateIndex(
                name: "IX_BillOfMaterials_ProductId_Version",
                table: "BillOfMaterials",
                columns: new[] { "ProductId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillOfMaterialsItems_BillOfMaterialsId_MaterialProductId",
                table: "BillOfMaterialsItems",
                columns: new[] { "BillOfMaterialsId", "MaterialProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillOfMaterialsItems_MaterialProductId",
                table: "BillOfMaterialsItems",
                column: "MaterialProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrderMaterials_MaterialProductId",
                table: "ProductionOrderMaterials",
                column: "MaterialProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrderMaterials_ProductionOrderId_MaterialProductId",
                table: "ProductionOrderMaterials",
                columns: new[] { "ProductionOrderId", "MaterialProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_BillOfMaterialsId",
                table: "ProductionOrders",
                column: "BillOfMaterialsId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_CompletedByUserId",
                table: "ProductionOrders",
                column: "CompletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_CreatedAt_Id",
                table: "ProductionOrders",
                columns: new[] { "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_CreatedByUserId",
                table: "ProductionOrders",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_DestinationWarehouseId",
                table: "ProductionOrders",
                column: "DestinationWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_ProductId",
                table: "ProductionOrders",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_ProductionOrderNumber",
                table: "ProductionOrders",
                column: "ProductionOrderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_SourceWarehouseId",
                table: "ProductionOrders",
                column: "SourceWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_StartedByUserId",
                table: "ProductionOrders",
                column: "StartedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_Status_CreatedAt",
                table: "ProductionOrders",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryMovements_ProductionOrders_ProductionOrderId",
                table: "InventoryMovements",
                column: "ProductionOrderId",
                principalTable: "ProductionOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                CREATE FUNCTION manufacturing_product_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                 IF OLD."ProductType" <> NEW."ProductType" AND (
                 EXISTS(SELECT 1 FROM "BillOfMaterials" WHERE "ProductId"=OLD."Id") OR
                 EXISTS(SELECT 1 FROM "BillOfMaterialsItems" WHERE "MaterialProductId"=OLD."Id") OR
                 EXISTS(SELECT 1 FROM "ProductionOrders" WHERE "ProductId"=OLD."Id") OR
                 EXISTS(SELECT 1 FROM "ProductionOrderMaterials" WHERE "MaterialProductId"=OLD."Id")) THEN
                 RAISE EXCEPTION 'Referenced product classification is immutable' USING ERRCODE='23514'; END IF;
                 RETURN NEW;
                END $$;
                CREATE TRIGGER manufacturing_product_guard BEFORE UPDATE ON "Products" FOR EACH ROW EXECUTE FUNCTION manufacturing_product_guard();
                CREATE FUNCTION manufacturing_bom_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                 IF TG_OP='DELETE' THEN
                 IF OLD."ActivatedAt" IS NOT NULL OR EXISTS(SELECT 1 FROM "ProductionOrders" WHERE "BillOfMaterialsId"=OLD."Id") THEN RAISE EXCEPTION 'BOM history cannot be deleted' USING ERRCODE='23514'; END IF; RETURN OLD; END IF;
                 IF OLD."ActivatedAt" IS NOT NULL AND (to_jsonb(OLD)-'IsActive'-'UpdatedAt') IS DISTINCT FROM (to_jsonb(NEW)-'IsActive'-'UpdatedAt') THEN
                 RAISE EXCEPTION 'Activated BOM is immutable' USING ERRCODE='23514'; END IF;
                 IF OLD."ActivatedAt" IS NOT NULL AND NOT OLD."IsActive" AND NEW."IsActive" THEN
                 RAISE EXCEPTION 'Retired BOM cannot be activated' USING ERRCODE='23514'; END IF;
                 RETURN NEW;
                END $$;
                CREATE TRIGGER manufacturing_bom_guard BEFORE UPDATE OR DELETE ON "BillOfMaterials" FOR EACH ROW EXECUTE FUNCTION manufacturing_bom_guard();
                CREATE FUNCTION manufacturing_bom_item_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE b "BillOfMaterials"%ROWTYPE;
                BEGIN
                 SELECT * INTO b FROM "BillOfMaterials" WHERE "Id"=COALESCE(NEW."BillOfMaterialsId",OLD."BillOfMaterialsId") FOR UPDATE;
                 IF b."ActivatedAt" IS NOT NULL THEN RAISE EXCEPTION 'Activated recipe is immutable' USING ERRCODE='23514'; END IF;
                 IF TG_OP='UPDATE' AND (OLD."BillOfMaterialsId"<>NEW."BillOfMaterialsId" OR OLD."MaterialProductId"<>NEW."MaterialProductId") THEN
                 RAISE EXCEPTION 'Recipe identity is immutable' USING ERRCODE='23514'; END IF;
                 IF TG_OP='DELETE' THEN RETURN OLD; END IF;
                 IF NEW."MaterialProductId"=b."ProductId" OR NOT EXISTS(SELECT 1 FROM "Products" WHERE "Id"=NEW."MaterialProductId" AND "ProductType"='RawMaterial') THEN
                 RAISE EXCEPTION 'Invalid material classification' USING ERRCODE='23514'; END IF;
                 RETURN NEW;
                END $$;
                CREATE TRIGGER manufacturing_bom_item_guard BEFORE INSERT OR UPDATE OR DELETE ON "BillOfMaterialsItems" FOR EACH ROW EXECUTE FUNCTION manufacturing_bom_item_guard();
                CREATE FUNCTION manufacturing_bom_valid() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE target uuid; b "BillOfMaterials"%ROWTYPE;
                BEGIN
                 IF TG_TABLE_NAME='BillOfMaterials' THEN target:=COALESCE(NEW."Id",OLD."Id"); ELSE target:=COALESCE(NEW."BillOfMaterialsId",OLD."BillOfMaterialsId"); END IF;
                 SELECT * INTO b FROM "BillOfMaterials" WHERE "Id"=target;
                 IF FOUND AND (NOT EXISTS(SELECT 1 FROM "Products" WHERE "Id"=b."ProductId" AND "ProductType"='FinishedGood') OR
                 NOT EXISTS(SELECT 1 FROM "BillOfMaterialsItems" WHERE "BillOfMaterialsId"=target)) THEN
                 RAISE EXCEPTION 'Recipe requires finished product and materials' USING ERRCODE='23514'; END IF;
                 RETURN NULL;
                END $$;
                CREATE CONSTRAINT TRIGGER manufacturing_bom_valid AFTER INSERT OR UPDATE ON "BillOfMaterials" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION manufacturing_bom_valid();
                CREATE CONSTRAINT TRIGGER manufacturing_bom_items_valid AFTER INSERT OR UPDATE OR DELETE ON "BillOfMaterialsItems" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION manufacturing_bom_valid();
                CREATE FUNCTION manufacturing_order_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                 IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Production history cannot be deleted' USING ERRCODE='23514'; END IF;
                 IF OLD."Status" IN ('Completed','Cancelled') THEN RAISE EXCEPTION 'Finalized production is immutable' USING ERRCODE='23514'; END IF;
                 IF OLD."Status"<>'Draft' AND (to_jsonb(OLD)-'Status'-'ProducedQuantity'-'StartedAt'-'StartedByUserId'-'CompletedAt'-'CompletedByUserId'-'UpdatedAt') IS DISTINCT FROM
                 (to_jsonb(NEW)-'Status'-'ProducedQuantity'-'StartedAt'-'StartedByUserId'-'CompletedAt'-'CompletedByUserId'-'UpdatedAt') THEN
                 RAISE EXCEPTION 'Released production snapshot is immutable' USING ERRCODE='23514'; END IF;
                 IF OLD."Status"<>NEW."Status" AND NOT ((OLD."Status"='Draft' AND NEW."Status" IN ('Released','Cancelled')) OR
                 (OLD."Status"='Released' AND NEW."Status" IN ('InProgress','Cancelled')) OR (OLD."Status"='InProgress' AND NEW."Status"='Completed')) THEN
                 RAISE EXCEPTION 'Invalid production transition' USING ERRCODE='23514'; END IF;
                 RETURN NEW;
                END $$;
                CREATE TRIGGER manufacturing_order_guard BEFORE UPDATE OR DELETE ON "ProductionOrders" FOR EACH ROW EXECUTE FUNCTION manufacturing_order_guard();
                CREATE FUNCTION manufacturing_material_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE state text;
                BEGIN
                 SELECT "Status" INTO state FROM "ProductionOrders" WHERE "Id"=COALESCE(NEW."ProductionOrderId",OLD."ProductionOrderId") FOR UPDATE;
                 IF TG_OP='UPDATE' THEN
                 IF OLD."ProductionOrderId"<>NEW."ProductionOrderId" OR OLD."MaterialProductId"<>NEW."MaterialProductId" THEN RAISE EXCEPTION 'Material identity is immutable' USING ERRCODE='23514'; END IF;
                 IF state<>'Draft' AND ((to_jsonb(OLD)-'QuantityConsumed') IS DISTINCT FROM (to_jsonb(NEW)-'QuantityConsumed') OR OLD."QuantityConsumed"<>0 OR NEW."QuantityConsumed"<>NEW."QuantityRequired" OR state NOT IN ('Released','InProgress')) THEN
                 RAISE EXCEPTION 'Consumed snapshot is immutable' USING ERRCODE='23514'; END IF;
                 ELSIF state<>'Draft' THEN RAISE EXCEPTION 'Material snapshot is immutable' USING ERRCODE='23514'; END IF;
                 IF TG_OP='DELETE' THEN RETURN OLD; END IF; RETURN NEW;
                END $$;
                CREATE TRIGGER manufacturing_material_guard BEFORE INSERT OR UPDATE OR DELETE ON "ProductionOrderMaterials" FOR EACH ROW EXECUTE FUNCTION manufacturing_material_guard();
                CREATE FUNCTION manufacturing_order_valid() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE target uuid; o "ProductionOrders"%ROWTYPE; b "BillOfMaterials"%ROWTYPE;
                BEGIN
                 IF TG_TABLE_NAME='ProductionOrders' THEN target:=COALESCE(NEW."Id",OLD."Id"); ELSE target:=COALESCE(NEW."ProductionOrderId",OLD."ProductionOrderId"); END IF;
                 SELECT * INTO o FROM "ProductionOrders" WHERE "Id"=target;
                 IF NOT FOUND THEN RETURN NULL; END IF;
                 SELECT * INTO b FROM "BillOfMaterials" WHERE "Id"=o."BillOfMaterialsId";
                 IF b."ProductId"<>o."ProductId" OR b."ActivatedAt" IS NULL OR NOT EXISTS(SELECT 1 FROM "ProductionOrderMaterials" WHERE "ProductionOrderId"=target) OR
                 (SELECT count(*) FROM "ProductionOrderMaterials" WHERE "ProductionOrderId"=target)<>(SELECT count(*) FROM "BillOfMaterialsItems" WHERE "BillOfMaterialsId"=b."Id") OR
                 EXISTS(SELECT 1 FROM "ProductionOrderMaterials" m LEFT JOIN "BillOfMaterialsItems" i ON i."BillOfMaterialsId"=b."Id" AND i."MaterialProductId"=m."MaterialProductId"
                 WHERE m."ProductionOrderId"=target AND (i."Id" IS NULL OR i."QuantityRequired"::bigint*o."PlannedQuantity"<>m."QuantityRequired"::bigint*b."BaseQuantity")) THEN
                 RAISE EXCEPTION 'Invalid production snapshot' USING ERRCODE='23514'; END IF;
                 IF o."Status" IN ('InProgress','Completed') THEN
                 IF EXISTS(SELECT 1 FROM "ProductionOrderMaterials" m WHERE m."ProductionOrderId"=target AND (m."QuantityConsumed"<>m."QuantityRequired" OR
                 NOT EXISTS(SELECT 1 FROM "InventoryMovements" v WHERE v."ProductionOrderId"=target AND v."ProductId"=m."MaterialProductId" AND v."WarehouseId"=o."SourceWarehouseId" AND v."MovementType"='ProductionConsumption' AND v."Quantity"=m."QuantityRequired"))) THEN
                 RAISE EXCEPTION 'Production consumption missing' USING ERRCODE='23514'; END IF;
                 ELSE
                 IF EXISTS(SELECT 1 FROM "ProductionOrderMaterials" WHERE "ProductionOrderId"=target AND "QuantityConsumed"<>0) OR EXISTS(SELECT 1 FROM "InventoryMovements" WHERE "ProductionOrderId"=target) THEN
                 RAISE EXCEPTION 'Unstarted production cannot affect inventory' USING ERRCODE='23514'; END IF;
                 END IF;
                 IF o."Status"='Completed' AND NOT EXISTS(SELECT 1 FROM "InventoryMovements" WHERE "ProductionOrderId"=target AND "ProductId"=o."ProductId" AND "WarehouseId"=o."DestinationWarehouseId" AND "MovementType"='ProductionOutput' AND "Quantity"=o."PlannedQuantity") THEN
                 RAISE EXCEPTION 'Production output missing' USING ERRCODE='23514'; END IF;
                 IF EXISTS(SELECT 1 FROM "InventoryMovements" v WHERE v."ProductionOrderId"=target AND (
                 (v."MovementType"='ProductionOutput' AND (o."Status"<>'Completed' OR v."ProductId"<>o."ProductId" OR v."WarehouseId"<>o."DestinationWarehouseId" OR v."Quantity"<>o."PlannedQuantity")) OR
                 (v."MovementType"='ProductionConsumption' AND NOT EXISTS(SELECT 1 FROM "ProductionOrderMaterials" m WHERE m."ProductionOrderId"=target AND m."MaterialProductId"=v."ProductId" AND m."QuantityRequired"=v."Quantity" AND v."WarehouseId"=o."SourceWarehouseId")))) THEN
                 RAISE EXCEPTION 'Invalid manufacturing movement' USING ERRCODE='23514'; END IF;
                 RETURN NULL;
                END $$;
                CREATE CONSTRAINT TRIGGER manufacturing_order_valid AFTER INSERT OR UPDATE ON "ProductionOrders" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION manufacturing_order_valid();
                CREATE CONSTRAINT TRIGGER manufacturing_material_valid AFTER INSERT OR UPDATE OR DELETE ON "ProductionOrderMaterials" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION manufacturing_order_valid();
                CREATE CONSTRAINT TRIGGER manufacturing_movement_valid AFTER INSERT ON "InventoryMovements" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW WHEN (NEW."ProductionOrderId" IS NOT NULL) EXECUTE FUNCTION manufacturing_order_valid();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Manufacturing history cannot be safely downgraded. Restore a verified backup or apply a forward migration.");
        }
    }
}
