SELECT i."Id", i."Quantity", i."QuantityBefore", i."QuantityAfter", u."Name" AS "Actor", i."CreatedAt"
FROM "InventoryMovements" AS i
INNER JOIN "Users" AS u ON i."CreatedByUserId" = u."Id"
WHERE i."ProductId" = @productId
ORDER BY i."CreatedAt" DESC, i."Id" DESC