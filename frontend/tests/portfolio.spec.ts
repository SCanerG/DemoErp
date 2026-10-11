import { test, expect } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { mkdir } from 'node:fs/promises';
import path from 'node:path';
import { adminSession } from './admin-helper';

test('portfolio: synthetic business screenshots and responsive visual matrix', async ({ page, request }) => {
  if (process.env.ERP_TEST_ENVIRONMENT !== 'isolated') throw new Error('Capture requires ERP_TEST_ENVIRONMENT=isolated and a disposable Compose database.');
  test.setTimeout(120_000);
  const output = process.env.SCREENSHOT_DIR ?? '../.tools/phase10/screenshots';
  await mkdir(output, { recursive: true });
  const admin = await adminSession(request);
  const base = `${process.env.API_URL ?? 'http://localhost:5080'}/api`;
  const headers = { Authorization: `Bearer ${admin.accessToken}` };
  const send = async (url: string, data?: unknown, method = 'POST') => {
    const r = await request.fetch(base + url, { method, headers, data });
    expect(r.ok(), `${url}: ${await r.text()}`).toBe(true);
    return r.status() === 204 ? null : r.json();
  };
  const suffix = Date.now();
  const category = await send('/categories', { name: 'Workspace collection', description: 'Synthetic portfolio collection', isActive: true });
  const raw = await send('/products', { name: 'Oak table leg', description: 'Solid oak component · synthetic demo', productType: 'RawMaterial', price: 15, isActive: true, categoryId: category.id });
  const finished = await send('/products', { name: 'Oak meeting table', description: 'Four-seat meeting table · synthetic demo', productType: 'FinishedGood', price: 360, isActive: true, categoryId: category.id });
  const main = await send('/warehouses', { code: `RAW-${suffix}`, name: 'Materials warehouse', location: 'Istanbul · demo', description: 'Synthetic materials location' });
  const destination = await send('/warehouses', { code: `FIN-${suffix}`, name: 'Finished goods warehouse', location: 'Ankara · demo', description: 'Synthetic finished goods location' });
  await send(`/warehouses/${main.id}/inventory/${raw.id}/stock-in`, { quantity: 100, reason: 'Synthetic portfolio supply' });
  const bom = await send('/boms', { productId: finished.id, name: 'Oak meeting table recipe', version: '1.0', baseQuantity: 1, items: [{ materialProductId: raw.id, quantityRequired: 4 }] });
  await send(`/boms/${bom.id}/activate`);
  const production = await send('/production-orders', { productId: finished.id, billOfMaterialsId: bom.id, plannedQuantity: 10, sourceWarehouseId: main.id, destinationWarehouseId: destination.id, plannedStartDate: null, plannedEndDate: null, notes: 'Synthetic portfolio production batch' });
  for (const action of ['release', 'start', 'complete']) await send(`/production-orders/${production.id}/${action}`);
  const customer = await send('/customers', { name: 'Northwind Workspace', email: 'workspace@example.com', phone: '', address: 'Synthetic demo address', isActive: true });
  const order = await send('/orders', { customerId: customer.id, warehouseId: destination.id, items: [{ productId: finished.id, quantity: 3 }] });
  for (const status of ['Confirmed', 'Completed']) await send(`/orders/${order.id}/status`, { status }, 'PUT');
  const pending = await send('/orders', { customerId: customer.id, warehouseId: destination.id, items: [{ productId: finished.id, quantity: 1 }] });
  const supplier = await send('/suppliers', { supplierCode: `OAK-${suffix}`, taxNumber: '', name: 'Oak Supply', contactPerson: 'Demo supplier contact', email: 'supply@example.com', phone: '', address: 'Synthetic demo address', isActive: true });
  const purchase = await send('/purchase-orders', { supplierId: supplier.id, expectedDeliveryDate: null, notes: 'Synthetic material replenishment', items: [{ productId: raw.id, quantityOrdered: 40, unitCost: 8 }] });
  await send(`/purchase-orders/${purchase.id}/submit`);
  const receiptResponse = await request.post(`${base}/purchase-orders/${purchase.id}/receipts`, { headers: { ...headers, 'Idempotency-Key': randomUUID() }, data: { warehouseId: main.id, notes: 'Synthetic goods receipt', items: [{ purchaseOrderItemId: purchase.items[0].id, quantityReceived: 20 }] } });
  expect(receiptResponse.ok(), await receiptResponse.text()).toBe(true);
  const transfer = await send('/stock-transfers', { sourceWarehouseId: main.id, destinationWarehouseId: destination.id, notes: 'Synthetic material relocation', items: [{ productId: raw.id, quantity: 5 }] });
  await send(`/stock-transfers/${transfer.id}/complete`);
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto('/auth/login');
  await expect(page.getByRole('heading', { name: 'Welcome back' })).toBeVisible();
  await page.screenshot({ path: path.join(output, 'login-en.png'), fullPage: true });
  await page.evaluate(s => sessionStorage.setItem('catalog.session', JSON.stringify(s)), admin);
  const screens = [
    ['/dashboard', 'dashboard-en.png', 'Dashboard'], ['/products', 'products-en.png', 'Products'],
    ['/orders', 'orders-en.png', 'Orders'], ['/inventory', 'inventory-en.png', 'Inventory'],
    ['/warehouses', 'warehouses-en.png', 'Warehouses'], ['/purchase-orders', 'purchase-orders-en.png', 'Purchase orders'],
    ['/boms', 'manufacturing-en.png', 'Bill of Materials'], [`/production-orders/${production.id}`, 'production-detail-en.png', production.productionOrderNumber],
  ];
  for (const [url, file, heading] of screens) {
    await page.goto(url);
    await expect(page.getByRole('heading', { name: heading, exact: true })).toBeVisible();
    if (url === '/dashboard') { await expect(page.locator('.kpi-card')).toHaveCount(8); await expect(page.locator('.dashboard-operations .stat')).toHaveCount(5); await expect(page.getByText('Current purchasing totals. Outstanding quantity excludes drafts and cancelled orders.')).toBeVisible(); }
    else await expect(page.getByRole('status', { name: '' }).filter({ hasText: 'Loading workspace…' })).toHaveCount(0);
    await page.evaluate(() => document.fonts.ready);
    await page.screenshot({ path: path.join(output, file), fullPage: true });
  }
  for (const width of [1440, 1024, 768, 390]) {
    await page.setViewportSize({ width, height: 1000 });
    for (const url of ['/dashboard', '/products', '/products/new', `/orders/${pending.id}`, '/warehouses', '/purchase-orders', '/boms/new', `/production-orders/${production.id}`]) {
      await page.goto(url);
      await expect(page.locator('main h1')).toBeVisible();
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${url} at ${width}px`).toBe(true);
      await page.screenshot({ path: path.join(output, `qa-${url === "/products/new" ? "product-form" : url.split('/')[1]}-${width}.png`), fullPage: true });
    }
  }
  await page.getByRole('button', { name: 'TR', exact: true }).click();
  await expect(page.getByText('Üretim Tamamlandı', { exact: false })).toBeVisible();
  await page.screenshot({ path: path.join(output, 'production-detail-tr-mobile.png'), fullPage: true });
});
