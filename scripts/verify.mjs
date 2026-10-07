// Run against the development Compose stack. Creates and cleans up its own records.
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { setTimeout as delay } from 'node:timers/promises';

const base = process.env.API_URL ?? 'http://localhost:5080';
const frontend = process.env.FRONTEND_URL ?? 'http://localhost:3000';
const email = `verify-${Date.now()}@example.com`;
const password = 'VerificationPassword123!';
let token;
let productId;
let categoryId;
let customerId;
let orderId;
const orderProductIds = [];
const docker = (...args) => execFileSync('docker', ['compose', ...args], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });
const sql = query => docker('exec', '-T', 'postgres', 'sh', '-c', 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Atc "$1"', 'verify', query).trim();

async function request(path, method = 'GET', body, authenticated = true) {
  const response = await fetch(`${base}${path}`, { method, headers: {
    ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
    ...(authenticated && token ? { Authorization: `Bearer ${token}` } : {}),
  }, ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
  const content = await response.text();
  return { status: response.status, data: content ? JSON.parse(content) : undefined, headers: response.headers };
}

async function waitHealthy() {
  for (let i = 0; i < 40; i++) {
    try { if ((await fetch(`${base}/health`)).ok) return; } catch { /* Container is starting. */ }
    await delay(1000);
  }
  throw new Error('Backend did not become healthy.');
}

try {
  await waitHealthy();
  assert.equal((await fetch(frontend)).status, 200);
  assert.equal((await fetch(`${frontend}/products/new`)).status, 200);
  assert.equal(sql('SELECT COUNT(*) FROM "__EFMigrationsHistory";'), '2');
  assert.equal(sql("SELECT character_maximum_length FROM information_schema.columns WHERE table_name = 'Users' AND column_name = 'Email';"), '254');
  assert.equal(sql("SELECT numeric_precision || ',' || numeric_scale FROM information_schema.columns WHERE table_name = 'Products' AND column_name = 'Price';"), '12,2');
  assert.equal(sql("SELECT COUNT(*) FROM pg_indexes WHERE tablename = 'Users' AND indexname = 'IX_Users_Email' AND indexdef LIKE 'CREATE UNIQUE INDEX%';"), '1');
  console.log('PASS startup, SPA routing, PostgreSQL connectivity, applied migration');

  const missingId = '00000000-0000-0000-0000-000000000001';
  for (const [path, method] of [['/api/products', 'GET'], ['/api/products', 'POST'], [`/api/products/${missingId}`, 'GET'], [`/api/products/${missingId}`, 'PUT'], [`/api/products/${missingId}`, 'DELETE']]) {
    const result = await request(path, method, undefined, false);
    assert.equal(result.status, 401);
    assert.equal(result.data.status, 401);
  }
  token = 'invalid-token';
  assert.equal((await request('/api/products')).status, 401);
  token = undefined;
  console.log('PASS unauthorized and invalid-token protection on product endpoints');

  const badRegistration = await request('/api/auth/register', 'POST', { name: ' ', email: 'bad', password: 'short' }, false);
  assert.equal(badRegistration.status, 400);
  assert.ok(badRegistration.data.errors);
  assert.equal((await request('/api/auth/register', 'POST', { name: 'Verification User', email, password }, false)).status, 201);
  assert.equal((await request('/api/auth/register', 'POST', { name: 'Duplicate', email: email.toUpperCase(), password }, false)).status, 409);
  const hash = sql(`SELECT "PasswordHash" FROM "Users" WHERE "Email" = '${email}';`);
  assert.ok(hash && hash !== password && hash.startsWith('AQAAAA'));
  assert.equal((await request('/api/auth/login', 'POST', { email, password: 'incorrect-password' }, false)).status, 401);
  const login = await request('/api/auth/login', 'POST', { email: email.toUpperCase(), password }, false);
  assert.equal(login.status, 200);
  assert.ok(login.data.accessToken && login.data.user.id);
  assert.ok(!JSON.stringify(login.data).includes('passwordHash'));
  token = login.data.accessToken;
  console.log('PASS registration, normalization, duplicate rejection, password hash, login');

  const category = await request('/api/categories', 'POST', { name: 'Verification category', description: '', isActive: true });
  assert.equal(category.status, 201); categoryId = category.data.id;
  const input = { name: 'Verification notebook', description: 'Integration test product', price: 19.95, isActive: true, categoryId };
  const beforeValidation = (await request('/api/products')).data.length;
  for (const invalid of [{ ...input, name: ' ' }, { ...input, price: -1 }, { ...input, price: 1.234 }, { ...input, description: 'x'.repeat(2001) }]) {
    const result = await request('/api/products', 'POST', invalid);
    assert.equal(result.status, 400);
    assert.ok(result.data.errors);
  }
  assert.equal((await request('/api/products', 'POST', { name: 'Missing price', description: '', isActive: true })).status, 400);
  const invalidBody = await request('/api/products', 'POST', { name: 'Missing price', description: '', isActive: true });
  assert.ok(!JSON.stringify(invalidBody.data).match(/Demo\.Api|Exception|stack/));
  assert.equal((await request('/api/products')).data.length, beforeValidation);
  const created = await request('/api/products', 'POST', input);
  assert.equal(created.status, 201);
  assert.ok(created.headers.get('location').endsWith(created.data.id));
  productId = created.data.id;
  assert.ok(created.data.createdAt);
  assert.equal(created.data.updatedAt, null);
  assert.ok((await request('/api/products')).data.some(product => product.id === productId));
  assert.equal((await request(`/api/products/${productId}`)).data.name, input.name);
  assert.equal((await request(`/api/products/${missingId}`)).status, 404);
  const updated = await request(`/api/products/${productId}`, 'PUT', { ...input, name: 'Updated notebook', price: 24.50, isActive: false });
  assert.equal(updated.status, 200);
  assert.ok(Date.parse(updated.data.updatedAt) >= Date.parse(created.data.createdAt));
  assert.equal(updated.data.createdAt, created.data.createdAt);
  assert.equal((await request(`/api/products/${productId}`)).data.price, 24.5);
  assert.equal((await request(`/api/products/${productId}`)).data.updatedAt, updated.data.updatedAt);
  assert.ok((await request('/api/products')).data.some(product => product.id === productId && !product.isActive));
  assert.equal((await request(`/api/products/${missingId}`, 'PUT', input)).status, 404);
  assert.equal((await request(`/api/products/${missingId}`, 'DELETE')).status, 404);
  console.log('PASS validation, create, list, detail, missing record, update, inactive catalog behavior');

  const swagger = await request('/swagger/v1/swagger.json');
  assert.equal(swagger.status, 200);
  assert.ok(swagger.data.components.securitySchemes.Bearer);
  for (const [path, operations] of Object.entries(swagger.data.paths)) {
    for (const operation of Object.values(operations)) {
      if (!path.startsWith('/api/auth')) assert.ok(operation.security.some(item => Object.hasOwn(item, 'Bearer')));
      if (path.startsWith('/api/auth')) assert.ok(!operation.security?.length);
    }
  }
  assert.equal((await fetch(`${base}/swagger/index.html`)).status, 200);
  console.log('PASS Swagger UI, contracts, protected endpoint security metadata');

  for (const resource of ['categories', 'customers', 'orders']) assert.equal((await request(`/api/${resource}`, 'GET', undefined, false)).status, 401);
  const customer = await request('/api/customers', 'POST', { name: 'Verification customer', email: 'verify@example.com', phone: '5551234567', address: 'Test address', isActive: true });
  assert.equal(customer.status, 201); customerId = customer.data.id;
  assert.equal((await request(`/api/customers/${customerId}`, 'PUT', { ...customer.data, phone: '5557654321' })).status, 200);
  for (const price of [19.95, 3.50]) {
    const product = await request('/api/products', 'POST', { ...input, name: `Order verification ${price}`, price });
    assert.equal(product.status, 201); orderProductIds.push(product.data.id);
  }
  const order = await request('/api/orders', 'POST', { customerId, totalAmount: 0, items: orderProductIds.map((productId, index) => ({ productId, quantity: index + 2, unitPrice: 0, lineTotal: 0 })) });
  assert.equal(order.status, 201); orderId = order.data.id;
  assert.equal(order.data.totalAmount, 50.40); assert.equal(order.data.items.length, 2);
  assert.equal((await request(`/api/categories/${categoryId}`, 'DELETE')).status, 409);
  assert.equal((await request(`/api/customers/${customerId}`, 'DELETE')).status, 409);
  assert.equal((await request(`/api/products/${orderProductIds[0]}`, 'DELETE')).status, 409);
  assert.equal((await request(`/api/orders/${orderId}/status`, 'PUT', { status: 'Completed' })).status, 409);
  assert.equal((await request(`/api/orders/${orderId}/status`, 'PUT', { status: 'Confirmed' })).data.status, 'Confirmed');
  assert.equal(sql("SELECT COUNT(*) FROM pg_indexes WHERE indexname IN ('IX_Products_CategoryId','IX_Orders_CustomerId','IX_OrderItems_OrderId','IX_OrderItems_ProductId','IX_Orders_OrderNumber');"), '5');
  console.log('PASS business relations, category/customer safeguards, authoritative order total, transitions and indexes');

  docker('restart');
  await waitHealthy();
  assert.equal((await request(`/api/products/${productId}`)).data.name, 'Updated notebook');
  assert.equal((await request('/api/auth/login', 'POST', { email, password }, false)).status, 200);
  const persistedOrder = await request(`/api/orders/${orderId}`);
  assert.equal(persistedOrder.data.totalAmount, 50.40); assert.equal(persistedOrder.data.status, 'Confirmed');
  assert.equal(persistedOrder.data.customerName, 'Verification customer'); assert.equal(persistedOrder.data.items.length, 2);
  assert.equal((await request(`/api/customers/${customerId}`)).data.phone, '5557654321');
  console.log('PASS category/customer/order/items persistence after restart');
  console.log('PASS user and product persistence after all containers restart');

  try {
    docker('stop', 'postgres');
    const failed = await request('/api/products');
    assert.equal(failed.status, 500);
    assert.equal(failed.data.status, 500);
    assert.ok(failed.data.traceId);
    assert.ok(!JSON.stringify(failed.data).match(/Npgsql|Exception|stack|ConnectionString/));
    assert.ok(docker('logs', '--tail', '80', 'backend').includes('Unexpected failure on'));
    console.log('PASS unexpected exception logged and safe ProblemDetails returned');
  } finally {
    docker('start', 'postgres');
    await waitHealthy();
  }

  assert.equal((await request(`/api/products/${productId}`, 'DELETE')).status, 204);
  assert.equal((await request(`/api/products/${productId}`)).status, 404);
  assert.ok(!(await request('/api/products')).data.some(product => product.id === productId));
  productId = undefined;
  console.log('PASS hard deletion and subsequent 404');
  const logs = docker('logs', 'backend');
  for (const sensitive of [password, hash, token]) assert.ok(!logs.includes(sensitive), 'Backend logs exposed sensitive credentials');
  console.log('PASS no test password, password hash, or JWT in backend logs');
  console.log('API verification completed successfully. Browser verification is separate.');
} finally {
  // Admin cleanup applies only to the UUID returned for this script's own order; the application has no order-delete endpoint.
  if (orderId && /^[0-9a-f-]{36}$/i.test(orderId)) sql(`DELETE FROM "Orders" WHERE "Id" = '${orderId}';`);
  for (const id of orderProductIds) await request(`/api/products/${id}`, 'DELETE').catch(() => {});
  if (customerId) await request(`/api/customers/${customerId}`, 'DELETE').catch(() => {});
  if (productId) await request(`/api/products/${productId}`, 'DELETE').catch(() => {});
  if (categoryId) await request(`/api/categories/${categoryId}`, 'DELETE').catch(() => {});
  sql(`DELETE FROM "Users" WHERE "Email" = '${email}';`);
}
