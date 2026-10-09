// Actual application screenshots with synthetic records. Run on a fresh,
// disposable Compose stack; this script intentionally does not delete orders.
import { chromium, expect } from '@playwright/test';
import { mkdir } from 'node:fs/promises';
import path from 'node:path';

const api = (process.env.API_URL ?? 'http://localhost:5080') + '/api';
const site = process.env.FRONTEND_URL ?? 'http://localhost:3000';
const output = process.env.SCREENSHOT_DIR ?? '/work/screenshots';
await mkdir(output, { recursive: true });
const email = `portfolio-${Date.now()}@example.com`;
const password = 'SyntheticCaptureOnly123!';
let token;
async function post(route, body, method = 'POST') {
  const response = await fetch(api + route, {
    method, headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: JSON.stringify(body),
  });
  if (!response.ok) throw new Error(`${route}: ${response.status}`);
  return response.json();
}
token = (await post('/auth/login', { email: process.env.BOOTSTRAP_ADMIN_EMAIL, password: process.env.BOOTSTRAP_ADMIN_PASSWORD })).accessToken;
await post('/users', { name: 'Portfolio Reviewer', email, password, role: 'Admin' });
token = (await post('/auth/login', { email, password })).accessToken;
const office = await post('/categories', { name: 'Office essentials', description: 'Everyday supplies for a productive workspace.', isActive: true });
const equipment = await post('/categories', { name: 'Workspace equipment', description: 'Comfortable and practical office equipment.', isActive: true });
const products = [];
for (const [name, description, price, categoryId, isActive] of [
  ['A5 notebook', 'Dotted pages with a durable cover.', 12.50, office.id, true],
  ['Writing pen set', 'A set of three smooth-writing pens.', 8.75, office.id, true],
  ['Desk lamp', 'Adjustable warm-light desk lamp.', 49.90, equipment.id, true],
  ['Laptop stand', 'Aluminium stand for an ergonomic desk.', 39.00, equipment.id, true],
  ['Archive folder', 'Retired folder model, kept for reference.', 4.25, office.id, false],
]) products.push(await post('/products', { name, description, price, categoryId, isActive }));
for (const product of products) await post(`/inventory/${product.id}/stock-in`, { quantity: 50, reason: 'Synthetic portfolio supply' });
const customers = [];
for (const [name, customerEmail, address] of [
  ['Northwind Studio', 'studio@example.com', 'Istanbul — synthetic demo address'],
  ['Bluebird Design', 'design@example.com', 'Ankara — synthetic demo address'],
]) customers.push(await post('/customers', { name, email: customerEmail, phone: '', address, isActive: true }));
await post('/orders', { customerId: customers[0].id, items: [{ productId: products[2].id, quantity: 2 }] });
for (const [customerId, productId, quantity] of [[customers[0].id, products[2].id, 3], [customers[1].id, products[3].id, 4], [customers[0].id, products[0].id, 5]]) {
  const order = await post('/orders', { customerId, items: [{ productId, quantity }] });
  await post(`/orders/${order.id}/status`, { status: 'Confirmed' }, 'PUT');
  await post(`/orders/${order.id}/status`, { status: 'Completed' }, 'PUT');
}
await post(`/inventory/${products[0].id}/minimum-level`, { minimumStockLevel: 45 }, 'PUT');

