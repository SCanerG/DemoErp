import { test, expect, type Page } from '@playwright/test';
import { createAccount } from './admin-helper';

async function fixtureSession(page: Page, language = 'en') {
  await page.route('**/api/dashboard/**', route => route.abort());
  await page.goto('/auth/login');
  await page.evaluate(lang => {
    localStorage.setItem('catalog.language', lang);
    sessionStorage.setItem('catalog.session', JSON.stringify({ accessToken: 'ai-ui-fixture', expiresAt: new Date(Date.now() + 3600000).toISOString(), user: { id: 'ai-fixture', name: 'AI fixture', email: 'fixture@example.com', role: 'Viewer', isActive: true, createdAt: '2026-10-01T00:00:00Z', updatedAt: null } }));
  }, language);
  await page.route('**/api/ai/status', route => route.fulfill({ json: { enabled: true, configured: true, available: true } }));
}

test('AI real Viewer navigation and default-disabled Docker require no provider key', async ({ page }) => {
  const viewer = await createAccount(page.request, 'Viewer');
  await page.goto('/auth/login'); await page.getByLabel('Email address').fill(viewer.email); await page.getByLabel('Password', { exact: true }).fill(viewer.password);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click(); await expect(page).toHaveURL(/\/dashboard$/);
  await page.getByRole('link', { name: 'AI Assistant', exact: true }).click(); await expect(page).toHaveURL(/\/ai-assistant$/);
  await expect(page.getByRole('heading', { name: 'AI Assistant unavailable' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Send question' })).toBeDisabled();
  await expect(page.getByText('AI is disabled. Your other ERP features remain available.')).toBeVisible();
  await page.getByRole('button', { name: 'TR', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Yapay Zekâ Asistanı kullanılamıyor' })).toBeVisible();
  await page.screenshot({ path: '../.tools/phase6/ai-disabled-tr.png', fullPage: true });
});

test('AI input, suggestions, loading, duplicate prevention and actual-source rendering in EN/TR', async ({ page }) => {
  await fixtureSession(page); let calls = 0; let release = () => {};
  const waiting = new Promise<void>(resolve => { release = resolve; });
  await page.route('**/api/ai/chat', async route => {
    calls++; const data = route.request().postDataJSON(); expect(data.language).toBe('en'); expect(data.message).toBe('Summarize sales for the last 30 days.');
    expect(Object.keys(data).sort()).toEqual(['language', 'message']); await waiting;
    await route.fulfill({ json: { answer: 'Completed sales: 125.00 USD. <script>alert(1)</script> [unsafe](https://attacker.example)', language: 'en', generatedAtUtc: '2026-10-09T12:00:00Z', requestId: 'ui-fixture',
      sources: [{ type: 'report', name: 'get_sales_summary', label: 'Sales summary', parameters: { startDate: '2026-09-09T00:00:00Z', endDate: '2026-10-09T00:00:00Z' }, reportPath: '/reports/sales' }] } });
  });
  await page.goto('/ai-assistant'); const send = page.getByRole('button', { name: 'Send question', exact: true });
  await expect(send).toBeDisabled(); await page.getByLabel('Your business question').fill('   '); await expect(send).toBeDisabled();
  await page.getByRole('button', { name: 'Summarize sales for the last 30 days.' }).click();
  await expect(page.getByLabel('Your business question')).toHaveValue('Summarize sales for the last 30 days.');
  await send.click(); await expect(send).toBeDisabled(); await expect(page.getByText('Reading authorized reports…')).toBeVisible();
  expect(calls).toBe(1); release();
  await expect(page.getByText('Completed sales: 125.00 USD.', { exact: false })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Sales summary' })).toHaveAttribute('href', '/reports/sales');
  await expect(page.locator('a[href*="attacker"]')).toHaveCount(0); await expect(page.locator('.ai-answer script')).toHaveCount(0);
  await page.screenshot({ path: '../.tools/phase6/ai-en-ui-fixture.png', fullPage: true });
  await page.getByRole('button', { name: 'TR', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Yapay Zekâ Asistanı', exact: true })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Satış özeti', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'En çok satılan 5 ürünü göster.' }).click(); await expect(page.getByLabel('İşletmeniz hakkındaki sorunuz')).toHaveValue('En çok satılan 5 ürünü göster.');
  await page.getByLabel('İşletmeniz hakkındaki sorunuz').fill('x'.repeat(2100)); await expect(page.getByLabel('İşletmeniz hakkındaki sorunuz')).toHaveValue('x'.repeat(2000));
  await page.setViewportSize({ width: 390, height: 844 }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.screenshot({ path: '../.tools/phase6/ai-tr-mobile-ui-fixture.png', fullPage: true });
  await page.reload(); await expect(page.getByText('Completed sales: 125.00 USD.', { exact: false })).toHaveCount(0);
  expect(await page.evaluate(() => [...Object.keys(localStorage), ...Object.keys(sessionStorage)].some(k => /chat|conversation/i.test(k)))).toBe(false);
});

for (const [status, code, message] of [
  [503, 'aiProviderUnavailable', 'The AI provider is unavailable. Please try again later.'],
  [429, 'aiRateLimit', 'AI request limit reached. Please try again later.'],
  [403, 'aiForbidden', 'You do not have permission to read this report.'],
] as const) test(`AI safely displays ${status} ${code}`, async ({ page }) => {
  await fixtureSession(page); await page.route('**/api/ai/chat', route => route.fulfill({ status, json: { code } }));
  await page.goto('/ai-assistant'); await page.getByLabel('Your business question').fill('Sales?'); await page.getByRole('button', { name: 'Send question' }).click();
  await expect(page.getByRole('alert')).toHaveText(message); await expect(page.getByRole('button', { name: 'Send question' })).toBeDisabled();
  await expect(page).toHaveURL(/\/ai-assistant$/);
});

test('AI Turkish request language, cancellation and forbidden status remain understandable', async ({ page }) => {
  await fixtureSession(page, 'tr'); let language: string | undefined;
  await page.route('**/api/ai/chat', async route => { language = route.request().postDataJSON().language; await new Promise(resolve => setTimeout(resolve, 1500)); await route.fulfill({ json: { answer: 'Rapor hazır.', language: 'tr', generatedAtUtc: new Date().toISOString(), sources: [], requestId: 'fixture' } }).catch(() => {}); });
  await page.goto('/ai-assistant'); await page.getByLabel('İşletmeniz hakkındaki sorunuz').fill('Satış özeti'); await page.getByRole('button', { name: 'Soruyu gönder' }).click();
  await expect(page.getByText('İzinli raporlar okunuyor…')).toBeVisible(); expect(language).toBe('tr');
  await page.getByRole('button', { name: 'İptal', exact: true }).click(); await expect(page.getByRole('alert')).toHaveText('İstek iptal edildi.');
  await page.unroute('**/api/ai/chat'); await page.route('**/api/ai/chat', route => route.fulfill({ json: { answer: 'Güncel stok raporu hazır.', language: 'tr', generatedAtUtc: new Date().toISOString(), sources: [], requestId: 'tr-fixture' } }));
  await page.getByLabel('İşletmeniz hakkındaki sorunuz').fill('Güncel stok özeti'); await page.getByRole('button', { name: 'Soruyu gönder' }).click();
  await expect(page.getByText('Güncel stok raporu hazır.', { exact: true })).toBeVisible();
  await page.unroute('**/api/ai/status'); await page.route('**/api/ai/status', route => route.fulfill({ status: 403, json: {} }));
  await page.reload(); await expect(page.getByRole('alert')).toHaveText('Bu işlemi yapma yetkiniz yok.');
});

test('AI missing configuration and unauthorized response respect existing session handling', async ({ page }) => {
  await fixtureSession(page); await page.unroute('**/api/ai/status');
  await page.route('**/api/ai/status', route => route.fulfill({ json: { enabled: true, configured: false, available: false } }));
  await page.goto('/ai-assistant'); await expect(page.getByText('AI provider configuration is required. Contact your administrator.')).toBeVisible();
  await page.unroute('**/api/ai/status'); await page.route('**/api/ai/status', route => route.fulfill({ status: 401, json: {} }));
  await page.reload(); await expect(page).toHaveURL(/\/auth\/login$/);
});
