SELECT c."Id", c."Name", COALESCE(o2."Count", 0), COALESCE(o0."Count", 0), COALESCE(o0."Sales", 0.0), COALESCE(o0."Average", 0.0), o0."Last"
FROM "Customers" AS c
LEFT JOIN (
    SELECT o."CustomerId" AS "Id", count(*)::int AS "Count", COALESCE(sum(o."TotalAmount"), 0.0) AS "Sales", avg(o."TotalAmount") AS "Average", max(o."CompletedAt") AS "Last"
    FROM "Orders" AS o
    WHERE o."Status" = 'Completed' AND o."CompletedAt" >= @start AND o."CompletedAt" < @end
    GROUP BY o."CustomerId"
) AS o0 ON c."Id" = o0."Id"
LEFT JOIN (
    SELECT o1."CustomerId" AS "Id", count(*)::int AS "Count"
    FROM "Orders" AS o1
    WHERE o1."OrderDate" >= @start2 AND o1."OrderDate" < @end3
    GROUP BY o1."CustomerId"
) AS o2 ON c."Id" = o2."Id"
ORDER BY COALESCE(o0."Sales", 0.0) DESC, c."Id"
LIMIT @p