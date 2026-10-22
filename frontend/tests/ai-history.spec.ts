import { test, expect } from '@playwright/test';

const id = '11111111-1111-1111-1111-111111111111';
async function session(page: import('@playwright/test').Page) {
  await page.route('**/api/dashboard/**', route => route.abort());
  await page.goto('/auth/login');
  await page.evaluate(() => {
    localStorage.setItem('catalog.language', 'en');
    sessionStorage.setItem('catalog.session', JSON.stringify({ accessToken: 'history-fixture', expiresAt: new Date(Date.now() + 3600000).toISOString(), user: { id: 'history-user', name: 'History user', email: 'history@example.com', role: 'Viewer', isActive: true, createdAt: '2026-10-20T00:00:00Z', updatedAt: null } }));
  });
  await page.route('**/api/ai/status', route => route.fulfill({ json: { enabled: true, configured: true, available: true, isMock: true, retentionDays: 7 } }));
}
test('AI history is opt in, resumes from server and can be deleted', async ({ page }) => {
  await session(page); let saved = false; let deleted = false;
  await page.route('**/api/ai/conversations', route => route.fulfill({ json: saved && !deleted ? [{ id, createdAt: '2026-10-20T10:00:00Z', expiresAt: '2026-10-27T10:00:00Z', turns: 1 }] : [] }));
  await page.route(`**/api/ai/conversations/${id}`, route => {
    if (route.request().method() === 'DELETE') { deleted = true; return route.fulfill({ status: 204 }); }
    return route.fulfill({ json: { id, expiresAt: '2026-10-27T10:00:00Z', messages: [
      { role: 'user', content: 'Remember inventory', language: 'en', sources: [], createdAt: '2026-10-20T10:00:00Z' },
      { role: 'assistant', content: 'Saved mock response', language: 'en', sources: [], createdAt: '2026-10-20T10:00:00Z' },
    ] } });
  });
  await page.route('**/api/ai/chat', route => {
    expect(route.request().postDataJSON().remember).toBe(true); saved = true;
    return route.fulfill({ json: { answer: 'Saved mock response', language: 'en', sources: [], generatedAtUtc: '2026-10-20T10:00:00Z', requestId: 'history', conversationId: id, isMock: true } });
  });
  await page.goto('/ai-assistant');
  const optin = page.getByRole('checkbox', { name: 'Save conversation history on the server' }); await expect(optin).not.toBeChecked();
  await expect(page.getByText('Development mock — no live language model is connected.')).toBeVisible();
  await optin.check(); await page.getByLabel('Your business question').fill('Remember inventory'); await page.getByRole('button', { name: 'Send question' }).click();
  await expect(page.getByText('Saved mock response', { exact: true })).toBeVisible();
  await page.reload(); await expect(page.getByText('Saved mock response', { exact: true })).toHaveCount(0);
  await page.getByRole('checkbox', { name: 'Save conversation history on the server' }).check();
  await page.getByLabel('Saved conversations').selectOption(id);
  await expect(page.getByText('Saved mock response', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Clear conversation' }).click(); await expect(page.getByText('Saved mock response', { exact: true })).toHaveCount(0); expect(deleted).toBe(true);
  expect(await page.evaluate(() => [...Object.keys(localStorage), ...Object.keys(sessionStorage)].some(k => /conversation|chat/i.test(k)))).toBe(false);
});
test('AI history errors and Turkish retention disclosure are accessible', async ({ page }) => {
  await session(page); await page.route('**/api/ai/conversations', route => route.fulfill({ status: 503, json: { code: 'aiProviderUnavailable' } }));
  await page.goto('/ai-assistant'); await page.getByRole('checkbox', { name: 'Save conversation history on the server' }).check();
  await expect(page.getByRole('alert')).toHaveText('The AI provider is unavailable. Please try again later.');
  await page.getByRole('button', { name: 'TR', exact: true }).click();
  await expect(page.getByRole('checkbox', { name: 'Konuşma geçmişini sunucuda sakla' })).toBeChecked();
  await expect(page.getByText('Saklama süresi (gün): 7', { exact: false })).toBeVisible();
  await page.setViewportSize({ width: 390, height: 844 }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});
