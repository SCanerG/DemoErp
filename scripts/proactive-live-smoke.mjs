import { pathToFileURL } from 'node:url';

// Creates only labelled synthetic product/threshold fixtures and monitoring metadata on an explicitly opted-in local review DB.
export async function runProactiveLiveSmoke(env = process.env, write = console.log) {
  if (env.PROACTIVE_API_LIVE_SMOKE !== 'true') { write('SKIP: PROACTIVE_API_LIVE_SMOKE=true is required. No network request made.'); return { skipped: true }; }
  if (env.PROACTIVE_ALLOW_SYNTHETIC_FIXTURES !== 'true') throw new Error('Explicit isolated synthetic fixture opt-in required.');
  const base = new URL(env.API_URL ?? 'http://127.0.0.1:5116');
  if (base.protocol !== 'http:' || !['127.0.0.1','localhost','[::1]'].includes(base.hostname) || base.username || base.password || base.search || base.hash || !['','/'].includes(base.pathname)) throw new Error('Only a loopback origin is supported.');
  if (!env.AI_LIVE_EMAIL || !env.AI_LIVE_PASSWORD) throw new Error('Existing isolated ERP administrator credentials are required.');
  let token;
  async function request(path, data, method) {
    const response = await fetch(new URL('/api' + path, base), { method: method ?? (data ? 'POST' : 'GET'), redirect: 'error', signal: AbortSignal.timeout(150000),
      headers: { ...(token ? { Authorization: 'Bearer ' + token } : {}), ...(data ? { 'Content-Type':'application/json' } : {}) }, ...(data ? { body: JSON.stringify(data) } : {}) });
    if (!response.ok) throw new Error(`Verification HTTP ${response.status}.`);
    return response.status === 204 ? null : response.json();
  }
  token = (await request('/auth/login', { email: env.AI_LIVE_EMAIL, password: env.AI_LIVE_PASSWORD })).accessToken;
  const status = await request('/operations/overview'), provider = await request('/ai/agents/status');
  if (!status.enabled || provider.provider !== 'Ollama' || provider.health !== 'ready') throw new Error('Enabled monitoring and actual local Ollama are required.');
  const category = await request('/categories', { name: 'Synthetic monitoring acceptance category', description: 'Isolated test only', isActive: true });
  const product = await request('/products', { name: 'Synthetic monitored component ' + Date.now(), description: 'Isolated monitoring acceptance only', price: 10, isActive: true, categoryId: category.id, productType: 'RawMaterial' });
  await request(`/inventory/${product.id}/minimum-level`, { minimumStockLevel: 5 }, 'PUT');
  const rule = (await request('/operations/rules')).find(r => r.kind === 'inventory');
  async function monitor(key) {
    const run = await request(`/operations/rules/${rule.id}/run`, { idempotencyKey: key });
    for (let attempt = 0; attempt < 120; attempt++) {
      const row = (await request('/operations/runs')).find(r => r.id === run.id);
      if (row?.state === 'success') return run.id;
      if (row && ['failed','cancelled'].includes(row.state)) throw new Error('Monitoring execution failed.');
      await new Promise(resolve => setTimeout(resolve, 500));
    }
    throw new Error('Monitoring wait exceeded.');
  }
  const key = crypto.randomUUID(), monitoringRun = await monitor(key);
  if ((await request(`/operations/rules/${rule.id}/run`, { idempotencyKey: key })).id !== monitoringRun) throw new Error('Monitoring operation key was not reused.');
  await monitor(crypto.randomUUID());
  const matches = (await request('/operations/alerts?state=all')).items.filter(a => a.entityId === product.id);
  if (matches.length !== 1 || matches[0].category !== 'outOfStock' || JSON.parse(matches[0].facts).stock !== 0 || JSON.parse(matches[0].facts).minimum !== 5) throw new Error('Persisted inventory finding verification failed.');
  const alert = matches[0]; const review = await request(`/operations/alerts/${alert.id}/review`, { purpose:'explain', language:'en' });
  if (review.error || review.result?.responseKind !== 'model' || review.result.isMock) throw new Error('Actual model explanation was not verified.');
  const journal = await request(`/ai/agents/runs/${review.executionId}`);
  if (journal.execution.outcome !== 'success' || journal.execution.provider !== 'Ollama' || journal.execution.providerCalls < 1 || journal.execution.totalTokens < 1) throw new Error('Model execution audit was not verified.');
  const repeated = await request(`/operations/alerts/${alert.id}/review`, { purpose:'explain', language:'en' });
  if (repeated.started || repeated.executionId !== review.executionId) throw new Error('Review deduplication failed.');
  await request(`/operations/alerts/${alert.id}/state`, { action:'acknowledged' });
  if (!(await request(`/operations/alerts/${alert.id}/history`)).some(h => h.action === 'acknowledged')) throw new Error('Alert acknowledgement was not persisted.');
  const result = { liveProactiveVerified:true, provider:provider.provider, model:provider.model, monitoringRun, alertId:alert.id, executionId:review.executionId,
    totalTokens:journal.execution.totalTokens, responseKind:review.result.responseKind, duplicateAlertsPrevented:true, reviewReused:true, acknowledgementPersisted:true };
  write(JSON.stringify(result)); return result;
}
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) runProactiveLiveSmoke().catch(() => { console.error('Live proactive verification failed. Inspect safe local monitoring and provider categories.'); process.exitCode = 1; });
