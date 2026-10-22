import { test, expect, type Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
if (process.env.AI_WORKFLOW_UI_LIVE_SMOKE === 'true') test.use({ trace: 'off', screenshot: 'off' });
const id = '11111111-1111-1111-1111-111111111113';
const empty = { executionId: id, intent: 'inventory', answer: 'Verified result explanation.', language: 'en', isMock: false, inventory: null, sales: null, orderDraft: null, sources: [] };
async function fixture(page: Page, role: 'Viewer' | 'Admin' | 'Manager' = 'Viewer') {
  await page.goto('/auth/login'); await page.evaluate(role => {
    localStorage.setItem('catalog.language', 'en');
    sessionStorage.setItem('catalog.session', JSON.stringify({ accessToken: 'agent-fixture', expiresAt: new Date(Date.now()+3600000).toISOString(), user: { id: 'agent-user', name: 'Agent user', email: 'agent@example.com', role, isActive: true, createdAt: '2026-10-01T00:00:00Z', updatedAt: null } }));
  }, role);
  await page.route('**/api/ai/agents/status', r => r.fulfill({ json: { enabled: true, configured: true, provider: 'Ollama', model: 'qwen3:8b', isMock: false, health: 'ready' } }));
  await page.route('**/api/ai/agents/runs', r => r.request().method() === 'POST' ? r.fulfill({ status: 201, json: { id } }) : r.fulfill({ json: [] }));
  await page.route(`**/api/ai/agents/runs/${id}/proposals`, r => r.fulfill({ json: [] }));
  await page.route(`**/api/ai/agents/runs/${id}`, r => r.fulfill({ json: { mode: 'inventory', execution: { id, provider: 'Ollama', model: 'qwen3:8b', outcome: 'success', totalTokens: 45, providerCalls: 1, durationMs: 120, tools: [{ name: 'analyze_replenishment', outcome: 'success', durationMs: 10 }] }, steps: [{ sequence: 0, agent: 'inventory', outcome: 'success', durationMs: 10, createdAt: new Date().toISOString() }] } }));
  await page.goto('/agent-center');
}
test('Agent Center renders real status, typed inventory facts and bounded execution metadata', async ({ page }) => {
  await fixture(page);
  await page.route('**/execute', r => {
    expect(r.request().postDataJSON()).toMatchObject({ message: 'Check stock', language: 'en', period: 'auto' });
    return r.fulfill({ json: { ...empty, inventory: { retrievedAt: new Date().toISOString(), salesPeriod: { start: '2026-07-01T00:00:00Z', end: '2026-10-01T00:00:00Z' }, matchingProducts: 1, truncated: false, products: [{ productId: '1', product: 'Synthetic material', currentStock: 2, minimumStock: 10, completedQuantity: 90, dailyAverage: 1, targetStock: 14, suggestedQuantity: 12 }] } } });
  });
  await expect(page.getByText('Local model ready')).toBeVisible();
  await page.getByLabel('Your business question').fill('Check stock'); await page.getByRole('button', { name: 'Run analysis', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Replenishment recommendations' })).toBeVisible(); await expect(page.getByText('Synthetic material')).toBeVisible();
  await expect(page.getByLabel('Agent activity').getByText('Replenishment analysis', { exact: false })).toBeVisible();
  await page.setViewportSize({ width: 390, height: 844 }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.getByRole('button', { name: 'TR', exact: true }).click(); await expect(page.getByRole('heading', { name: 'Agent Kontrol Merkezi' })).toBeVisible();
  const axe = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze(); expect(axe.violations).toEqual([]);
});
test('Agent Center shows missing order identities and viewers cannot save an order', async ({ page }) => {
  await fixture(page); await page.route('**/execute', r => r.fulfill({ json: { ...empty, intent: 'order', orderDraft: { customer: null, warehouse: 'Main', lines: [], total: null, currency: 'USD', notSubmitted: true, persisted: false, requiresHumanReview: true, clarifications: [{ field: 'customer', query: 'Acme', reason: 'ambiguous', candidates: [{ id: '1', name: 'Acme A' }, { id: '2', name: 'Acme B' }] }] } } }));
  await page.getByLabel('Your business question').fill('Draft order'); await page.getByRole('button', { name: 'Run analysis', exact: true }).click();
  await expect(page.getByText('Not submitted', { exact: true })).toBeVisible(); await expect(page.getByRole('heading', { name: 'Clarification required' })).toBeVisible();
  await expect(page.getByText('Acme A · Acme B')).toBeVisible(); await expect(page.getByRole('button', { name: /submit order|create order/i })).toHaveCount(0);
});

const completeDraft = {
  customerId: '11111111-1111-1111-1111-111111111114', customer: 'Synthetic customer',
  warehouseId: '11111111-1111-1111-1111-111111111115', warehouse: 'Main',
  lines: [{ productId: '11111111-1111-1111-1111-111111111116', product: 'Synthetic product', quantity: 10, unitPrice: 2, lineTotal: 20, availableStock: 0, shortage: 10 }],
  total: 20, currency: 'USD', notSubmitted: true, persisted: false, requiresHumanReview: true, clarifications: [],
};
const completeProposal = { id: 'proposal-1', executionId: id, kind: 'sales', state: 'pending', fingerprint: 'a'.repeat(64), createdAt: new Date().toISOString(), expiresAt: new Date(Date.now()+900000).toISOString(),
  facts: { counterpartyId: completeDraft.customerId, counterparty: completeDraft.customer, warehouseId: completeDraft.warehouseId, warehouse: completeDraft.warehouse, currency: 'USD', lines: completeDraft.lines.map(line => ({ productId: line.productId, product: line.product, quantity: line.quantity, unitPrice: line.unitPrice, stock: line.availableStock, priceSourceId: null })) }, resultId: null, resultNumber: null, resultStatus: null, errorCategory: null };
async function showDraft(page: Page, draft: object = completeDraft) {
  const proposals = draft === completeDraft ? [completeProposal] : [];
  await page.route(`**/api/ai/agents/runs/${id}/proposals`, r => r.fulfill({ json: proposals }));
  await page.route('**/execute', r => r.fulfill({ json: { ...empty, intent: 'order', orderDraft: draft, proposals } }));
  await page.getByLabel('Your business question').fill('Prepare an order draft');
  await page.getByRole('button', { name: 'Run analysis', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Order draft preview', exact: true })).toBeVisible();
}
for (const role of ['Admin', 'Manager'] as const) {
  test(`${role} can explicitly save a reviewed draft once via the bound proposal approval endpoint`, async ({ page }) => {
    await fixture(page, role); let requests = 0;
    await page.route('**/api/ai/agents/proposals/proposal-1/approve', async route => {
      requests++;
      expect(route.request().method()).toBe('POST');
      expect(route.request().postDataJSON()).toEqual({ idempotencyKey: expect.any(String), fingerprint: completeProposal.fingerprint });
      await new Promise(resolve => setTimeout(resolve, 400));
      await route.fulfill({ json: { ...completeProposal, state: 'approved', resultVerified: true, resultId: 'saved-order', resultNumber: 'ORD-SYNTHETIC', resultStatus: 'Pending' } });
    });
    await showDraft(page);
    const save = page.getByRole('button', { name: 'Save order', exact: true });
    await expect(save).toBeDisabled(); expect(requests).toBe(0);
    await page.getByRole('checkbox', { name: 'I reviewed the customer, warehouse and items and approve saving this order.' }).check();
    await save.evaluate((button: HTMLButtonElement) => { button.click(); button.click(); });
    await expect(page.getByRole('button', { name: 'Run analysis', exact: true })).toBeDisabled();
    await expect(page.getByText('Saved as pending', { exact: true })).toBeVisible();
    await expect(page.getByRole('status').filter({ hasText: 'ORD-SYNTHETIC' })).toContainText('$20.00');
    await expect(page.getByRole('link', { name: 'View order', exact: true })).toHaveAttribute('href', '/orders/saved-order');
    await expect(save).toHaveCount(0); expect(requests).toBe(1);
    await expect(page.getByText('Not submitted', { exact: true })).toHaveCount(0);
  });
}
test('Viewer cannot save even a complete order draft', async ({ page }) => {
  await fixture(page); await showDraft(page);
  await expect(page.getByText('Only administrators and operations managers can save an order.')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Save order', exact: true })).toHaveCount(0);
  await expect(page.getByRole('checkbox', { name: /I reviewed/ })).toHaveCount(0);
});
test('Incomplete draft cannot be saved by an administrator', async ({ page }) => {
  await fixture(page, 'Admin');
  await showDraft(page, { ...completeDraft, customerId: null, customer: null, total: null, clarifications: [{ field: 'customer', query: 'B', reason: 'notFound', candidates: [] }] });
  await expect(page.getByRole('button', { name: 'Save order', exact: true })).toHaveCount(0);
  await expect(page.getByRole('checkbox', { name: /I reviewed/ })).toHaveCount(0);
  await expect(page.getByText('Resolve the missing or ambiguous draft fields before saving an order.')).toBeVisible();
});
test('Save errors remain visible without automatically repeating order creation', async ({ page }) => {
  await fixture(page, 'Admin'); let requests = 0;
  await page.route('**/api/ai/agents/proposals/proposal-1/approve', route => { requests++; return route.fulfill({ status: 403, json: { title: 'Forbidden' } }); });
  await showDraft(page);
  await page.getByRole('checkbox', { name: /I reviewed/ }).check();
  await page.getByRole('button', { name: 'Save order', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('You do not have permission to perform this action.');
  await expect(page.getByText('Saved as pending', { exact: true })).toHaveCount(0);
  await expect(page.getByRole('alert')).toContainText('Reload the execution to check its verified result.');
  expect(requests).toBe(1);
});
test('Turkish order save controls explain the human review and pending status', async ({ page }) => {
  await fixture(page, 'Admin'); await showDraft(page);
  await page.getByRole('button', { name: 'TR', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Siparişi kaydet', exact: true })).toBeDisabled();
  await expect(page.getByRole('checkbox', { name: 'Müşteriyi, depoyu ve sipariş kalemlerini inceledim; siparişin kaydedilmesini onaylıyorum.' })).toBeVisible();
  await expect(page.getByText('Onayda teklif içeriği, yetkiler, fiyat ve stok yeniden kontrol edilir. Veri değiştiyse yeni teklif gerekir. Stok rezerve edilmez.')).toBeVisible();
});
test('Purchase workflow proposals show supplier evidence and restore approved purchase results from history', async ({ page }) => {
  await fixture(page, 'Admin');
  const proposal = { ...completeProposal, id: 'purchase-proposal', kind: 'purchase', state: 'approved', resultVerified: true, resultId: 'saved-purchase', resultNumber: 'PO-SYNTHETIC', resultStatus: 'Draft', facts: { ...completeProposal.facts, warehouse: null, warehouseId: null, counterparty: 'Synthetic supplier' } };
  await page.route('**/api/ai/agents/runs', r => r.fulfill({ json: [{ id, mode: 'procurement', outcome: 'success', createdAt: new Date().toISOString() }] }));
  await page.route(`**/api/ai/agents/runs/${id}/proposals`, r => r.fulfill({ json: [proposal] }));
  await page.reload();
  await page.getByRole('button', { name: /Purchase workflow/ }).click();
  await expect(page.getByRole('heading', { name: 'Purchase order proposal' })).toBeVisible();
  await expect(page.getByText('Supplier: Synthetic supplier', { exact: true })).toBeVisible();
  await expect(page.getByText('Purchase draft saved', { exact: true })).toBeVisible();
  await expect(page.getByRole('link', { name: 'View order', exact: true })).toHaveAttribute('href', '/purchase-orders/saved-purchase');
  await expect(page.getByRole('button', { name: 'Save order', exact: true })).toHaveCount(0);
});
test('Expired proposals cannot be approved even by an administrator', async ({ page }) => {
  await fixture(page, 'Admin');
  await showDraft(page);
  await page.route(`**/api/ai/agents/runs/${id}/proposals`, r => r.fulfill({ json: [{ ...completeProposal, expiresAt: new Date(Date.now()-1000).toISOString() }] }));
  await page.reload();
  await page.route('**/api/ai/agents/runs', r => r.fulfill({ json: [{ id, mode: 'order', outcome: 'success', createdAt: new Date().toISOString() }] }));
  await page.reload(); await page.getByRole('button', { name: /Order Draft Agent/ }).click();
  await expect(page.getByText('Proposal expired', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Save order', exact: true })).toHaveCount(0);
});
test('Approval retries retain the operation key after a connection failure', async ({ page }) => {
  await fixture(page, 'Admin'); const keys: string[] = [];
  await page.route('**/api/ai/agents/proposals/proposal-1/approve', async route => {
    keys.push(route.request().postDataJSON().idempotencyKey);
    if (keys.length === 1) return route.abort('failed');
    return route.fulfill({ json: { ...completeProposal, state: 'approved', resultVerified: true, resultId: 'saved-order', resultNumber: 'ORD-RETRY', resultStatus: 'Pending' } });
  });
  await showDraft(page); await page.getByRole('checkbox', { name: /I reviewed/ }).check();
  await page.getByRole('button', { name: 'Save order', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('Unable to reach the server.');
  await page.getByRole('button', { name: 'Save order', exact: true }).click();
  await expect(page.getByText('Saved as pending', { exact: true })).toBeVisible();
  expect(keys).toHaveLength(2); expect(keys[0]).toBe(keys[1]);
});
test('Cancelling a proposal consumes its approval controls without creating an order', async ({ page }) => {
  await fixture(page, 'Admin'); let approvals = 0;
  await page.route('**/api/ai/agents/proposals/proposal-1/approve', r => { approvals++; return r.abort(); });
  await page.route('**/api/ai/agents/proposals/proposal-1/cancel', r => r.fulfill({ json: { ...completeProposal, state: 'cancelled' } }));
  await showDraft(page); await page.getByRole('button', { name: 'Cancel proposal', exact: true }).click();
  await expect(page.getByText('Cancelled', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Save order', exact: true })).toHaveCount(0); expect(approvals).toBe(0);
});

test('Selecting an example draft resets a conflicting sales mode to automatic routing', async ({ page }) => {
  await fixture(page);
  await page.locator('#agent-mode').selectOption('sales');
  await page.getByText('Example requests', { exact: true }).click();
  await page.getByRole('button', { name: 'Prepare an order draft for 10 units of Product A for Customer B.', exact: true }).click();
  await expect(page.locator('#agent-mode')).toHaveValue('auto');
  await expect(page.getByLabel('Your business question')).toHaveValue('Prepare an order draft for 10 units of Product A for Customer B.');
});

test('Agent Center identifies verified pending count as a deterministic report', async ({ page }) => {
  await fixture(page);
  await page.route(`**/api/ai/agents/runs/${id}`, r => r.fulfill({ json: { mode: 'auto', execution: { id, provider: 'Ollama', model: 'qwen3:8b', outcome: 'success', totalTokens: 0, providerCalls: 0, durationMs: 20, tools: [{ name: 'get_pending_orders', outcome: 'success', durationMs: 10 }] }, steps: [{ sequence: 0, agent: 'reporting', outcome: 'success', durationMs: 10, createdAt: new Date().toISOString() }] } }));
  await page.route('**/execute', r => r.fulfill({ json: { ...empty, intent: 'reporting', responseKind: 'deterministic', answer: 'There are currently 3 orders awaiting confirmation.', sources: [{ name: 'get_pending_orders', label: 'Pending orders', reportPath: '/orders' }] } }));
  await page.getByLabel('Your business question').fill('How many orders are pending?');
  await page.getByRole('button', { name: 'Run analysis', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Verified ERP summary', exact: true })).toBeVisible();
  await expect(page.getByText('There are currently 3 orders awaiting confirmation.')).toBeVisible();
  await expect(page.getByRole('link', { name: 'Pending orders', exact: true })).toHaveAttribute('href', '/orders');
  await expect(page.getByRole('heading', { name: 'AI interpretation — verify the facts below', exact: true })).toHaveCount(0);
});
test('Agent Center handles cancellation without duplicate execution', async ({ page }) => {
  await fixture(page); let count = 0;
  await page.route('**/execute', async r => { count++; await new Promise(resolve => setTimeout(resolve, 2000)); await r.fulfill({ json: empty }).catch(() => {}); });
  await page.getByLabel('Your business question').fill('Inventory'); await page.getByRole('button', { name: 'Run analysis', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Run analysis', exact: true })).toBeDisabled(); await expect.poll(() => count).toBe(1);
  await page.getByRole('button', { name: 'Cancel', exact: true }).click(); await expect(page.getByRole('alert')).toContainText('Request cancelled.'); expect(count).toBe(1);
});
test('Agent Center exposes safe error and real versus mock status', async ({ page }) => {
  await fixture(page);
  await page.route('**/api/ai/agents/status', r => r.fulfill({ json: { enabled: true, configured: true, provider: 'Mock', model: 'development', isMock: true, health: 'mock' } }));
  await page.getByRole('button', { name: 'Check connection', exact: true }).click();
  await expect(page.getByText('Development mock — no live language model is connected.')).toBeVisible();
  await page.route('**/execute', r => r.fulfill({ status: 400, json: { code: 'aiAgentLimit' } }));
  await page.getByLabel('Your business question').fill('Inventory'); await page.getByRole('button', { name: 'Run analysis', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('The agent execution limit was reached.');
});
test('Live Agent Center runs coordinated real Ollama analysis and shows actual steps', async ({ page }) => {
  test.skip(process.env.AI_AGENTS_LIVE_SMOKE !== 'true', 'Opt-in real Ollama only.'); test.setTimeout(150000);
  const email = process.env.AI_LIVE_EMAIL, password = process.env.AI_LIVE_PASSWORD; if (!email || !password) throw new Error('ERP test credentials required.');
  await page.goto('/auth/login'); await page.getByLabel('Email address').fill(email); await page.getByLabel('Password', { exact: true }).fill(password);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click(); await expect(page).toHaveURL(/\/dashboard$/);
  await page.getByRole('link', { name: 'Agent Control Center', exact: true }).click(); await expect(page.getByText('Local model ready')).toBeVisible();
  await page.getByLabel('Your business question').fill('Analyze sales from the last three months and inspect inventory for replenishment.');
  const pending = page.waitForResponse(r => /\/api\/ai\/agents\/runs\/[^/]+\/execute$/.test(r.url()) && r.request().method() === 'POST', { timeout: 130000 });
  await page.getByRole('button', { name: 'Run analysis', exact: true }).click();
  const response = await pending; expect(response.status()).toBe(200);
  const result = await response.json(); expect(result.isMock).toBe(false); expect(['model', 'fallback']).toContain(result.responseKind);
  await expect(page.getByRole('heading', { name: result.responseKind === 'fallback' ? 'Verified ERP summary — AI explanation unavailable' : 'AI interpretation — verify the facts below', exact: true })).toBeVisible();
  console.log(JSON.stringify({ liveUi: 'agent-center', responseKind: result.responseKind, executionId: result.executionId }));
  await expect(page.getByRole('heading', { name: 'Verified sales facts' })).toBeVisible({ timeout: 130000 });
  await expect(page.getByRole('heading', { name: 'Replenishment recommendations' })).toBeVisible();
  await expect(page.getByLabel('Agent activity').getByText('Sales Agent', { exact: false })).toBeVisible();
  await expect(page.getByLabel('Agent activity').getByText('Inventory Agent', { exact: false })).toBeVisible();
  await page.screenshot({ path: '../.tools/phase11-3/agent-center-live.png', fullPage: true });
});

test.describe('Live workflow approval', () => {
  test('Live approved purchase is verified through actual Ollama, browser review and durable history', async ({ page }) => {
    test.skip(process.env.AI_WORKFLOW_UI_LIVE_SMOKE !== 'true' || process.env.AI_WORKFLOW_ALLOW_WRITE !== 'true', 'Two explicit opt-ins required for isolated ERP writes.');
    test.setTimeout(180000);
    const origin = new URL(process.env.API_URL ?? 'http://127.0.0.1:5104');
    if (origin.protocol !== 'http:' || !['localhost', '127.0.0.1', '[::1]'].includes(origin.hostname) || origin.username || origin.password || origin.search || origin.hash || !['', '/'].includes(origin.pathname)) throw new Error('Only a loopback API origin is allowed.');
    const email = process.env.AI_LIVE_EMAIL, password = process.env.AI_LIVE_PASSWORD;
    if (!email || !password) throw new Error('Existing isolated ERP test credentials required.');
    let token: string;
    async function read(path: string, data?: object) {
      const r = await fetch(new URL('/api' + path, origin), { method: data ? 'POST' : 'GET', redirect: 'error', headers: { Authorization: 'Bearer ' + token, ...(data ? { 'Content-Type': 'application/json' } : {}) }, ...(data ? { body: JSON.stringify(data) } : {}) });
      if (!r.ok) throw new Error('Live workflow HTTP verification failed.'); return r.json();
    }
    await page.goto('/auth/login'); await page.evaluate(() => localStorage.setItem('catalog.language', 'en')); await page.reload();
    await page.getByLabel('Email address').fill(email); await page.getByLabel('Password', { exact: true }).fill(password);
    const loginResponse = page.waitForResponse(r => r.url().endsWith('/api/auth/login') && r.request().method() === 'POST');
    await page.getByRole('button', { name: 'Sign in', exact: true }).click();
    const login = await (await loginResponse).json(); token = login.accessToken; expect(login.user.role).toBe('Admin');
    await expect(page).toHaveURL(/\/dashboard$/);
    const sales = JSON.stringify(await read('/orders')), stock = JSON.stringify(await read('/inventory'));
    const before = await read('/purchase-orders?pageSize=1');
    await page.goto('/agent-center'); await expect(page.getByText('Local model ready', { exact: true })).toBeVisible();
    await page.getByLabel('Your business question').fill('Son 30 günlük satışları analiz et, mevcut stokları kontrol et ve eksik ürünler için satın alma siparişi hazırla.');
    const executionResponse = page.waitForResponse(r => /\/execute$/.test(r.url()) && r.request().method() === 'POST', { timeout: 140000 });
    await page.getByRole('button', { name: 'Run analysis', exact: true }).click();
    const response = await executionResponse; expect(response.status()).toBe(200); const result = await response.json(); expect(result.intent).toBe('procurement');
    const proposal = result.proposals.find((p: { kind: string }) => p.kind === 'purchase'); if (!proposal) throw new Error('Synthetic low-stock products require active suppliers and submitted purchase history.');
    const card = page.locator('section.panel').filter({ has: page.getByRole('heading', { name: 'Purchase order proposal', exact: true }) }).first();
    await expect(card.getByRole('button', { name: 'Save order', exact: true })).toBeDisabled();
    expect((await read('/purchase-orders?pageSize=1')).totalCount).toBe(before.totalCount);
    await card.getByRole('checkbox', { name: /I reviewed the supplier/ }).check();
    const approvalRequest = page.waitForRequest(r => r.url().endsWith('/approve') && r.method() === 'POST');
    const approvalResponse = page.waitForResponse(r => r.url().endsWith('/approve') && r.request().method() === 'POST');
    await card.getByRole('button', { name: 'Save order', exact: true }).click();
    const savedResponse = await approvalResponse; expect(savedResponse.status()).toBe(200); const saved = await savedResponse.json(); expect(saved.resultVerified).toBe(true); expect(saved.resultStatus).toBe('Draft');
    const repeated = await read(`/ai/agents/proposals/${saved.id}/approve`, (await approvalRequest).postDataJSON()); expect(repeated.resultId).toBe(saved.resultId);
    const persisted = await read(`/purchase-orders/${saved.resultId}`); expect(persisted.supplierId).toBe(proposal.facts.counterpartyId); expect(persisted.status).toBe('Draft');
    expect((await read('/purchase-orders?pageSize=1')).totalCount).toBe(before.totalCount + 1);
    expect(JSON.stringify(await read('/orders'))).toBe(sales); expect(JSON.stringify(await read('/inventory'))).toBe(stock);
    const audit = await read('/audit-logs?entityName=PurchaseOrder&action=Create&pageSize=100'); expect(audit.items.some((a: { entityId: string; userId: string }) => a.entityId === saved.resultId && a.userId === login.user.id)).toBe(true);
    await expect(card.getByText('Purchase draft saved', { exact: true })).toBeVisible();
    await expect(card.getByRole('link', { name: 'View order', exact: true })).toHaveAttribute('href', '/purchase-orders/' + saved.resultId);
    await page.reload(); await page.getByRole('button', { name: /Automatic routing/ }).first().click();
    await expect(page.getByText('Purchase draft saved', { exact: true })).toBeVisible();
    const metadata = await read(`/ai/agents/runs/${result.executionId}`); expect(metadata.execution.provider).toBe('Ollama'); expect(metadata.execution.providerCalls).toBeGreaterThan(0);
    console.log(JSON.stringify({ liveUi: 'workflow-approval', executionId: result.executionId, responseKind: result.responseKind, provider: metadata.execution.provider, model: metadata.execution.model, totalTokens: metadata.execution.totalTokens, verifiedPurchaseId: saved.resultId, approvalAndHistoryVerified: true, duplicatePrevented: true, auditVerified: true, stockAndSalesUnchanged: true }));
  });
});
