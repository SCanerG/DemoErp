using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Demo.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InventoryManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Inventories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuantityOnHand = table.Column<int>(type: "integer", nullable: false),
                    MinimumStockLevel = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Inventories", x => x.Id);
                    table.CheckConstraint("CK_Inventories_Levels", "\"QuantityOnHand\" >= 0 AND \"MinimumStockLevel\" >= 0");
                    table.ForeignKey(
                        name: "FK_Inventories_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InventoryMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    MovementType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    QuantityBefore = table.Column<int>(type: "integer", nullable: false),
                    QuantityAfter = table.Column<int>(type: "integer", nullable: false),
                    ReferenceType = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ReferenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryMovements", x => x.Id);
                    table.CheckConstraint("CK_InventoryMovements_Delta", "(\"MovementType\" IN ('StockIn','AdjustmentIncrease','OrderCancellationReturn') AND \"QuantityAfter\"::bigint = \"QuantityBefore\"::bigint + \"Quantity\") OR (\"MovementType\" IN ('StockOut','AdjustmentDecrease','OrderDeduction') AND \"QuantityAfter\"::bigint = \"QuantityBefore\"::bigint - \"Quantity\")");
                    table.CheckConstraint("CK_InventoryMovements_Quantity", "\"Quantity\" > 0 AND \"QuantityBefore\" >= 0 AND \"QuantityAfter\" >= 0");
                    table.CheckConstraint("CK_InventoryMovements_Reason", "length(btrim(\"Reason\")) > 0");
                    table.CheckConstraint("CK_InventoryMovements_Reference", "(\"ReferenceType\" = 'Manual' AND \"ReferenceId\" IS NULL AND \"MovementType\" IN ('StockIn','StockOut','AdjustmentIncrease','AdjustmentDecrease')) OR (\"ReferenceType\" = 'Order' AND \"ReferenceId\" IS NOT NULL AND \"MovementType\" IN ('OrderDeduction','OrderCancellationReturn'))");
                    table.ForeignKey(
                        name: "FK_InventoryMovements_Orders_ReferenceId",
                        column: x => x.ReferenceId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryMovements_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryMovements_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Inventories_ProductId",
                table: "Inventories",
                column: "ProductId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_CreatedByUserId",
                table: "InventoryMovements",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_ProductId_CreatedAt",
                table: "InventoryMovements",
                columns: new[] { "ProductId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_ReferenceId_ProductId_MovementType",
                table: "InventoryMovements",
                columns: new[] { "ReferenceId", "ProductId", "MovementType" },
                unique: true,
                filter: "\"ReferenceType\" = 'Order'");
            // No invented opening stock or historical movements. All existing products
            // start at zero; new products are initialized even when inserted via SQL.
            migrationBuilder.Sql("""
                INSERT INTO "Inventories" ("Id", "ProductId", "QuantityOnHand", "MinimumStockLevel", "UpdatedAt")
                SELECT gen_random_uuid(), "Id", 0, 0, CURRENT_TIMESTAMP FROM "Products";

                CREATE FUNCTION initialize_inventory() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    INSERT INTO "Inventories" ("Id", "ProductId", "QuantityOnHand", "MinimumStockLevel", "UpdatedAt")
                    VALUES (gen_random_uuid(), NEW."Id", 0, 0, CURRENT_TIMESTAMP);
                    RETURN NEW;
                END; $$;
                CREATE TRIGGER "InitializeInventory" AFTER INSERT ON "Products"
                FOR EACH ROW EXECUTE FUNCTION initialize_inventory();

                CREATE FUNCTION protect_inventory_row() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'UPDATE' THEN
                        IF NEW."ProductId" <> OLD."ProductId" OR NEW."Id" <> OLD."Id" THEN
                            RAISE EXCEPTION 'Inventory identity is immutable' USING ERRCODE = '23514';
                        END IF;
                        RETURN NEW;
                    END IF;
                    IF EXISTS (SELECT 1 FROM "Products" WHERE "Id" = OLD."ProductId") THEN
                        RAISE EXCEPTION 'Product inventory cannot be removed' USING ERRCODE = '23514';
                    END IF;
                    RETURN OLD;
                END; $$;
                CREATE TRIGGER "ProtectInventory" BEFORE UPDATE OR DELETE ON "Inventories"
                FOR EACH ROW EXECUTE FUNCTION protect_inventory_row();

                CREATE FUNCTION immutable_inventory_movement() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Inventory movements are immutable' USING ERRCODE = '23514';
                END; $$;
                CREATE TRIGGER "ImmutableInventoryMovement" BEFORE UPDATE OR DELETE ON "InventoryMovements"
                FOR EACH ROW EXECUTE FUNCTION immutable_inventory_movement();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS "InitializeInventory" ON "Products";
                DROP FUNCTION IF EXISTS initialize_inventory();
                DROP FUNCTION IF EXISTS protect_inventory_row() CASCADE;
                DROP FUNCTION IF EXISTS immutable_inventory_movement() CASCADE;
                """);
            migrationBuilder.DropTable(
                name: "Inventories");

            migrationBuilder.DropTable(
                name: "InventoryMovements");
        }
    }
}
