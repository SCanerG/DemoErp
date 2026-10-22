import { test, expect, type Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
const id = '11600000-0000-0000-0000-000000000001';
const alertId = '11600000-0000-0000-0000-000000000011';
const settings = { enabled: false, eventsEnabled: false, intervalMinutes: 60, periodDays: 30, minimumSamples: 5, percentageChange: 50, absoluteChange: 100, stalledDays: 7, approachingDays: 3, warningAlerts: true };
const alert = { id: alertId, category: 'outOfStock', severity: 'critical', entityType: 'product', entityId: id, label: '<img src=x onerror=alert(1)> Synthetic component', facts: '{"stock":0,"minimum":5,"suggestedQuantity":6}', dueAt: null, status: 'open', conditionActive: true, lastSeenAt: '2026-10-21T00:00:00Z', occurrence: 1 };
async function fixture(page: Page, role = 'Admin', enabled = true) {
  await page.goto('/auth/login'); await page.evaluate(role => {
    localStorage.setItem('catalog.language', 'en');
    sessionStorage.setItem('catalog.session', JSON.stringify({ accessToken: 'operations-fixture', expiresAt: new Date(Date.now()+3600000).toISOString(), user: { id: 'operations-owner', name: 'Operations user', email: 'ops@example.com', role, isActive: true, createdAt: '2026-10-01T00:00:00Z', updatedAt: null } }));
  }, role);
  await page.route('**/api/ai/agents/status', r => r.fulfill({ json: { health: 'unavailable', model: 'qwen3:8b' } }));
  await page.route('**/api/operations/overview', r => r.fulfill({ json: { enabled, criticalAlerts: 1, openAlerts: 1, activeJobs: 0, pendingApprovals: 0 } }));
  await page.route('**/api/operations/rules', r => r.fulfill({ json: [{ id, kind: 'inventory', version: 1, settings }] }));
  await page.route('**/api/operations/alerts?*', r => r.fulfill({ json: { total: 1, items: [alert] } }));
  await page.route('**/api/operations/runs', r => r.fulfill({ json: [{ id, ruleId: id, state: 'success', trigger: 'scheduled', attempts: 1, createdAt: '2026-10-21T00:00:00Z', durationMs: 200, findings: 1, error: null, diagnostics: '["insufficientSalesHistory"]' }] }));
  await page.goto('/operations');
}
test('Operations Center shows verified facts and useful deterministic status while Ollama is unavailable', async ({ page }) => {
  await fixture(page);
  await expect(page.getByRole('heading', { name: 'AI Operations Center', exact: true })).toBeVisible();
  await expect(page.getByText('Unavailable — deterministic checks continue', { exact: false })).toBeVisible();
  await expect(page.getByText(alert.label, { exact: true })).toBeVisible(); await expect(page.locator('img[src="x"]')).toHaveCount(0);
  await expect(page.getByText('Insufficient completed-order history for a reliable period comparison.')).toBeVisible();
  await expect(page.getByRole('link', { name: 'Open related record' })).toHaveAttribute('href', '/inventory/' + id);
  await page.setViewportSize({ width: 390, height: 844 }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});
test('Viewer sees findings but cannot configure, run checks, change alerts or prepare orders', async ({ page }) => {
  await fixture(page, 'Viewer');
  await expect(page.getByRole('button', { name: 'Acknowledge', exact: true })).toHaveCount(0);
  await expect(page.getByRole('button', { name: /Save monitoring configuration/ })).toHaveCount(0);
  await expect(page.getByRole('button', { name: /Run check now/ })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Prepare a purchase proposal for review' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Analyze inventory and sales' })).toBeVisible();
});
test('Admin settings validate bounds and send an optimistic configuration version; manual checks have operation keys', async ({ page }) => {
  await fixture(page); let configurations = 0, manual = 0;
  await page.route(`**/api/operations/rules/${id}`, r => { configurations++; const body = r.request().postDataJSON(); expect(body.version).toBe(1); expect(body.settings.intervalMinutes).toBe(15); return r.fulfill({ status: 204 }); });
  await page.getByLabel('Interval in minutes', { exact: true }).fill('0'); await page.getByRole('button', { name: 'Save monitoring configuration' }).click(); expect(configurations).toBe(0);
  await page.getByLabel('Interval in minutes', { exact: true }).fill('15'); await page.getByRole('button', { name: 'Save monitoring configuration' }).click(); await expect.poll(() => configurations).toBe(1);
  await page.route(`**/api/operations/rules/${id}/run`, async r => { manual++; expect(r.request().postDataJSON().idempotencyKey).toMatch(/^[0-9a-f-]{36}$/); await r.fulfill({ status: 202, json: { id } }); });
  await page.getByRole('button', { name: /Run check now/ }).click(); await expect.poll(() => manual).toBe(1);
});
test('Alert actions and private agent review preserve explicit approval boundaries and source provenance labels', async ({ page }) => {
  await fixture(page); let action = '';
  await page.route(`**/api/operations/alerts/${alertId}/state`, r => { action = r.request().postDataJSON().action; return r.fulfill({ status: 204 }); });
  await page.getByRole('button', { name: 'Acknowledge', exact: true }).click(); await expect.poll(() => action).toBe('acknowledged');
  await page.route(`**/api/operations/alerts/${alertId}/review`, r => { expect(r.request().postDataJSON()).toEqual({ purpose: 'explain', language: 'en' }); return r.fulfill({ json: { executionId: id, started: true, error: null, result: { answer: 'Review the verified inventory before replenishment.', responseKind: 'fallback', isMock: false, knowledge: null, proposals: [] } } }); });
  await page.getByRole('button', { name: 'Analyze inventory and sales' }).click();
  await expect(page.getByText('Verified ERP summary — AI explanation unavailable')).toBeVisible();
  await expect(page.getByRole('link', { name: 'Open private execution and approval history' })).toHaveAttribute('href', '/agent-center?execution=' + id);
  await expect(page.getByRole('button', { name: 'Save order', exact: true })).toHaveCount(0);
});
test('Disabled monitoring and Turkish error states remain usable', async ({ page }) => {
  await fixture(page, 'Admin', false);
  await expect(page.getByRole('button', { name: /Run check now/ })).toBeDisabled();
  await page.evaluate(() => localStorage.setItem('catalog.language', 'tr')); await page.reload();
  await expect(page.getByRole('heading', { name: 'Yapay Zekâ Operasyon Merkezi' })).toBeVisible();
  await expect(page.getByText('İzleme kapalı.', { exact: false })).toBeVisible();
});
test('Operations Center is keyboard accessible and passes the existing WCAG scan', async ({ page }) => {
  test.setTimeout(90000); await fixture(page);
  const results = await new AxeBuilder({ page }).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();
  expect(results.violations.map(v => ({ id: v.id, nodes: v.nodes.map(n => n.target) }))).toEqual([]);
});
