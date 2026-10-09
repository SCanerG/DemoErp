SELECT s."ProductId", s."Name", c."Name", s."QuantityOnHand", s."MinimumStockLevel", CASE
    WHEN s."QuantityOnHand" = 0 THEN 'Out'
    WHEN s."QuantityOnHand" <= s."MinimumStockLevel" THEN 'Low'
    ELSE 'Healthy'
END, s."UpdatedAt"
FROM (
    SELECT i."MinimumStockLevel", i."ProductId", i."QuantityOnHand", i."UpdatedAt", p."CategoryId", p."Name"
    FROM "Inventories" AS i
    INNER JOIN "Products" AS p ON i."ProductId" = p."Id"
    WHERE p."CategoryId" = @filter_CategoryId
    ORDER BY p."Name", i."ProductId"
    LIMIT @p
) AS s
INNER JOIN "Categories" AS c ON s."CategoryId" = c."Id"
ORDER BY s."Name", s."ProductId"