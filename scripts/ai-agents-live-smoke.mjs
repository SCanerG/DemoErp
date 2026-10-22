import { pathToFileURL } from 'node:url';
export async function runAgentLiveSmoke(env = process.env, write = console.log) {
  if (env.AI_AGENTS_LIVE_SMOKE !== 'true') { write('SKIP: set AI_AGENTS_LIVE_SMOKE=true for real Ollama agent acceptance. No network request was made.'); return { skipped: true }; }
  const base = new URL(env.API_URL ?? 'http://127.0.0.1:5102');
  if (base.protocol !== 'http:' || !['127.0.0.1', 'localhost', '[::1]'].includes(base.hostname) || base.username || base.password || base.search || base.hash || !['', '/'].includes(base.pathname)) throw new Error('Only a loopback API origin is accepted.');
  if (!env.AI_LIVE_EMAIL || !env.AI_LIVE_PASSWORD) throw new Error('Existing local ERP test credentials are required.');
  async function request(path, token, data) {
    const r = await fetch(new URL('/api' + path, base), { method: data ? 'POST' : 'GET', redirect: 'error', signal: AbortSignal.timeout(140000),
      headers: { ...(token ? { Authorization: 'Bearer ' + token } : {}), ...(data ? { 'Content-Type': 'application/json' } : {}) }, ...(data ? { body: JSON.stringify(data) } : {}) });
    const value = await r.json().catch(() => null);
    if (!r.ok) throw new Error(`Agent check failed at ${path}: HTTP ${r.status}.`);
    return value;
  }
  const login = await request('/auth/login', null, { email: env.AI_LIVE_EMAIL, password: env.AI_LIVE_PASSWORD });
  const token = login?.accessToken; if (!token) throw new Error('ERP login failed.');
  const status = await request('/ai/agents/status', token);
  if (status.provider !== 'Ollama' || status.isMock || status.health !== 'ready') throw new Error('A ready real Ollama provider is required.');
  const beforeOrders = await request('/orders', token), beforeStock = await request('/inventory', token);
  const customers = await request('/customers', token), products = await request('/products', token);
  const customer = customers.find(c => c.isActive && (!env.AI_AGENT_CUSTOMER || c.name === env.AI_AGENT_CUSTOMER) && customers.filter(x => x.isActive && x.name === c.name).length === 1);
  const product = products.find(p => p.isActive && (!env.AI_AGENT_PRODUCT || p.name === env.AI_AGENT_PRODUCT) && products.filter(x => x.isActive && x.name === p.name).length === 1);
  if (!customer || !product) throw new Error('Unambiguous active customer and product data are required; this script never seeds data.');
  const scenarios = [
    { mode: 'inventory', message: 'Analyze current inventory and recommend replenishment.', intent: 'inventory' },
    { mode: 'sales', message: 'Analyze the last three months of sales and identify best-selling products.', intent: 'sales' },
    { mode: 'order', message: `Prepare an order draft for 2 units of product ${JSON.stringify(product.name)} for customer ${JSON.stringify(customer.name)}.`, intent: 'order' },
    { mode: 'auto', message: 'Analyze sales from the last three months, inspect current inventory, and prepare a replenishment recommendation.', intent: 'collaboration' },
  ];
  const outcomes = [];
  for (const scenario of scenarios) {
    const run = await request('/ai/agents/runs', token, { mode: scenario.mode });
    const answer = await request(`/ai/agents/runs/${run.id}/execute`, token, { message: scenario.message, language: 'tr', period: 'last3months' });
    const audit = await request(`/ai/agents/runs/${run.id}`, token);
    if (answer.intent !== scenario.intent || answer.isMock || answer.language !== 'tr' || !answer.answer?.trim() || audit.execution.outcome !== 'success'
      || audit.execution.id !== run.id || answer.executionId !== run.id || audit.execution.model !== status.model || audit.execution.providerCalls < 1 || audit.execution.providerCalls > 3 || audit.execution.provider !== 'Ollama' || audit.execution.totalTokens < 1 || audit.steps.length > 5 || audit.execution.tools.length > 6
      || audit.steps.some(s => s.outcome !== 'success') || audit.execution.tools.some(t => t.outcome !== 'success')) throw new Error('Real agent result/audit verification failed.');
    if (['inventory', 'collaboration'].includes(scenario.intent) && (!answer.inventory || !answer.sources.some(s => s.name === 'analyze_replenishment'))) throw new Error('Inventory evidence missing.');
    if (['sales', 'collaboration'].includes(scenario.intent) && (!answer.sales || !answer.sources.some(s => s.name === 'get_sales_summary'))) throw new Error('Sales evidence missing.');
    if (scenario.intent === 'order') {
      const draft = answer.orderDraft;
      if (!draft?.notSubmitted || draft.persisted || !draft.requiresHumanReview || draft.clarifications.length || draft.customerId !== customer.id || draft.lines.length !== 1
        || draft.lines[0].productId !== product.id || draft.lines[0].quantity !== 2 || draft.lines[0].unitPrice !== product.price || Math.abs(draft.total - 2 * product.price) > 0.000001) throw new Error('Draft references, pricing or review-only enforcement failed.');
    }
    outcomes.push({ intent: answer.intent, responseKind: answer.responseKind, executionId: run.id, provider: audit.execution.provider, model: audit.execution.model, totalTokens: audit.execution.totalTokens,
      durationMs: audit.execution.durationMs, agents: audit.steps.map(s => s.agent), auditedTools: audit.execution.tools.map(t => t.name) });
  }
  const afterOrders = await request('/orders', token), afterStock = await request('/inventory', token);
  const stockSignature = rows => JSON.stringify(rows.map(r => [r.productId, r.quantityOnHand, r.minimumStockLevel]).sort((a,b) => a[0].localeCompare(b[0])));
  if (JSON.stringify(beforeOrders) !== JSON.stringify(afterOrders) || stockSignature(beforeStock) !== stockSignature(afterStock)) throw new Error('Business state changed during acceptance. Use an isolated database without concurrent writers.');
  const result = { liveOllamaAgentsVerified: true, readOnlyBusinessStateVerified: true, model: status.model, scenarios: outcomes };
  write(JSON.stringify(result)); return result;
}
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  runAgentLiveSmoke().catch(() => { console.error('Live agent acceptance failed. Inspect safe local error categories; no credentials, provider bodies or ERP rows are printed.'); process.exitCode = 1; });
}
