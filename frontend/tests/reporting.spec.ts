import { test, expect } from '@playwright/test';
import { adminSession, createAccount } from './admin-helper';

test.use({ timezoneId: 'Europe/Istanbul' });
const base = `${process.env.API_URL ?? 'http://localhost:5080'}/api`;
test('real reporting aggregates, date filters, pagination, CSV, Viewer access and mobile EN/TR', async ({ page }) => {
  test.setTimeout(90_000); // Includes 23 real order creations and 24 transactional status changes.
  const admin = await adminSession(page.request); const headers = { Authorization: `Bearer ${admin.accessToken}` };
  const suffix = Date.now();
  const create = async (path: string, data: object) => { const r = await page.request.post(`${base}/${path}`, { headers, data }); expect(r.status()).toBe(201); return r.json(); };
  const category = await create('categories', { name: `Report category ${suffix}`, description: '', isActive: true });
  const product = await create('products', { name: `=Çay, "özel" ${suffix}`, description: '', price: 25, isActive: true, categoryId: category.id });
  const customer = await create('customers', { name: `Report customer ${suffix}`, email: '', phone: '', address: '', isActive: true });
  expect((await page.request.post(`${base}/inventory/${product.id}/stock-in`, { headers, data: { quantity: 100, reason: 'Reporting browser setup' } })).status()).toBe(200);
  for (let i = 0; i < 23; i++) {
    const order = await create('orders', { customerId: customer.id, items: [{ productId: product.id, quantity: 1 }] });
    if (i < 12) for (const status of ['Confirmed', 'Completed']) expect((await page.request.put(`${base}/orders/${order.id}/status`, { headers, data: { status } })).status()).toBe(200);
  }
  const viewer = await createAccount(page.request, 'Viewer');
  await page.goto('/auth/login');
  await page.getByLabel('Email address').fill(viewer.email); await page.getByLabel('Password', { exact: true }).fill(viewer.password);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page).toHaveURL(/\/dashboard$/); await expect(page.locator('.kpi-card')).toHaveCount(8);
  await expect(page.getByRole('heading', { name: 'Top 10 selling products' })).toBeVisible();
  const boundary = await page.evaluate(() => {
    const inputs = [...document.querySelectorAll<HTMLInputElement>('input[type="date"]')];
    const parse = (s: string, next: number) => { const [y, m, d] = s.split('-').map(Number); return new Date(y, m - 1, d + next).toISOString(); };
    return { start: parse(inputs[0].value, 0), end: parse(inputs[1].value, 1) };
  });
  expect(new Date(boundary.start).getUTCHours()).toBe(21); // Istanbul local midnight is 21:00 UTC on the preceding day.
  const summaryResponse = await page.request.get(`${base}/dashboard/summary?${new URLSearchParams(boundary)}`, { headers }); expect(summaryResponse.status()).toBe(200);
  const summary = await summaryResponse.json();
  await expect(page.locator('.kpi-card').first()).toContainText(new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(summary.totalRevenue));
  await expect(page.locator('.kpi-card').nth(1).locator('strong')).toHaveText(String(summary.totalOrders));
  await page.screenshot({ path: '../.tools/phase5/dashboard-en.png', fullPage: true });
  await page.getByLabel('Reporting period').selectOption('7'); await expect(page.getByLabel('Reporting period')).toHaveValue('7');
  await page.getByLabel('Start date').fill('1990-01-01'); await page.getByLabel('End date').fill('1990-01-03');
  await expect(page.locator('.kpi-card').first()).toContainText('$0.00'); await expect(page.getByText('No results for these filters.').first()).toBeVisible();
  await page.getByLabel('Reporting period').selectOption('30');
  await page.getByRole('button', { name: 'TR', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Gösterge Paneli', exact: true })).toBeVisible();
  await page.setViewportSize({ width: 390, height: 844 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.screenshot({ path: '../.tools/phase5/dashboard-tr-mobile.png', fullPage: true });
  await page.getByRole('button', { name: 'EN', exact: true }).click(); await page.setViewportSize({ width: 1280, height: 800 });
  await page.getByRole('link', { name: 'View reports', exact: true }).click();
  await page.getByLabel('Customer', { exact: true }).selectOption(customer.id);
  await expect(page.getByText('Total records: 23')).toBeVisible(); await expect(page.locator('tbody tr')).toHaveCount(20);
  await page.getByRole('button', { name: 'Next', exact: true }).click(); await expect(page.locator('tbody tr')).toHaveCount(3);
  await page.getByLabel('Rows per page').selectOption('50'); await expect(page.locator('tbody tr')).toHaveCount(23);
  await page.getByLabel('Status', { exact: true }).selectOption('Completed'); await expect(page.getByText('Total records: 12')).toBeVisible();
  await page.screenshot({ path: '../.tools/phase5/sales-report-en.png', fullPage: true });
  const downloadPromise = page.waitForEvent('download'); await page.getByRole('button', { name: 'Export CSV' }).click(); const download = await downloadPromise;
  expect(download.suggestedFilename()).toBe('sales.csv'); const stream = await download.createReadStream(); const chunks: Buffer[] = []; for await (const c of stream!) chunks.push(Buffer.from(c));
  const csv = Buffer.concat(chunks).toString('utf8'); expect(csv).toContain(customer.name); expect(csv).toContain('Completed'); expect(csv).not.toContain('Pending');
  await page.getByRole('link', { name: 'Product performance', exact: true }).click(); await page.getByLabel('Product', { exact: true }).selectOption(product.id);
  await expect(page.locator('tbody tr')).toHaveCount(1); await expect(page.locator('tbody tr')).toContainText('$300.00'); await expect(page.locator('tbody tr')).toContainText('88');
  await page.getByRole('link', { name: 'Customer report', exact: true }).click(); await page.getByLabel('Customer', { exact: true }).selectOption(customer.id);
  await expect(page.locator('tbody tr')).toContainText('$300.00'); await expect(page.locator('tbody tr')).toContainText('$25.00');
  await page.getByRole('link', { name: 'Inventory report', exact: true }).click(); await page.getByLabel('Category', { exact: true }).selectOption(category.id);
  await expect(page.getByLabel('Start date')).toHaveCount(0); await expect(page.locator('tbody tr')).toContainText('88');
  await page.getByLabel('Stock status').selectOption('Out'); await expect(page.getByText('No results for these filters.')).toBeVisible();
  await page.getByLabel('Stock status').selectOption('Healthy'); await expect(page.locator('tbody tr')).toHaveCount(1);
  await page.getByRole('button', { name: 'TR', exact: true }).click(); await page.setViewportSize({ width: 390, height: 844 });
  await expect(page.getByRole('heading', { name: 'Stok raporu' })).toBeVisible(); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.screenshot({ path: '../.tools/phase5/inventory-report-tr-mobile.png', fullPage: true });
  expect((await page.request.get(`${base}/audit-logs`, { headers: { Authorization: `Bearer ${viewer.session.accessToken}` } })).status()).toBe(403);
});

test('report errors, retry and invalid dates remain readable', async ({ page }) => {
  const viewer = await createAccount(page.request, 'Viewer');
  await page.goto('/auth/login'); await page.evaluate(s => sessionStorage.setItem('catalog.session', JSON.stringify(s)), viewer.session);
  await page.route('**/api/reports/sales?**', route => route.fulfill({ status: 500, contentType: 'application/problem+json', body: JSON.stringify({ title: 'Please try again later.' }) }));
  await page.goto('/reports/sales'); await expect(page.getByRole('alert')).toBeVisible();
  await page.unroute('**/api/reports/sales?**'); await page.getByRole('button', { name: 'Try again' }).click(); await expect(page.getByText('Total records:')).toBeVisible();
  await page.getByLabel('Start date').fill('2020-01-01'); await expect(page.getByRole('alert')).toContainText('Check report filters');
  await page.getByRole('button', { name: 'TR', exact: true }).click(); await expect(page.getByRole('alert')).toContainText('Rapor filtrelerini kontrol edin');
});
