import { pathToFileURL } from 'node:url';
export async function runWorkflowLiveSmoke(env = process.env, write = console.log) {
  if (env.AI_WORKFLOW_LIVE_SMOKE !== 'true') { write('SKIP: set AI_WORKFLOW_LIVE_SMOKE=true. No network request was made.'); return { skipped: true }; }
  if (env.AI_WORKFLOW_ALLOW_WRITE !== 'true') throw new Error('Explicit AI_WORKFLOW_ALLOW_WRITE=true is required for this isolated-database write test.');
  const base = new URL(env.API_URL ?? 'http://127.0.0.1:5104');
  if (base.protocol !== 'http:' || !['127.0.0.1', 'localhost', '[::1]'].includes(base.hostname) || base.username || base.password || base.search || base.hash || !['', '/'].includes(base.pathname)) throw new Error('Only a loopback API origin is accepted.');
  if (!env.AI_LIVE_EMAIL || !env.AI_LIVE_PASSWORD) throw new Error('Existing isolated ERP test credentials are required.');
  async function request(path, token, data, expected = 200) {
    const response = await fetch(new URL('/api' + path, base), { method: data ? 'POST' : 'GET', redirect: 'error', signal: AbortSignal.timeout(150000),
      headers: { ...(token ? { Authorization: 'Bearer ' + token } : {}), ...(data ? { 'Content-Type': 'application/json' } : {}) }, ...(data ? { body: JSON.stringify(data) } : {}) });
    if (response.status !== expected) throw new Error('Workflow HTTP verification failed.');
    return response.json();
  }
  const session = await request('/auth/login', null, { email: env.AI_LIVE_EMAIL, password: env.AI_LIVE_PASSWORD });
  const token = session.accessToken;
  if (!token || session.user.role !== 'Admin') throw new Error('An isolated test administrator is required to verify the audit trail.');
  const provider = await request('/ai/agents/status', token);
  if (provider.provider !== 'Ollama' || provider.isMock || provider.health !== 'ready') throw new Error('A ready real Ollama model is required.');
  const stock = rows => JSON.stringify(rows.map(row => [row.productId, row.quantityOnHand]).sort((a, b) => a[0].localeCompare(b[0])));
  const beforeSales = await request('/orders', token), beforeStock = stock(await request('/inventory', token));
  const run = await request('/ai/agents/runs', token, { mode: 'auto' }, 201);
  const answer = await request(`/ai/agents/runs/${run.id}/execute`, token, { message: 'Son 30 günlük satışları analiz et, mevcut stokları kontrol et ve eksik ürünler için satın alma siparişi hazırla.', language: 'tr', period: 'auto' });
  const history = await request(`/ai/agents/runs/${run.id}`, token);
  if (answer.intent !== 'procurement' || answer.isMock || !answer.sales || !answer.inventory || answer.orderDraft
    || history.execution.outcome !== 'success' || history.execution.provider !== 'Ollama' || history.execution.providerCalls < 1 || history.execution.totalTokens < 1
    || history.steps.map(step => step.agent).join(',') !== 'orchestrator,sales,inventory,order,summary'
    || history.steps.some(step => step.outcome !== 'success')) throw new Error('Real orchestration evidence failed.');
  const proposal = answer.proposals?.find(item => item.kind === 'purchase' && item.state === 'pending');
  if (!proposal) throw new Error('Existing active supplier and submitted purchase history are required for at least one suggested replenishment. No data is seeded by this script.');
  const beforePurchases = await request('/purchase-orders?pageSize=1', token);
  const approval = { fingerprint: proposal.fingerprint, idempotencyKey: crypto.randomUUID() };
  const saved = await request(`/ai/agents/proposals/${proposal.id}/approve`, token, approval);
  const repeated = await request(`/ai/agents/proposals/${proposal.id}/approve`, token, approval);
  if (saved.state !== 'approved' || !saved.resultVerified || !saved.resultId || saved.resultId !== repeated.resultId || saved.resultStatus !== 'Draft') throw new Error('Approval result failed.');
  const order = await request(`/purchase-orders/${saved.resultId}`, token);
  if (order.supplierId !== proposal.facts.counterpartyId || order.items.length !== proposal.facts.lines.length || order.status !== 'Draft'
    || proposal.facts.lines.some(line => !order.items.some(item => item.productId === line.productId && item.quantityOrdered === line.quantity && item.unitCost === line.unitPrice))) throw new Error('Persisted purchase does not match the exact approved facts.');
  const afterPurchases = await request('/purchase-orders?pageSize=1', token);
  const audit = await request('/audit-logs?entityName=PurchaseOrder&action=Create&pageSize=100', token);
  if (afterPurchases.totalCount !== beforePurchases.totalCount + 1 || JSON.stringify(beforeSales) !== JSON.stringify(await request('/orders', token))
    || beforeStock !== stock(await request('/inventory', token)) || !audit.items.some(item => item.entityId === order.id && item.userId === session.user.id)) throw new Error('Business state, duplicate prevention or audit verification failed.');
  const restored = await request(`/ai/agents/runs/${run.id}/proposals`, token);
  if (!restored.some(item => item.id === proposal.id && item.resultId === order.id && item.resultVerified)) throw new Error('Durable result recovery failed.');
  const result = { liveWorkflowVerified: true, executionId: run.id, provider: provider.provider, model: provider.model, responseKind: answer.responseKind,
    totalTokens: history.execution.totalTokens, durationMs: history.execution.durationMs, verifiedPurchaseId: order.id,
    exactProposalVerified: true, duplicatePrevented: true, auditVerified: true, stockAndSalesUnchanged: true };
  write(JSON.stringify(result)); return result;
}
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  runWorkflowLiveSmoke().catch(() => { console.error('Live workflow verification failed. Inspect safe local backend errors. No credentials, raw provider output or ERP rows are printed.'); process.exitCode = 1; });
}
