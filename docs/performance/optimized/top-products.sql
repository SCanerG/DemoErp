SELECT s0."Id", s0."Name", c."Name", s0."Quantity", s0."Sales", s0."Count", i."QuantityOnHand"
FROM (
    SELECT s."Id", s."Quantity", s."Sales", s."Count", p."Id" AS "Id0", p."CategoryId", p."Name"
    FROM (
        SELECT o."ProductId" AS "Id", COALESCE(sum(o."Quantity"::bigint), 0.0)::bigint AS "Quantity", COALESCE(sum(o."LineTotal"), 0.0) AS "Sales", count(DISTINCT o."OrderId")::int AS "Count"
        FROM "OrderItems" AS o
        INNER JOIN "Orders" AS o0 ON o."OrderId" = o0."Id"
        WHERE o0."Status" = 'Completed' AND o0."CompletedAt" >= @start AND o0."CompletedAt" < @end
        GROUP BY o."ProductId"
    ) AS s
    INNER JOIN "Products" AS p ON s."Id" = p."Id"
    ORDER BY s."Sales" DESC, s."Id"
    LIMIT @p
) AS s0
INNER JOIN "Categories" AS c ON s0."CategoryId" = c."Id"
LEFT JOIN "Inventories" AS i ON s0."Id0" = i."ProductId"
ORDER BY s0."Sales" DESC, s0."Id"