-- Optional isolated developer fixture. Run only through the guarded benchmark tool.
-- Immutable movements are synthetic examples; order lifecycles are tested through the API separately.
BEGIN;
INSERT INTO "Categories" ("Id","Name","Description","IsActive","CreatedAt")
SELECT md5('category-'||n)::uuid, 'Benchmark category '||lpad(n::text,3,'0'), '', true, '2024-01-01Z'::timestamptz FROM generate_series(1,500) n;
INSERT INTO "Products" ("Id","CategoryId","Name","Description","Price","IsActive","CreatedAt")
SELECT md5('product-'||n)::uuid, md5('category-'||(1+(n-1)%500))::uuid, 'Benchmark product '||lpad(n::text,4,'0'), '', (10+n%990)::numeric, n%10<>0, '2024-01-01Z'::timestamptz FROM generate_series(1,5000) n;
INSERT INTO "Customers" ("Id","Name","Email","Phone","Address","IsActive","CreatedAt")
SELECT md5('customer-'||n)::uuid, 'Benchmark customer '||lpad(n::text,4,'0'), '', '', '', n%10<>0, '2024-01-01Z'::timestamptz FROM generate_series(1,2000) n;
INSERT INTO "Users" ("Id","Name","Email","PasswordHash","Role","IsActive","SecurityVersion","IsBootstrapAccount","CreatedAt")
VALUES (md5('benchmark-actor')::uuid,'Synthetic disabled actor','benchmark-actor@example.invalid','disabled-fixture-not-a-password-hash','Viewer',false,0,false,'2024-01-01Z');
INSERT INTO "Orders" ("Id","OrderNumber","CustomerId","Status","OrderDate","CompletedAt","TotalAmount","CreatedAt")
SELECT md5('order-'||n)::uuid, 'BENCH-'||lpad(n::text,6,'0'), md5('customer-'||(1+(n*17)%2000))::uuid,
CASE WHEN n%5 IN (0,1,2) THEN 'Completed' WHEN n%5=3 THEN 'Pending' ELSE 'Cancelled' END,
'2024-01-01Z'::timestamptz+(n%730)*interval '1 day'+(n%24)*interval '1 hour',
CASE WHEN n%5 IN (0,1,2) THEN '2024-01-01Z'::timestamptz+(n%730)*interval '1 day'+(n%24)*interval '1 hour'+interval '2 days' ELSE NULL END,
0, '2024-01-01Z'::timestamptz+(n%730)*interval '1 day' FROM generate_series(1,20000) n;
INSERT INTO "OrderItems" ("Id","OrderId","ProductId","Quantity","UnitPrice","LineTotal")
SELECT md5('line-'||n||'-'||j)::uuid, md5('order-'||n)::uuid, md5('product-'||(1+(n*13+j*107)%5000))::uuid,
1+(n+j)%4, (10+(n+j)%90)::numeric, (1+(n+j)%4)*(10+(n+j)%90)::numeric
FROM generate_series(1,20000) n CROSS JOIN generate_series(1,4) j;
UPDATE "Orders" o SET "TotalAmount"=v.total FROM (SELECT "OrderId",sum("LineTotal") total FROM "OrderItems" GROUP BY "OrderId") v WHERE v."OrderId"=o."Id";
UPDATE "Inventories" SET "QuantityOnHand"=20,"MinimumStockLevel"=5,"UpdatedAt"='2025-12-31Z';
INSERT INTO "InventoryMovements" ("Id","ProductId","MovementType","Quantity","QuantityBefore","QuantityAfter","ReferenceType","ReferenceId","Reason","CreatedAt","CreatedByUserId")
SELECT md5('movement-'||n||'-'||j)::uuid,md5('product-'||n)::uuid,'StockIn',1,j-1,j,'Manual',NULL,'Synthetic benchmark movement',
'2025-01-01Z'::timestamptz+j*interval '1 day',md5('benchmark-actor')::uuid
FROM generate_series(1,5000) n CROSS JOIN generate_series(1,20) j;
COMMIT;
ANALYZE;
