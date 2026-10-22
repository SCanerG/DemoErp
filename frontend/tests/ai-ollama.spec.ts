import { test, expect } from '@playwright/test';

test('Inventory Agent selection sends a bounded agent mode through the existing chat contract', async ({ page }) => {
  await page.goto('/auth/login');
  await page.evaluate(() => {
    localStorage.setItem('catalog.language', 'en');
    sessionStorage.setItem('catalog.session', JSON.stringify({ accessToken: 'ai-ui-fixture', expiresAt: new Date(Date.now()+3600000).toISOString(), user: { id: 'ai-fixture', name: 'AI fixture', email: 'fixture@example.com', role: 'Viewer', isActive: true, createdAt: '2026-10-01T00:00:00Z', updatedAt: null } }));
  });
  await page.route('**/api/ai/status', route => route.fulfill({ json: { enabled: true, configured: true, available: true, provider: 'Ollama' } }));
  await page.route('**/api/ai/chat', route => {
    expect(route.request().postDataJSON()).toEqual({ message: 'Current inventory?', language: 'en', agent: 'inventory' });
    return route.fulfill({ json: { answer: 'Synthetic inventory answer', language: 'en', sources: [], generatedAtUtc: new Date().toISOString(), requestId: 'fixture' } });
  });
  await page.goto('/ai-assistant');
  await expect(page.getByText('Ollama', { exact: true })).toBeVisible();
  await page.getByRole('combobox', { name: 'Agent', exact: true }).selectOption('inventory');
  await page.getByLabel('Your business question').fill('Current inventory?');
  await page.getByRole('button', { name: 'Send question', exact: true }).click();
  await expect(page.getByText('Synthetic inventory answer')).toBeVisible();
});

test('Live Ollama React to .NET to model inventory source and audit', async ({ page }) => {
  test.skip(process.env.OLLAMA_LIVE_SMOKE !== 'true', 'Opt-in real local model only.');
  test.setTimeout(150000);
  const email = process.env.AI_LIVE_EMAIL, password = process.env.AI_LIVE_PASSWORD;
  if (!email || !password) throw new Error('Local ERP test account required.');
  await page.goto('/auth/login');
  await page.evaluate(() => localStorage.setItem('catalog.language', 'en'));
  await page.reload();
  await page.getByLabel('Email address').fill(email);
  await page.getByLabel('Password', { exact: true }).fill(password);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page).toHaveURL(/\/dashboard$/);
  await page.getByRole('link', { name: 'AI Assistant', exact: true }).click();
  await expect(page.getByText('Ollama', { exact: true })).toBeVisible();
  await page.getByRole('combobox', { name: 'Agent', exact: true }).selectOption('inventory');
  await page.getByLabel('Your business question').fill('Which products currently have low inventory?');
  const pending = page.waitForResponse(r => r.url().endsWith('/api/ai/chat') && r.request().method() === 'POST', { timeout: 130000 });
  await page.getByRole('button', { name: 'Send question', exact: true }).click();
  const response = await pending; expect(response.status()).toBe(200);
  const answer = await response.json(); expect(answer.isMock).toBe(false);
  expect(['model', 'fallback']).toContain(answer.responseKind);
  expect(answer.sources.some((s: { name: string }) => s.name === 'get_low_stock_products')).toBe(true);
  await expect(page.getByRole('link', { name: 'Low stock products', exact: true })).toBeVisible();
  const token = await page.evaluate(() => JSON.parse(sessionStorage.getItem('catalog.session') ?? '{}').accessToken as string);
  const auditResponse = await page.request.get(`${process.env.API_URL}/api/ai/executions/${answer.executionId}`, { headers: { Authorization: `Bearer ${token}` } });
  expect(auditResponse.status()).toBe(200);
  const audit = await auditResponse.json();
  expect(audit.provider).toBe('Ollama'); expect(audit.outcome).toBe('success');
  expect(audit.totalTokens).toBeGreaterThan(0); expect(audit.correlationId).toBe(answer.requestId);
  expect(audit.tools.some((tool: { name: string; outcome: string }) => tool.name === 'get_low_stock_products' && tool.outcome === 'success')).toBe(true);
  await expect(page.getByText(answer.responseKind === 'fallback' ? 'Verified ERP summary — AI explanation unavailable' : 'AI interpretation of ERP reports', { exact: true })).toBeVisible();
  console.log(JSON.stringify({ liveUi: 'assistant', responseKind: answer.responseKind, executionId: answer.executionId, totalTokens: audit.totalTokens }));
  await page.screenshot({ path: '../.tools/phase11-2/ollama-live-ui.png', fullPage: true });
});
