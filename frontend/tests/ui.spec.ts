import { test, expect } from '@playwright/test';

const product = { id: '00000000-0000-0000-0000-000000000123', name: 'Studio notebook',
  description: 'A compact notebook for daily ideas.', price: 32.50, isActive: true,
  categoryId: '00000000-0000-0000-0000-000000000124', categoryName: 'Stationery',
  createdAt: '2026-10-07T12:00:00Z', updatedAt: null };

test('mobile catalog exposes all actions without scrolling horizontally', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/auth/login');
  await page.evaluate(() => sessionStorage.setItem('catalog.session', JSON.stringify({ accessToken: 'ui-fixture', expiresAt: new Date(Date.now() + 3600000).toISOString(), user: { id: 'fixture', name: 'UI fixture', email: 'fixture@example.com', role: 'Admin', isActive: true, createdAt: '2026-10-01T00:00:00Z', updatedAt: null } })));
  await page.route('**/api/products', route => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([product]) }));
  await page.goto('/products');
  const card = page.getByRole('list', { name: 'Products', exact: true }).getByRole('listitem');
  await expect(card).toContainText('Studio notebook');
  await expect(card).toContainText('$32.50');
  await expect(card).toContainText('Active');
  for (const name of ['View', 'Edit']) {
    const action = card.getByRole('link', { name, exact: true });
    await expect(action).toBeVisible();
    const box = await action.boundingBox();
    expect(box?.x).toBeGreaterThanOrEqual(0);
    expect((box?.x ?? 0) + (box?.width ?? 0)).toBeLessThanOrEqual(390);
  }
  await expect(card.getByRole('button', { name: 'Delete Studio notebook' })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  if (process.env.CAPTURE_SCREENSHOTS) await page.screenshot({ path: '../.tools/catalog-mobile-improved.png', fullPage: true });
});

test('delete dialog traps keyboard focus, closes on Escape, and restores focus', async ({ page }) => {
  await page.goto('/auth/login');
  await page.evaluate(() => sessionStorage.setItem('catalog.session', JSON.stringify({ accessToken: 'ui-fixture', expiresAt: new Date(Date.now() + 3600000).toISOString(), user: { id: 'fixture', name: 'UI fixture', email: 'fixture@example.com', role: 'Admin', isActive: true, createdAt: '2026-10-01T00:00:00Z', updatedAt: null } })));
  await page.route('**/api/products', route => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([product]) }));
  await page.goto('/products');
  const trigger = page.getByRole('button', { name: 'Delete Studio notebook' });
  await trigger.click();
  const dialog = page.getByRole('dialog', { name: 'Delete product?' });
  await expect(dialog).toBeVisible();
  await expect(dialog.getByRole('button', { name: 'Cancel' })).toBeFocused();
  await page.keyboard.press('Shift+Tab');
  await expect(dialog.getByRole('button', { name: 'Delete product', exact: true })).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(dialog.getByRole('button', { name: 'Cancel' })).toBeFocused();
  if (process.env.CAPTURE_SCREENSHOTS) await page.screenshot({ path: '../.tools/catalog-delete-dialog.png', fullPage: true });
  await page.keyboard.press('Escape');
  await expect(dialog).toHaveCount(0);
  await expect(trigger).toBeFocused();
});
