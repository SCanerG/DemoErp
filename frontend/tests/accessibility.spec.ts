import AxeBuilder from '@axe-core/playwright';
import { test, expect, type Page } from '@playwright/test';
import { adminSession } from './admin-helper';

async function scan(page: Page) {
  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze();
  expect(results.violations.map(v => ({ id: v.id, impact: v.impact, nodes: v.nodes.map(n => n.target) }))).toEqual([]);
}
test('accessibility: login dashboard list form dialog order detail', async ({ page, request }) => {
  await page.goto('/auth/login');
  await expect(page.getByRole('heading', { name: 'Welcome back' })).toBeVisible();
  await scan(page);
  const session = await adminSession(request);
  const base = `${process.env.API_URL ?? 'http://localhost:5080'}/api`;
  const headers = { Authorization: `Bearer ${session.accessToken}` };
  const post = async (path: string, data: unknown) => { const r = await request.post(base + path, { headers, data }); expect(r.ok(), await r.text()).toBe(true); return r.json(); };
  const suffix = Date.now();
  const category = await post('/categories', { name: `A11y ${suffix}`, description: '', isActive: true });
  const product = await post('/products', { name: `A11y product ${suffix}`, description: '', categoryId: category.id, productType: 'RawMaterial', price: 10, isActive: true });
  const customer = await post('/customers', { name: `A11y customer ${suffix}`, email: 'accessibility@example.com', phone: '', address: '', isActive: true });
  const order = await post('/orders', { customerId: customer.id, items: [{ productId: product.id, quantity: 1 }] });
  await page.evaluate(s => sessionStorage.setItem('catalog.session', JSON.stringify(s)), session);
  for (const [path, heading] of [['/dashboard', 'Dashboard'], ['/products', 'Products'], ['/products/new', 'New product'], [`/orders/${order.id}`, order.orderNumber]]) {
    await page.goto(path);
    await expect(page.getByRole('heading', { name: heading, exact: true })).toBeVisible();
    await scan(page);
  }
  await page.goto('/products');
  await page.getByRole('button', { name: `Delete ${product.name}`, exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'Delete product?' })).toBeVisible();
  await scan(page);
  await page.keyboard.press('Escape');
  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByRole('button', { name: 'Open navigation' }).click();
  await scan(page);
});
