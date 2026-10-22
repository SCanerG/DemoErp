import { test, expect } from '@playwright/test';
test.use({ trace: 'off', screenshot: 'off' });
test('Live Knowledge Center uploads, indexes with local embeddings and cites actual document text', async ({ page }) => {
  test.skip(process.env.AI_RAG_UI_LIVE_SMOKE !== 'true', 'Explicit isolated live RAG opt-in required.'); test.setTimeout(180000);
  const email = process.env.AI_LIVE_EMAIL, password = process.env.AI_LIVE_PASSWORD;
  if (!email || !password) throw new Error('Existing isolated ERP credentials required.');
  await page.goto('/auth/login'); await page.evaluate(() => localStorage.setItem('catalog.language', 'en')); await page.reload();
  await page.getByLabel('Email address').fill(email); await page.getByLabel('Password', { exact: true }).fill(password); await page.getByRole('button', { name: 'Sign in', exact: true }).click(); await expect(page).toHaveURL(/\/dashboard$/);
  await page.getByRole('link', { name: 'Knowledge Center', exact: true }).click();
  const text = 'To create an order, choose an active customer, add products and quantities, select a warehouse and save. The saved order remains Pending until confirmed.';
  const filename = 'synthetic-browser-manual-' + Date.now() + '.md';
  await page.getByLabel('Document file').setInputFiles({ name: filename, mimeType: 'text/markdown', buffer: Buffer.from('# Order creation\n' + text) });
  const uploadResponse = page.waitForResponse(r => r.url().endsWith('/api/knowledge/documents') && r.request().method() === 'POST');
  await page.getByRole('button', { name: 'Upload document', exact: true }).click(); const upload = await uploadResponse; expect(upload.status()).toBe(202); const document = await upload.json();
  const row = page.getByRole('listitem').filter({ has: page.getByRole('button', { name: filename, exact: true }) });
  await expect(row.getByText('Indexed · 100%', { exact: true })).toBeVisible({ timeout: 100000 });
  await page.getByLabel('Knowledge sources', { exact: true }).selectOption(document.id);
  await page.getByLabel('Documentation question').fill('How do I create an order?');
  const pending = page.waitForResponse(r => r.url().endsWith('/api/knowledge/chat') && r.request().method() === 'POST', { timeout: 120000 });
  await page.getByRole('button', { name: 'Ask knowledge agent', exact: true }).click(); const response = await pending; expect(response.status()).toBe(200); const result = await response.json();
  expect(result.responseKind).toBe('model_extract'); expect(result.dimensions).toBe(768); expect(result.citations.length).toBeGreaterThan(0);
  for (const citation of result.citations) { expect(citation.documentId).toBe(document.id); expect(text).toContain(citation.quote); }
  await expect(page.getByRole('heading', { name: 'Model-selected verified source excerpts' })).toBeVisible();
  await page.getByRole('button', { name: 'Read source passage', exact: true }).first().click(); await expect(page.getByRole('heading', { name: 'Source passage' })).toBeVisible();
  await page.setViewportSize({ width: 390, height: 844 }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  console.log(JSON.stringify({ liveKnowledgeUi: true, documentId: document.id, embeddingModel: result.embeddingModel, dimensions: result.dimensions, responseKind: result.responseKind, citations: result.citations.length, totalTokens: result.totalTokens, retrievalMs: result.retrievalMs, llmMs: result.llmMs }));
});
