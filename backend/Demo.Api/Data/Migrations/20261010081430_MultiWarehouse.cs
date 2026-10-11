using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Demo.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class MultiWarehouse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE \"Inventories\" RENAME TO \"LegacyInventories\";");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryMovements_Delta",
                table: "InventoryMovements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryMovements_Reference",
                table: "InventoryMovements");

            migrationBuilder.CreateSequence(
                name: "TransferNumbers");

            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseId",
                table: "Orders",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("80000000-0000-0000-0000-000000000001"));

            migrationBuilder.AddColumn<Guid>(
                name: "StockTransferId",
                table: "InventoryMovements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseId",
                table: "InventoryMovements",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("80000000-0000-0000-0000-000000000001"));

            migrationBuilder.AddColumn<int>(
                name: "FingerprintVersion",
                table: "GoodsReceipts",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseId",
                table: "GoodsReceipts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("80000000-0000-0000-0000-000000000001"));

            migrationBuilder.CreateTable(
                name: "Warehouses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Location = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Warehouses", x => x.Id);
                    table.CheckConstraint("CK_Warehouses_Code", "\"Code\" ~ '^[A-Z0-9][A-Z0-9_-]{0,39}$'");
                    table.CheckConstraint("CK_Warehouses_DefaultActive", "NOT \"IsDefault\" OR \"IsActive\"");
                    table.CheckConstraint("CK_Warehouses_Name", "length(btrim(\"Name\")) > 0");
                });

            migrationBuilder.CreateTable(
                name: "StockTransfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransferNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SourceWarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationWarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockTransfers", x => x.Id);
                    table.CheckConstraint("CK_StockTransfers_Completion", "(\"Status\" = 'Completed') = (\"CompletedAt\" IS NOT NULL AND \"CompletedByUserId\" IS NOT NULL)");
                    table.CheckConstraint("CK_StockTransfers_Status", "\"Status\" IN ('Draft','Completed','Cancelled')");
                    table.CheckConstraint("CK_StockTransfers_Warehouses", "\"SourceWarehouseId\" <> \"DestinationWarehouseId\"");
                    table.ForeignKey(
                        name: "FK_StockTransfers_Users_CompletedByUserId",
                        column: x => x.CompletedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransfers_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransfers_Warehouses_DestinationWarehouseId",
                        column: x => x.DestinationWarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransfers_Warehouses_SourceWarehouseId",
                        column: x => x.SourceWarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WarehouseInventories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuantityOnHand = table.Column<int>(type: "integer", nullable: false),
                    MinimumStockLevel = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WarehouseInventories", x => x.Id);
                    table.CheckConstraint("CK_WarehouseInventories_Levels", "\"QuantityOnHand\" >= 0 AND \"MinimumStockLevel\" >= 0");
                    table.ForeignKey(
                        name: "FK_WarehouseInventories_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WarehouseInventories_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockTransferItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StockTransferId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockTransferItems", x => x.Id);
                    table.CheckConstraint("CK_StockTransferItems_Quantity", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_StockTransferItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransferItems_StockTransfers_StockTransferId",
                        column: x => x.StockTransferId,
                        principalTable: "StockTransfers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_WarehouseId",
                table: "Orders",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_StockTransferId_ProductId_MovementType",
                table: "InventoryMovements",
                columns: new[] { "StockTransferId", "ProductId", "MovementType" },
                unique: true,
                filter: "\"StockTransferId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_WarehouseId_CreatedAt_Id",
                table: "InventoryMovements",
                columns: new[] { "WarehouseId", "CreatedAt", "Id" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryMovements_Delta",
                table: "InventoryMovements",
                sql: "(\"MovementType\" IN ('StockIn','AdjustmentIncrease','OrderCancellationReturn','PurchaseReceipt','TransferIn') AND \"QuantityAfter\"::bigint = \"QuantityBefore\"::bigint + \"Quantity\") OR (\"MovementType\" IN ('StockOut','AdjustmentDecrease','OrderDeduction','TransferOut') AND \"QuantityAfter\"::bigint = \"QuantityBefore\"::bigint - \"Quantity\")");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryMovements_Reference",
                table: "InventoryMovements",
                sql: "\"StockTransferId\" IS NULL AND ((\"ReferenceType\" = 'Manual' AND \"ReferenceId\" IS NULL AND \"GoodsReceiptId\" IS NULL AND \"MovementType\" IN ('StockIn','StockOut','AdjustmentIncrease','AdjustmentDecrease')) OR (\"ReferenceType\" = 'Order' AND \"ReferenceId\" IS NOT NULL AND \"GoodsReceiptId\" IS NULL AND \"MovementType\" IN ('OrderDeduction','OrderCancellationReturn')) OR (\"ReferenceType\" = 'Receipt' AND \"ReferenceId\" IS NULL AND \"GoodsReceiptId\" IS NOT NULL AND \"MovementType\" = 'PurchaseReceipt')) OR (\"ReferenceType\" = 'Transfer' AND \"StockTransferId\" IS NOT NULL AND \"ReferenceId\" IS NULL AND \"GoodsReceiptId\" IS NULL AND \"MovementType\" IN ('TransferOut','TransferIn'))");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceipts_WarehouseId",
                table: "GoodsReceipts",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferItems_ProductId",
                table: "StockTransferItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferItems_StockTransferId_ProductId",
                table: "StockTransferItems",
                columns: new[] { "StockTransferId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_CompletedByUserId",
                table: "StockTransfers",
                column: "CompletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_CreatedAt_Id",
                table: "StockTransfers",
                columns: new[] { "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_CreatedByUserId",
                table: "StockTransfers",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_DestinationWarehouseId",
                table: "StockTransfers",
                column: "DestinationWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_SourceWarehouseId",
                table: "StockTransfers",
                column: "SourceWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_Status_CreatedAt",
                table: "StockTransfers",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_TransferNumber",
                table: "StockTransfers",
                column: "TransferNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseInventories_ProductId",
                table: "WarehouseInventories",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseInventories_WarehouseId_ProductId",
                table: "WarehouseInventories",
                columns: new[] { "WarehouseId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_Code",
                table: "Warehouses",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_IsDefault",
                table: "Warehouses",
                column: "IsDefault",
                unique: true,
                filter: "\"IsDefault\"");

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_Name_Id",
                table: "Warehouses",
                columns: new[] { "Name", "Id" });

            // Preserve opening balances and existing history; no synthetic movement is inserted.
            migrationBuilder.Sql("""
                INSERT INTO "Warehouses" ("Id","Code","Name","Description","Location","IsActive","IsDefault","CreatedAt")
                VALUES ('80000000-0000-0000-0000-000000000001','MAIN','Main warehouse','Legacy stock opening balance','',true,true,CURRENT_TIMESTAMP);
                INSERT INTO "WarehouseInventories" ("Id","WarehouseId","ProductId","QuantityOnHand","MinimumStockLevel","UpdatedAt")
                SELECT "Id",'80000000-0000-0000-0000-000000000001',"ProductId","QuantityOnHand","MinimumStockLevel","UpdatedAt" FROM "LegacyInventories";
                ALTER TABLE "Orders" ALTER COLUMN "WarehouseId" DROP DEFAULT;
                ALTER TABLE "GoodsReceipts" ALTER COLUMN "WarehouseId" DROP DEFAULT, ALTER COLUMN "FingerprintVersion" DROP DEFAULT;
                ALTER TABLE "InventoryMovements" ALTER COLUMN "WarehouseId" DROP DEFAULT;
                CREATE VIEW "Inventories" AS SELECT l."Id",l."ProductId",
                    COALESCE(SUM(i."QuantityOnHand"),0)::integer AS "QuantityOnHand",
                    COALESCE(SUM(i."MinimumStockLevel"),0)::integer AS "MinimumStockLevel",
                    COALESCE(MAX(i."UpdatedAt"),l."UpdatedAt") AS "UpdatedAt"
                    FROM "LegacyInventories" l LEFT JOIN "WarehouseInventories" i ON i."ProductId"=l."ProductId"
                    GROUP BY l."Id",l."ProductId",l."UpdatedAt";
                CREATE OR REPLACE FUNCTION initialize_inventory() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE balance uuid := gen_random_uuid(); warehouse uuid;
                BEGIN
                    SELECT "Id" INTO STRICT warehouse FROM "Warehouses" WHERE "IsDefault";
                    INSERT INTO "LegacyInventories" VALUES (balance,NEW."Id",0,0,CURRENT_TIMESTAMP);
                    INSERT INTO "WarehouseInventories" ("Id","WarehouseId","ProductId","QuantityOnHand","MinimumStockLevel","UpdatedAt")
                    VALUES (balance,warehouse,NEW."Id",0,0,CURRENT_TIMESTAMP);
                    RETURN NEW;
                END; $$;
                CREATE FUNCTION freeze_legacy_inventory() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'Opening balances are immutable' USING ERRCODE='23514'; END; $$;
                CREATE TRIGGER "FreezeLegacyInventory" BEFORE UPDATE ON "LegacyInventories" FOR EACH ROW EXECUTE FUNCTION freeze_legacy_inventory();
                CREATE FUNCTION lock_warehouse_inventory_product() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP='UPDATE' AND (NEW."ProductId"<>OLD."ProductId" OR NEW."WarehouseId"<>OLD."WarehouseId" OR NEW."Id"<>OLD."Id") THEN
                        RAISE EXCEPTION 'Balance identity is immutable' USING ERRCODE='23514'; END IF;
                    IF TG_OP='DELETE' THEN
                        IF EXISTS(SELECT 1 FROM "Products" WHERE "Id"=OLD."ProductId") THEN
                            RAISE EXCEPTION 'Product balance cannot be removed' USING ERRCODE='23514'; END IF;
                        RETURN OLD;
                    END IF;
                    PERFORM 1 FROM "Products" WHERE "Id"=NEW."ProductId" FOR UPDATE;
                    RETURN NEW;
                END; $$;
                CREATE TRIGGER "LockWarehouseInventoryProduct" BEFORE INSERT OR UPDATE OR DELETE ON "WarehouseInventories"
                FOR EACH ROW EXECUTE FUNCTION lock_warehouse_inventory_product();
                CREATE FUNCTION check_company_stock_limit() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE product uuid := COALESCE(NEW."ProductId",OLD."ProductId");
                BEGIN
                    IF EXISTS(SELECT 1 FROM "WarehouseInventories" WHERE "ProductId"=product GROUP BY "ProductId"
                        HAVING SUM("QuantityOnHand")>2147483647 OR SUM("MinimumStockLevel")>2147483647) THEN
                        RAISE EXCEPTION 'Company stock limit exceeded' USING ERRCODE='23514'; END IF;
                    RETURN NULL;
                END; $$;
                CREATE CONSTRAINT TRIGGER "CompanyStockLimit" AFTER INSERT OR UPDATE OR DELETE ON "WarehouseInventories"
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION check_company_stock_limit();
                CREATE FUNCTION require_default_warehouse() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF (SELECT COUNT(*) FROM "Warehouses" WHERE "IsDefault" AND "IsActive")<>1 THEN
                        RAISE EXCEPTION 'Exactly one active default warehouse is required' USING ERRCODE='23514'; END IF;
                    RETURN NULL;
                END; $$;
                CREATE CONSTRAINT TRIGGER "RequireDefaultWarehouse" AFTER INSERT OR UPDATE OR DELETE ON "Warehouses"
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION require_default_warehouse();
                CREATE FUNCTION protect_stock_transfer() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP='DELETE' OR OLD."Status"<>'Draft' THEN
                        RAISE EXCEPTION 'Finalized transfers are immutable' USING ERRCODE='23514'; END IF;
                    RETURN NEW;
                END; $$;
                CREATE TRIGGER "ProtectStockTransfer" BEFORE UPDATE OR DELETE ON "StockTransfers" FOR EACH ROW EXECUTE FUNCTION protect_stock_transfer();
                CREATE FUNCTION protect_stock_transfer_item() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE parent uuid := COALESCE(NEW."StockTransferId",OLD."StockTransferId"); state text;
                BEGIN
                    SELECT "Status" INTO state FROM "StockTransfers" WHERE "Id"=parent FOR UPDATE;
                    IF state IS DISTINCT FROM 'Draft' THEN RAISE EXCEPTION 'Finalized transfer items are immutable' USING ERRCODE='23514'; END IF;
                    IF TG_OP='UPDATE' AND (NEW."StockTransferId"<>OLD."StockTransferId" OR NEW."ProductId"<>OLD."ProductId") THEN
                        RAISE EXCEPTION 'Transfer item identity is immutable' USING ERRCODE='23514'; END IF;
                    RETURN COALESCE(NEW,OLD);
                END; $$;
                CREATE TRIGGER "ProtectStockTransferItem" BEFORE INSERT OR UPDATE OR DELETE ON "StockTransferItems" FOR EACH ROW EXECUTE FUNCTION protect_stock_transfer_item();
                CREATE FUNCTION protect_order_warehouse() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW."WarehouseId"<>OLD."WarehouseId" AND EXISTS(SELECT 1 FROM "InventoryMovements" WHERE "ReferenceId"=OLD."Id" AND "MovementType"='OrderDeduction') THEN
                        RAISE EXCEPTION 'Fulfillment warehouse is immutable after deduction' USING ERRCODE='23514'; END IF;
                    RETURN NEW;
                END; $$;
                CREATE TRIGGER "ProtectOrderWarehouse" BEFORE UPDATE ON "Orders" FOR EACH ROW EXECUTE FUNCTION protect_order_warehouse();
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_GoodsReceipts_Warehouses_WarehouseId",
                table: "GoodsReceipts",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryMovements_StockTransfers_StockTransferId",
                table: "InventoryMovements",
                column: "StockTransferId",
                principalTable: "StockTransfers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryMovements_Warehouses_WarehouseId",
                table: "InventoryMovements",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Warehouses_WarehouseId",
                table: "Orders",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => throw new NotSupportedException("Warehouse history requires a forward migration or restoring a verified backup.");
    }
}
