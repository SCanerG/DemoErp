import { test, expect, type Page } from '@playwright/test';
import { adminSession, createAccount } from './admin-helper';
import type { Session } from '../src/types';

const base = `${process.env.API_URL ?? 'http://localhost:5080'}/api`;
async function enter(page: Page, session: Session, route = '/products') {
  await page.goto('/auth/login'); await page.evaluate(value => sessionStorage.setItem('catalog.session', JSON.stringify(value)), session); await page.goto(route);
}

test('actual Viewer Manager Admin permissions, denied routes and mobile Turkish navigation', async ({ page }) => {
  const admin = await adminSession(page.request); const viewer = await createAccount(page.request, 'Viewer'); const manager = await createAccount(page.request, 'Manager');
  await enter(page, viewer.session);
  await expect(page.getByRole('heading', { name: 'Products', exact: true })).toBeVisible();
  for (const name of ['Users', 'Audit Logs']) await expect(page.getByRole('navigation').getByRole('link', { name, exact: true })).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'New product', exact: true })).toHaveCount(0);
  expect((await page.request.post(`${base}/products`, { headers: { Authorization: `Bearer ${viewer.session.accessToken}` }, data: {} })).status()).toBe(403);
  await page.goto('/products/new'); await expect(page.getByRole('heading', { name: 'Access Denied', exact: true })).toBeVisible();
  await page.goto('/users'); await expect(page.getByRole('heading', { name: 'Access Denied', exact: true })).toBeVisible();
  const inventory = await (await page.request.get(`${base}/inventory`, { headers: { Authorization: `Bearer ${admin.accessToken}` } })).json();
  if (inventory.length) { await page.goto(`/inventory/${inventory[0].productId}`); await expect(page.getByRole('heading', { name: 'Movement history', exact: true })).toBeVisible(); await expect(page.getByRole('group', { name: 'Inventory operations' })).toHaveCount(0); }
  await enter(page, manager.session); await expect(page.getByRole('link', { name: 'New product', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: /^Delete / })).toHaveCount(0);
  expect((await page.request.get(`${base}/users`, { headers: { Authorization: `Bearer ${manager.session.accessToken}` } })).status()).toBe(403);
  await page.goto('/audit-logs'); await expect(page.getByRole('heading', { name: 'Access Denied', exact: true })).toBeVisible();
  await enter(page, admin, '/users'); await expect(page.getByRole('heading', { name: 'Users', exact: true })).toBeVisible();
  const self = page.getByRole('row').filter({ hasText: admin.user.email });
  await expect(self.getByRole('combobox')).toBeDisabled(); await expect(self.getByRole('button', { name: 'Deactivate', exact: true })).toBeDisabled();
  await page.setViewportSize({ width: 390, height: 844 }); await page.getByRole('button', { name: 'TR', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Kullanıcılar', exact: true })).toBeVisible();
  await expect(page.getByRole('navigation').getByRole('link', { name: 'İşlem Geçmişi', exact: true })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.screenshot({ path: '../.tools/phase4/users-tr-mobile.png', fullPage: true });
});

test('Admin user forms, explicit Admin creation, role/status confirmations, audit details and revoked sessions', async ({ page }) => {
  const admin = await adminSession(page.request); const suffix = Date.now(); const email = `ui-managed-${suffix}@example.com`; const password = 'UserManagementTest123!';
  await enter(page, admin, '/users/new');
  await page.getByRole('button', { name: 'Create user', exact: true }).click(); await expect(page.getByText('Enter a name.', { exact: true })).toBeVisible();
  await page.getByLabel('Name', { exact: false }).fill(`Managed ${suffix}`); await page.getByLabel('Email', { exact: false }).fill(email);
  await page.getByLabel('Password', { exact: false }).fill(password); await page.getByLabel('Role', { exact: false }).selectOption('Manager');
  await page.getByRole('button', { name: 'Create user', exact: true }).click(); await expect(page).toHaveURL(/\/users$/);
  const loginResponse = await page.request.post(`${base}/auth/login`, { data: { email, password } }); expect(loginResponse.status()).toBe(200); const manager = await loginResponse.json() as Session;
  const row = page.getByRole('row').filter({ hasText: email });
  await row.getByRole('link', { name: 'Edit', exact: true }).click(); await page.getByLabel('Name', { exact: false }).fill(`Updated ${suffix}`);
  await page.getByRole('button', { name: 'Save changes', exact: true }).click(); await expect(page).toHaveURL(/\/users$/);
  await row.getByRole('combobox').selectOption('Viewer'); await expect(page.getByRole('dialog')).toContainText('Existing sessions will be revoked.');
  await page.getByRole('dialog').getByRole('button', { name: 'Confirm change', exact: true }).click(); await expect(page.getByRole('dialog')).not.toBeVisible();
  expect((await page.request.get(`${base}/products`, { headers: { Authorization: `Bearer ${manager.accessToken}` } })).status()).toBe(401);
  await row.getByRole('button', { name: 'Deactivate', exact: true }).click(); await page.getByRole('dialog').getByRole('button', { name: 'Confirm change', exact: true }).click(); await expect(page.getByRole('dialog')).not.toBeVisible();
  expect((await page.request.post(`${base}/auth/login`, { data: { email, password } })).status()).toBe(401);
  await row.getByRole('button', { name: 'Activate', exact: true }).click(); await page.getByRole('dialog').getByRole('button', { name: 'Confirm change', exact: true }).click(); await expect(page.getByRole('dialog')).not.toBeVisible();
  await page.getByRole('navigation').getByRole('link', { name: 'Audit Logs', exact: true }).click();
  await page.getByLabel('Action', { exact: true }).selectOption('RoleChange'); await page.getByLabel('Entity', { exact: true }).selectOption('User');
  await expect(page.getByRole('link', { name: 'Details', exact: true }).first()).toBeVisible(); await page.getByRole('link', { name: 'Details', exact: true }).first().click();
  await expect(page.getByRole('heading', { name: 'Audit details', exact: true })).toBeVisible(); await expect(page.getByRole('cell', { name: 'Manager', exact: true })).toBeVisible(); await expect(page.getByRole('cell', { name: 'Viewer', exact: true })).toBeVisible();
  await expect(page.getByText('Correlation ID', { exact: true })).toBeVisible();
  await page.screenshot({ path: '../.tools/phase4/audit-detail-en.png', fullPage: true });
  await page.goto('/users/new'); await page.getByLabel('Name', { exact: false }).fill(`Additional Admin ${suffix}`); await page.getByLabel('Email', { exact: false }).fill(`admin-${suffix}@example.com`); await page.getByLabel('Password', { exact: false }).fill(password); await page.getByLabel('Role', { exact: false }).selectOption('Admin');
  await page.getByRole('button', { name: 'Create user', exact: true }).click(); await expect(page.getByRole('dialog')).toContainText('full administrative access');
  await page.getByRole('dialog').getByRole('button', { name: 'Create Admin', exact: true }).click(); await expect(page).toHaveURL(/\/users$/);
  const viewerLogin = await (await page.request.post(`${base}/auth/login`, { data: { email, password } })).json() as Session;
  expect((await page.request.put(`${base}/users/${viewerLogin.user.id}/status`, { headers: { Authorization: `Bearer ${admin.accessToken}` }, data: { isActive: false } })).status()).toBe(200);
  await enter(page, viewerLogin); await expect(page).toHaveURL(/\/auth\/login$/);
});

test('administrative filters paginate and localize empty/error states without treating 403 as logout', async ({ page }) => {
  const admin = await adminSession(page.request); await enter(page, admin, '/audit-logs');
  await expect(page.getByRole('heading', { name: 'Audit Logs', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Next', exact: true })).toBeEnabled(); await page.getByRole('button', { name: 'Next', exact: true }).click(); await expect(page.getByText(/^Page 2/)).toBeVisible();
  await page.getByLabel('From date', { exact: true }).fill('2099-01-01T00:00'); await expect(page.getByText('No audit records match these filters.', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'TR', exact: true }).click(); await expect(page.getByText('Bu filtrelere uyan işlem kaydı yok.', { exact: true })).toBeVisible();
  await page.route('**/api/audit-logs?*', route => route.fulfill({ status: 403, contentType: 'application/problem+json', body: JSON.stringify({ status: 403, title: 'Forbidden' }) }));
  await page.getByRole('button', { name: 'Filtreleri temizle', exact: true }).click(); await page.reload(); await expect(page.getByText('Bu işlemi yapma yetkiniz yok.', { exact: true })).toBeVisible();
  expect(await page.evaluate(() => !!sessionStorage.getItem('catalog.session'))).toBe(true);
});
