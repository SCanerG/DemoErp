SELECT o0."Id", o0."OrderNumber", c."Name", o0."OrderDate", o0."CompletedAt", o0."Status", (
    SELECT count(*)::int
    FROM "OrderItems" AS o1
    WHERE o0."Id" = o1."OrderId"), o0."TotalAmount"
FROM (
    SELECT o."Id", o."CompletedAt", o."CustomerId", o."OrderDate", o."OrderNumber", o."Status", o."TotalAmount"
    FROM "Orders" AS o
    WHERE o."OrderDate" >= @start AND o."OrderDate" < @end AND o."Status" = @filter_Status
    ORDER BY o."OrderDate" DESC, o."Id"
    LIMIT @p
) AS o0
INNER JOIN "Customers" AS c ON o0."CustomerId" = c."Id"
ORDER BY o0."OrderDate" DESC, o0."Id"