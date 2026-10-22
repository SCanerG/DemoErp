import { test, expect } from '@playwright/test';
import { createAccount } from './admin-helper';

test('Development mock uses real authorized reports and preserves private history across reload', async ({ page }) => {
  test.skip(process.env.AI_DEVELOPMENT_MOCK !== 'true', 'Requires an isolated Development Mock stack; never calls a paid provider.');
  const viewer = await createAccount(page.request, 'Viewer');
  await page.goto('/auth/login'); await page.getByLabel('Email address').fill(viewer.email); await page.getByLabel('Password', { exact: true }).fill(viewer.password);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click(); await expect(page).toHaveURL(/\/dashboard$/);
  await page.getByRole('link', { name: 'AI Assistant', exact: true }).click();
  await expect(page.getByText('Development mock — no live language model is connected.')).toBeVisible();
  await page.getByRole('checkbox', { name: 'Save conversation history on the server' }).check();
  await page.getByLabel('Your business question').fill('Show inventory overview.');
  await page.getByRole('button', { name: 'Send question', exact: true }).click();
  await expect(page.locator('.ai-answer')).toBeVisible(); await expect(page.locator('.ai-answer').getByText('Development mock', { exact: true })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Inventory overview', exact: true })).toBeVisible();
  const api = `${process.env.API_URL}/api`;
  const headers = { Authorization: `Bearer ${viewer.session.accessToken}` };
  const saved = await page.request.get(`${api}/ai/conversations`, { headers }); expect(saved.status()).toBe(200); expect(saved.headers()['cache-control']).toContain('no-store');
  const rows = await saved.json(); expect(rows).toHaveLength(1); expect(rows[0].turns).toBe(1);
  await page.reload(); await expect(page.locator('.ai-answer')).toHaveCount(0);
  await page.getByRole('checkbox', { name: 'Save conversation history on the server' }).check();
  await page.getByLabel('Saved conversations').selectOption(rows[0].id);
  await expect(page.locator('.ai-answer').getByText('Development mock', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Clear conversation', exact: true }).click(); await expect(page.locator('.ai-answer')).toHaveCount(0);
  expect((await page.request.get(`${api}/ai/conversations/${rows[0].id}`, { headers })).status()).toBe(404);
});
