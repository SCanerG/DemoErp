SELECT COALESCE(sum(o0."TotalAmount"), 0.0) AS "Sales", count(*)::int AS "Count"
FROM (
    SELECT o."TotalAmount", 1 AS "Key"
    FROM "Orders" AS o
    WHERE o."Status" = 'Completed' AND o."CompletedAt" >= @filter_Start AND o."CompletedAt" < @filter_End
) AS o0
GROUP BY o0."Key"
LIMIT 2