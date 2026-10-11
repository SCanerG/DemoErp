import { test, expect, type Page } from '@playwright/test';
import { adminSession, createAccount } from './admin-helper';
import { purchaseSchema, receiptSchema, supplierSchema, previewTotal, progress, receiptOperation } from '../src/procurement';
import type { PurchaseItem } from '../src/procurement';
const base = `${process.env.API_URL ?? 'http://localhost:5080'}/api`;
const supplierId = '10000000-0000-0000-0000-000000000001';
const productId = '20000000-0000-0000-0000-000000000001';
const purchaseId = '30000000-0000-0000-0000-000000000001';
const itemId = '40000000-0000-0000-0000-000000000001';
const receiptId = '50000000-0000-0000-0000-000000000001';
const item = { id: itemId, productId, productName: 'Steel part', quantityOrdered: 10, quantityReceived: 6, quantityRemaining: 4, unitCost: 2.25, lineTotal: 22.5 };
const order = { id: purchaseId, purchaseOrderNumber: 'PO-FIXTURE', supplierId, supplierName: 'North supplier', status: 'PartiallyReceived', orderDate: '2026-10-10T10:00:00Z', expectedDeliveryDate: null, totalAmount: 22.5, notes: '', createdBy: 'Purchaser', items: [item] };
const supplier = { id: supplierId, supplierCode: 'NORTH', name: 'North supplier', contactPerson: 'Contact', email: '', phone: '', address: '', taxNumber: '', isActive: true, createdAt: '2026-10-10T10:00:00Z' };
const pageOf = (items: unknown[], totalCount = items.length) => ({ items, page: 1, pageSize: 20, totalCount, totalPages: Math.ceil(totalCount / 20) });
async function fixtureSession(page: Page, role = 'Manager') {
  await page.route('**/api/dashboard/**', r => r.abort());
  await page.goto('/auth/login'); await page.evaluate(role => {
    localStorage.setItem('catalog.language', 'en');
    sessionStorage.setItem('catalog.session', JSON.stringify({ accessToken: 'procurement-fixture', expiresAt: new Date(Date.now() + 3600000).toISOString(), user: { id: 'fixture', name: 'Fixture', email: 'fixture@example.com', role, isActive: true, createdAt: '2026-10-01T00:00:00Z', updatedAt: null } }));
  }, role);
  await page.route('**/api/warehouses?**', r => r.fulfill({json: pageOf([{id: '80000000-0000-0000-0000-000000000001', code:'MAIN',name:'Main warehouse',isActive:true,isDefault:true}])}));
  await page.route('**/api/suppliers?**', r => r.fulfill({ json: pageOf([supplier]) }));
  await page.route(`**/api/purchase-orders/${purchaseId}`, r => r.fulfill({ json: order }));
  await page.route(`**/api/purchase-orders/${purchaseId}/receipts?**`, r => r.fulfill({ json: pageOf([]) }));
}
test('purchase validation rejects missing, duplicate, fractional quantity and invalid costs; totals use entered costs', () => {
  const valid = { supplierId, expectedDeliveryDate: '', notes: '', items: [{ productId, quantityOrdered: 3, unitCost: 1.25 }] };
  expect(purchaseSchema.safeParse(valid).success).toBe(true); expect(previewTotal(valid.items)).toBe(3.75);
  for (const items of [[], [...valid.items, ...valid.items], [{ ...valid.items[0], quantityOrdered: 0 }], [{ ...valid.items[0], quantityOrdered: 1.5 }], [{ ...valid.items[0], unitCost: -1 }], [{ ...valid.items[0], unitCost: 1.001 }]]) expect(purchaseSchema.safeParse({ ...valid, items }).success).toBe(false);
  expect(purchaseSchema.safeParse({ ...valid, supplierId: '' }).success).toBe(false);
  expect(progress(110, 20)).toBe(18); // weighted units, not the average of item percentages
});
test('receipt and supplier validation enforce remaining quantities, positive selection and valid contact data', () => {
  const schema = receiptSchema([item] as PurchaseItem[]);
  for (const quantity of [0, -1, 5, 1.5]) expect(schema.safeParse({ notes: '', quantities: [quantity] }).success).toBe(false);
  expect(schema.safeParse({ notes: '', quantities: [4] }).success).toBe(true);
  expect(supplierSchema.safeParse({ ...supplier, email: 'bad' }).success).toBe(false);
  expect(supplierSchema.safeParse({ ...supplier, supplierCode: 'bad code' }).success).toBe(false);
  expect(supplierSchema.safeParse(supplier).success).toBe(true);
  const operation = receiptOperation(null, 'same'); expect(receiptOperation(operation, 'same').key).toBe(operation.key); expect(receiptOperation(operation, 'changed').key).not.toBe(operation.key);
});
test('supplier form validates fields and switches English/Turkish', async ({ page }) => {
  await fixtureSession(page); await page.goto('/suppliers/new'); await page.getByRole('button', { name: 'Save supplier', exact: true }).click(); await expect(page.getByRole('alert').first()).toBeVisible();
  await page.getByLabel('Supplier code').fill('bad code'); await page.getByLabel('Name', { exact: false }).fill('North'); await page.getByRole('button', { name: 'Save supplier', exact: true }).click(); await expect(page.getByText('Use letters, numbers, hyphens or underscores.')).toBeVisible();
  await page.getByRole('button', { name: 'TR', exact: true }).click(); await expect(page.getByRole('heading', { name: 'Yeni tedarikçi' })).toBeVisible(); await expect(page.getByLabel('Tedarikçi kodu')).toBeVisible();
});
test('supplier server search and pagination render loading, empty and recoverable error states', async ({ page }) => {
  await fixtureSession(page); let state = 'loading'; let release = () => {}; const waiting = new Promise<void>(r => { release = r; });
  await page.route('**/api/suppliers?**', async r => { if (state === 'loading') await waiting; if (state === 'error') await r.fulfill({ status: 500, json: {} }); else await r.fulfill({ json: pageOf(state === 'empty' ? [] : [supplier], state === 'empty' ? 0 : 21) }); });
  await page.goto('/suppliers'); await expect(page.getByRole('status')).toBeVisible(); release(); await expect(page.getByRole('link', { name: 'NORTH', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Next', exact: true }).click(); await expect(page.getByText(/Page 2/)).toBeVisible();
  state = 'empty'; await page.getByLabel('Search', { exact: true }).fill('absent'); await expect(page.getByText('No results for these filters.')).toBeVisible();
  state = 'error'; await page.getByLabel('Search', { exact: true }).fill('error'); await expect(page.getByRole('button', { name: 'Try again' })).toBeVisible({ timeout: 15000 }); state = 'ok'; await page.getByRole('button', { name: 'Try again' }).click(); await expect(page.getByRole('link', { name: 'NORTH', exact: true })).toBeVisible();
});
test('purchase form previews entered costs and prevents duplicate product choices', async ({ page }) => {
  await fixtureSession(page); await page.route('**/api/products', r => r.fulfill({ json: [{ id: productId, name: 'Steel part', price: 99, isActive: true }] }));
  await page.goto('/purchase-orders/new'); await page.getByLabel('Supplier', { exact: false }).last().selectOption(supplierId);
  await page.getByLabel('Product', { exact: false }).selectOption(productId); await page.getByLabel('Ordered quantity').fill('3'); await page.getByLabel('Unit cost').fill('1.25'); await expect(page.locator('.order-total')).toContainText('$3.75');
  await page.getByRole('button', { name: 'Add item' }).click(); expect(await page.getByLabel('Product', { exact: false }).nth(1).locator(`option[value="${productId}"]`).count()).toBe(0);
  await page.screenshot({ path: '../.tools/phase7/purchase-form-en.png', fullPage: true });
});
test('purchase lifecycle controls and Viewer route protection match permissions', async ({ page }) => {
  await fixtureSession(page, 'Viewer'); await page.goto(`/purchase-orders/${purchaseId}`); await expect(page.getByRole('button', { name: 'Cancel purchase order', exact: true })).toHaveCount(0); await expect(page.getByRole('link', { name: 'Receive goods', exact: true })).toHaveCount(0);
  await page.goto(`/purchase-orders/${purchaseId}/receive`); await expect(page.getByText('You do not have permission to access this page.')).toBeVisible();
});
test('partial purchase shows remaining quantities and only receiving action; mobile has no page overflow', async ({ page }) => {
  await fixtureSession(page); await page.goto(`/purchase-orders/${purchaseId}`); await expect(page.getByRole('link', { name: 'Receive goods', exact: true })).toBeVisible(); await expect(page.getByRole('button', { name: 'Cancel purchase order', exact: true })).toHaveCount(0); await expect(page.getByRole('link', { name: 'Edit purchase order', exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'TR', exact: true }).click(); await expect(page.getByText('Kısmen teslim alındı', { exact: true })).toBeVisible();
  await page.setViewportSize({ width: 390, height: 844 }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true); await page.screenshot({ path: '../.tools/phase7/purchase-detail-tr-mobile.png', fullPage: true });
});
test('receipt form defaults zero and rejects zero, negative and excessive quantities', async ({ page }) => {
  await fixtureSession(page); await page.goto(`/purchase-orders/${purchaseId}/receive`); const input = page.getByLabel('Receive quantity'); await expect(input).toHaveValue('0'); await expect(input).toHaveAttribute('max', '4');
  await page.getByRole('button', { name: 'Review receipt' }).click(); await expect(page.getByText('Enter at least one positive receipt quantity.')).toBeVisible();
  await input.fill('5'); await page.getByRole('button', { name: 'Review receipt' }).click(); await expect(page.getByText('Quantity exceeds remaining amount.')).toBeVisible();
  await input.fill('-1'); await page.getByRole('button', { name: 'Review receipt' }).click(); await expect(page.getByRole('dialog')).toHaveCount(0);
});
test('receipt retry reuses operation key, payload change creates new key, pending prevents duplicates', async ({ page }) => {
  await fixtureSession(page); const keys: string[] = []; let release = () => {}; const waiting = new Promise<void>(r => { release = r; });
  await page.route(`**/api/purchase-orders/${purchaseId}/receipts`, async r => { keys.push(r.request().headers()['idempotency-key']); if (keys.length === 1) await waiting; await r.fulfill({ status: 409, json: { code: 'receiptExceedsRemaining' } }); });
  await page.goto(`/purchase-orders/${purchaseId}/receive`); await page.getByLabel('Receive quantity').fill('2'); await page.getByRole('button', { name: 'Review receipt' }).click(); const post = page.getByRole('dialog').getByRole('button', { name: 'Post receipt', exact: true }); await post.click(); await expect(page.getByRole('dialog').getByRole('button').last()).toBeDisabled(); expect(keys).toHaveLength(1); release(); await expect(post).toBeEnabled(); await expect(page.getByRole('dialog').getByRole('alert')).toContainText('Quantity exceeds remaining');
  await post.click(); await expect(post).toBeEnabled(); expect(keys).toHaveLength(2); expect(keys[1]).toBe(keys[0]); await page.getByRole('dialog').getByRole('button', { name: 'Cancel', exact: true }).click();
  await page.getByLabel('Receive quantity').fill('3'); await page.getByRole('button', { name: 'Review receipt' }).click(); await page.getByRole('dialog').getByRole('button', { name: 'Post receipt', exact: true }).click(); await expect(page.getByRole('dialog').getByRole('alert')).toBeVisible(); expect(keys).toHaveLength(3); expect(keys[2]).not.toBe(keys[0]);
});
test('receipt success navigates to immutable detail with order and stock links', async ({ page }) => {
  await fixtureSession(page); const receipt = { id: receiptId, receiptNumber: 'GR-FIXTURE', purchaseOrderId: purchaseId, purchaseOrderNumber: 'PO-FIXTURE', supplierId, supplierName: supplier.name, receivedAt: '2026-10-10T10:00:00Z', createdBy: 'Purchaser', notes: '', items: [{ id: itemId, productId, productName: item.productName, quantityReceived: 4 }] };
  await page.route(`**/api/purchase-orders/${purchaseId}/receipts`, r => r.fulfill({ status: 201, json: receipt })); await page.route(`**/api/goods-receipts/${receiptId}`, r => r.fulfill({ json: receipt }));
  await page.goto(`/purchase-orders/${purchaseId}/receive`); await page.getByLabel('Receive quantity').fill('4'); await page.getByRole('button', { name: 'Review receipt' }).click(); await page.getByRole('dialog').getByRole('button', { name: 'Post receipt', exact: true }).click(); await expect(page).toHaveURL(new RegExp(`/goods-receipts/${receiptId}$`)); await expect(page.getByRole('heading', { name: 'GR-FIXTURE' })).toBeVisible(); await expect(page.getByRole('link', { name: /View stock movements/ })).toHaveAttribute('href', `/inventory/${productId}`); await page.screenshot({ path: '../.tools/phase7/receipt-en.png', fullPage: true });
});
test('procurement report sends server-side supplier/status/date filters and weighted progress', async ({ page }) => {
  await fixtureSession(page); await page.route('**/api/reports/procurement?**', r => r.fulfill({ json: pageOf([{ ...order, orderedQuantity: 110, receivedQuantity: 20, remainingQuantity: 90 }]) }));
  await page.goto('/reports/procurement'); await expect(page.getByText('20 / 110 · 18%')).toBeVisible(); const request = page.waitForRequest(r => r.url().includes('/reports/procurement?') && r.url().includes('status=Submitted')); await page.getByLabel('Status', { exact: true }).selectOption('Submitted'); await request;
  await page.getByLabel('Start date').fill('2026-10-01'); await expect(page.getByText('Choose both dates in chronological order.')).toBeVisible(); const dates = page.waitForRequest(r => r.url().includes('/reports/procurement?') && r.url().includes('start=') && r.url().includes('end=')); await page.getByLabel('End date').fill('2026-10-10'); await dates;
});
test('real Manager creates supplier and purchase, submits, receives partially/full and follows inventory references', async ({ page }) => {
  const manager = await createAccount(page.request, 'Manager'); const admin = await adminSession(page.request); const headers = { Authorization: `Bearer ${admin.accessToken}` }; const unique = Date.now();
  const categoryResponse = await page.request.post(`${base}/categories`, { headers, data: { name: `Procurement ${unique}`, description: '', isActive: true } }); expect(categoryResponse.status()).toBe(201); const category = await categoryResponse.json();
  const productResponse = await page.request.post(`${base}/products`, { headers, data: { name: `Part ${unique}`, description: '', price: 99, isActive: true, categoryId: category.id } }); expect(productResponse.status()).toBe(201); const product = await productResponse.json();
  await page.goto('/auth/login'); await page.evaluate(session => { localStorage.setItem('catalog.language', 'en'); sessionStorage.setItem('catalog.session', JSON.stringify(session)); }, manager.session);
  await page.goto('/suppliers/new'); await page.getByLabel('Supplier code').fill(`s-${unique}`); await page.getByLabel('Name', { exact: false }).fill(`Supplier ${unique}`); await page.getByRole('button', { name: 'Save supplier', exact: true }).click(); await expect(page).toHaveURL(/\/suppliers\/[\da-f-]+$/);
  const supplier = page.url().split('/').pop()!; await page.goto('/purchase-orders/new'); await page.getByLabel('Search suppliers').fill(`s-${unique}`); await page.getByLabel('Supplier', { exact: false }).last().selectOption(supplier); await page.getByLabel('Product', { exact: false }).selectOption(product.id); await page.getByLabel('Ordered quantity').fill('10'); await page.getByLabel('Unit cost').fill('2.25'); await page.getByRole('button', { name: 'Save draft', exact: true }).click(); await expect(page).toHaveURL(/\/purchase-orders\/[\da-f-]+$/); const orderUrl = page.url();
  await expect(page.locator('.order-total')).toContainText('$22.50'); await page.getByRole('button', { name: 'Submit purchase order', exact: true }).click(); await page.getByRole('dialog').getByRole('button', { name: 'Submit purchase order', exact: true }).click(); await expect(page.getByRole('link', { name: 'Receive goods', exact: true })).toBeVisible();
  for (const qty of ['6', '4']) { await page.getByRole('link', { name: 'Receive goods', exact: true }).click(); await page.getByLabel('Receive quantity').fill(qty); await page.getByRole('button', { name: 'Review receipt' }).click(); await page.getByRole('dialog').getByRole('button', { name: 'Post receipt', exact: true }).click(); await expect(page).toHaveURL(/\/goods-receipts\/[\da-f-]+$/); await expect(page.getByText('Posted receipt. Quantities and stock movements are permanent.')).toBeVisible(); await page.goto(orderUrl); }
  await expect(page.getByText('Received', { exact: true })).toBeVisible(); await expect(page.getByRole('link', { name: 'Receive goods', exact: true })).toHaveCount(0); await page.goto(`/inventory/${product.id}`); await expect(page.locator('table tbody tr').filter({ hasText: 'Purchase receipt' })).toHaveCount(2); await expect(page.locator('a[href^="/goods-receipts/"]').first()).toBeVisible();
  await page.screenshot({ path: '../.tools/phase7/real-inventory-receipts-en.png', fullPage: true });
});
