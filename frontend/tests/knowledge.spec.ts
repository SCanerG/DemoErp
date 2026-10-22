import { test, expect, type Page } from '@playwright/test';
const id = '11111111-1111-1111-1111-111111111121';
const doc = { id, fileName: 'Manual.md', scope: 'private', version: 1, status: 'indexed', updatedAt: '2026-10-21T00:00:00Z', progress: 100, error: null, chunkCount: 2, canManage: true, embeddingModel: 'nomic-embed-text', dimensions: 768 };
const answer = { answer: 'Verified source excerpts.', language: 'en', responseKind: 'model_extract', insufficientEvidence: false, retrievalMs: 20, llmMs: 30, totalTokens: 100, embeddingModel: 'nomic-embed-text', dimensions: 768,
  citations: [{ number: 1, documentId: id, version: 1, chunkId: '11111111-1111-1111-1111-111111111122', fileName: 'Manual.md', reference: 'Order procedure', quote: 'Choose a customer and save the pending order.' }] };
async function fixture(page: Page, role = 'Admin') {
  await page.goto('/auth/login'); await page.evaluate(role => {
    localStorage.setItem('catalog.language', 'en');
    sessionStorage.setItem('catalog.session', JSON.stringify({ accessToken: 'knowledge-fixture', expiresAt: new Date(Date.now()+3600000).toISOString(), user: { id: 'knowledge-owner', name: 'Knowledge user', email: 'knowledge@example.com', role, isActive: true, createdAt: '2026-10-01T00:00:00Z', updatedAt: null } }));
  }, role);
  await page.route('**/api/knowledge/status', r => r.fulfill({ json: { enabled: true, vectorAvailable: true, embeddingModel: 'nomic-embed-text', maxFileBytes: 5242880 } }));
  await page.route('**/api/knowledge/documents', r => r.fulfill({ json: [role === 'Viewer' ? { ...doc, canManage: false } : doc] }));
  await page.route(`**/api/knowledge/documents/${id}/versions`, r => r.fulfill({ json: [{ number: 1, fileName: doc.fileName, createdAt: doc.updatedAt }] }));
  await page.goto('/knowledge');
}
test('Knowledge Center lists scoped documents, metadata, versions and safe cited source previews', async ({ page }) => {
  await fixture(page);
  await expect(page.getByRole('heading', { name: 'Knowledge Center', exact: true })).toBeVisible();
  await expect(page.getByText('Indexed · 100%', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Manual.md', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Document versions' })).toBeVisible();
  await page.route('**/api/knowledge/chat', r => { expect(r.request().postDataJSON()).toEqual({ message: 'How to create an order?', language: 'en' }); return r.fulfill({ json: answer }); });
  await page.route(`**/api/knowledge/documents/${id}/chunks/*`, r => r.fulfill({ json: { text: '<img src=x onerror=alert(1)> Source stays text.' } }));
  await page.getByLabel('Documentation question').fill('How to create an order?'); await page.getByRole('button', { name: 'Ask knowledge agent', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Model-selected verified source excerpts' })).toBeVisible();
  await expect(page.getByText('[1] Manual.md · Version 1 · Order procedure', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Read source passage' }).click();
  await expect(page.getByText('<img src=x onerror=alert(1)> Source stays text.', { exact: true })).toBeVisible(); await expect(page.locator('img[src=x]')).toHaveCount(0);
});
test('Knowledge document upload sends multipart content and an explicit private scope', async ({ page }) => {
  await fixture(page); let uploads = 0;
  await page.route('**/api/knowledge/documents', r => {
    if (r.request().method() === 'GET') return r.fulfill({ json: [doc] });
    uploads++; expect(r.request().headers()['content-type']).toContain('multipart/form-data; boundary=');
    expect(r.request().postData()).toContain('name="scope"'); expect(r.request().postData()).toContain('private');
    return r.fulfill({ status: 202, json: { ...doc, status: 'queued', progress: 0 } });
  });
  await page.getByLabel('Document file').setInputFiles({ name: 'policy.md', mimeType: 'text/markdown', buffer: Buffer.from('# Policy\nHuman review required.') });
  await page.getByRole('button', { name: 'Upload document', exact: true }).click(); await expect.poll(() => uploads).toBe(1);
});
test('Viewer can ask and cite sources but has no upload or document management actions', async ({ page }) => {
  await fixture(page, 'Viewer');
  await expect(page.getByRole('heading', { name: 'Upload document', exact: true })).toHaveCount(0);
  for (const name of ['Reindex', 'Delete', 'Upload a new document version']) await expect(page.getByRole('button', { name, exact: true })).toHaveCount(0);
  await expect(page.getByLabel('Documentation question')).toBeEnabled();
});
test('Knowledge Turkish copy, insufficient evidence and disabled vector states are meaningful', async ({ page }) => {
  await fixture(page);
  await page.getByRole('button', { name: 'TR', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Bilgi Merkezi', exact: true })).toBeVisible();
  await page.route('**/api/knowledge/chat', r => r.fulfill({ json: { ...answer, answer: 'Erişebildiğiniz belgelerde yeterli bilgi bulunamadı.', responseKind: 'insufficient', insufficientEvidence: true, citations: [] } }));
  await page.getByLabel('Belgelere ilişkin sorunuz').fill('Bilinmeyen politika?'); await page.getByRole('button', { name: 'Bilgi ajanına sor', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Yeterli doğrulanmış kaynak yok' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Kaynak pasajı oku' })).toHaveCount(0);
  await page.route('**/api/knowledge/status', r => r.fulfill({ json: { enabled: true, vectorAvailable: false, embeddingModel: 'nomic-embed-text', maxFileBytes: 5242880 } }));
  await page.reload(); await expect(page.getByLabel('Belgelere ilişkin sorunuz')).toBeDisabled(); await expect(page.getByText(/Bu veritabanında pgvector kurulu değil/)).toBeVisible();
});
test('Knowledge library supports reindex, new version and deliberate deletion', async ({ page }) => {
  await fixture(page); let reindexed = 0, deleted = 0;
  await page.route(`**/api/knowledge/documents/${id}/reindex`, r => { reindexed++; return r.fulfill({ status: 202, json: { ...doc, status: 'queued' } }); });
  await page.route(`**/api/knowledge/documents/${id}`, r => { if (r.request().method() === 'DELETE') deleted++; return r.fulfill({ status: 204 }); });
  await page.getByRole('button', { name: 'Reindex', exact: true }).click(); await expect.poll(() => reindexed).toBe(1);
  await page.getByRole('button', { name: 'Upload a new document version', exact: true }).click(); await expect(page.getByRole('heading', { name: 'Upload a new document version' })).toBeVisible();
  page.on('dialog', dialog => dialog.accept()); await page.getByRole('button', { name: 'Delete', exact: true }).click(); await expect.poll(() => deleted).toBe(1);
});
test('Knowledge screen remains readable at a mobile viewport', async ({ page }) => {
  await fixture(page); await page.setViewportSize({ width: 390, height: 844 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await expect(page.getByRole('heading', { name: 'Knowledge Agent', exact: true })).toBeVisible();
});