const browser = await chromium.launch();
try {
  const page = await browser.newPage({ viewport: { width: 1440, height: 1000 }, deviceScaleFactor: 1 });
  async function capture(file) {
    await page.evaluate(() => document.fonts.ready);
    await page.screenshot({ path: path.join(output, file), fullPage: true });
    console.log(`Captured ${file}`);
  }
  await page.goto(site + '/auth/login');
  await expect(page.getByRole('button', { name: 'Sign in', exact: true })).toBeVisible();
  await capture('login-en.png');
  await page.getByLabel('Email address').fill(email);
  await page.getByLabel('Password', { exact: true }).fill(password);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page).toHaveURL(/\/dashboard$/);
  await expect(page.locator('.kpi-card')).toHaveCount(8);
  await expect(page.getByRole('heading', { name: 'Top customers', exact: true })).toBeVisible();
  await capture('dashboard-en.png');
  await page.getByRole('button', { name: 'TR', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Gösterge Paneli', exact: true })).toBeVisible();
  await capture('dashboard-tr.png');
  await page.getByRole('button', { name: 'EN', exact: true }).click();
  await page.goto(site + '/reports/sales');
  await expect(page.locator('tbody tr').first()).toBeVisible();
  await capture('sales-report-en.png');
  await page.goto(site + '/reports/inventory');
  await page.getByRole('button', { name: 'TR', exact: true }).click();
  await expect(page.locator('tbody tr').first()).toBeVisible();
  await capture('inventory-report-tr.png');
  await page.getByRole('button', { name: 'EN', exact: true }).click();
  await page.goto(site + '/products');
  await expect(page.getByText('A5 notebook', { exact: true }).filter({ visible: true })).toBeVisible();
  await capture('products-en.png');
  for (const [route, heading, record] of [
    ['categories', 'Categories', 'Office essentials'],
    ['customers', 'Customers', 'Northwind Studio'],
    ['orders', 'Orders', 'Northwind Studio'],
  ]) {
    await page.goto(`${site}/${route}`);
    await expect(page.getByRole('heading', { name: heading, exact: true })).toBeVisible();
    await expect(page.getByText(record, { exact: true }).filter({ visible: true }).first()).toBeVisible();
    await capture(`${route}-en.png`);
  }
  await page.goto(site + '/orders/new');
  await page.getByLabel('Customer', { exact: false }).selectOption(customers[1].id);
  await page.getByLabel('Product name', { exact: false }).first().selectOption(products[0].id);
  await page.getByLabel('Quantity', { exact: false }).first().fill('4');
  await page.getByRole('button', { name: 'Add item', exact: false }).click();
  await page.getByLabel('Product name', { exact: false }).nth(1).selectOption(products[1].id);
  await page.getByLabel('Quantity', { exact: false }).nth(1).fill('2');
  await expect(page.locator('.order-total')).toContainText('$67.50');
  await capture('order-create-en.png');
  await page.getByRole('button', { name: 'Create order', exact: true }).click();
  await expect(page.getByRole('heading', { name: /^ORD-/ })).toBeVisible();
  await page.getByRole('button', { name: 'Confirm', exact: true }).click();
  await expect(page.locator('.badge')).toHaveText('Confirmed');
  await page.getByRole('button', { name: 'TR', exact: true }).click();
  await expect(page.locator('.badge')).toHaveText('Onaylandı');
  await capture('order-detail-tr.png');
  await page.getByRole('navigation').getByRole('link', { name: 'Ürünler', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Ürünler', exact: true })).toBeVisible();
  await expect(page.getByText('A5 notebook', { exact: true }).filter({ visible: true })).toBeVisible();
  await capture('products-tr.png');
  await page.goto(site + '/inventory');
  await expect(page.getByRole('heading', { name: 'Stok', exact: true })).toBeVisible();
  await expect(page.getByText('A5 notebook', { exact: true }).filter({ visible: true })).toBeVisible();
  await capture('inventory-tr.png');
  await page.getByRole('button', { name: 'EN', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Inventory', exact: true })).toBeVisible();
  await capture('inventory-en.png');
  await page.goto(`${site}/inventory/${products[0].id}`);
  await expect(page.getByRole('heading', { name: 'Movement history', exact: true })).toBeVisible();
  await expect(page.getByText('Synthetic portfolio supply', { exact: true }).filter({ visible: true })).toBeVisible();
  await capture('inventory-detail-en.png');
  const manager = await post('/users', { name: 'Avery Morgan', email: 'avery-reviewer@example.com', password, role: 'Manager' });
  await post('/users', { name: 'Jordan Taylor', email: 'jordan-reviewer@example.com', password, role: 'Viewer' });
  await post(`/users/${manager.id}/role`, { role: 'Viewer' }, 'PUT');
  await post(`/users/${manager.id}/role`, { role: 'Manager' }, 'PUT');
  await page.goto(site + '/users');
  await expect(page.getByRole('heading', { name: 'Users', exact: true })).toBeVisible();
  await expect(page.getByText('Avery Morgan', { exact: true }).filter({ visible: true })).toBeVisible();
  await capture('users-en.png');
  await page.getByRole('button', { name: 'TR', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Kullanıcılar', exact: true })).toBeVisible();
  await capture('users-tr.png');
  await page.getByRole('button', { name: 'EN', exact: true }).click();
  await page.goto(site + '/audit-logs');
  await expect(page.getByRole('link', { name: 'Details', exact: true }).first()).toBeVisible();
  await capture('audit-logs-en.png');
  await page.getByRole('button', { name: 'TR', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'İşlem Geçmişi', exact: true })).toBeVisible();
  await capture('audit-logs-tr.png');
  await page.getByRole('button', { name: 'EN', exact: true }).click();
  await page.getByLabel('Action', { exact: true }).selectOption('RoleChange');
  await expect(page.getByRole('row').filter({ hasText: 'Role change' }).first()).toBeVisible();
  await page.getByRole('link', { name: 'Details', exact: true }).first().click();
  await expect(page.getByRole('heading', { name: 'Audit details', exact: true })).toBeVisible();
  await capture('audit-detail-en.png');
} finally { await browser.close(); }
