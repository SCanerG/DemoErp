SELECT w."Id", w."MinimumStockLevel", w."ProductId", w."QuantityOnHand", w."UpdatedAt", w."WarehouseId"
FROM "WarehouseInventories" AS w
WHERE w."WarehouseId" = @warehouseId AND w."ProductId" = ANY (@materialIds)