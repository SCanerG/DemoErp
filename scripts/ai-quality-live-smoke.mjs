import { pathToFileURL } from 'node:url';
import { runRagLiveSmoke } from './ai-rag-live-smoke.mjs';
export async function runQualityLiveSmoke(env = process.env, write = console.log) {
  if (env.AI_QUALITY_LIVE_SMOKE !== 'true') { write('SKIP: AI_QUALITY_LIVE_SMOKE=true is required. No network request made.'); return { skipped:true }; }
  if (env.AI_QUALITY_ALLOW_SYNTHETIC_FIXTURES !== 'true') throw new Error('Explicit isolated fixture opt-in required.');
  const base = new URL(env.API_URL ?? 'http://127.0.0.1:5118');
  if (base.protocol !== 'http:' || !['127.0.0.1','localhost','[::1]'].includes(base.hostname) || base.username || base.password || base.search || base.hash || !['','/'].includes(base.pathname)) throw new Error('Only a loopback origin is supported.');
  if (!env.AI_LIVE_EMAIL || !env.AI_LIVE_PASSWORD) throw new Error('Existing isolated administrator credentials required.');
  let token; const traces = new Map();
  async function request(path, data, method) {
    const response = await fetch(new URL('/api' + path, base), { method:method ?? (data ? 'POST':'GET'), redirect:'error', signal:AbortSignal.timeout(150000),
      headers:{...(token ? {Authorization:'Bearer '+token}:{}),...(data ? {'Content-Type':'application/json'}:{})},...(data ? {body:JSON.stringify(data)}:{}) });
    if (!response.ok) throw new Error('Local quality verification HTTP '+response.status);
    const value = response.status === 204 ? null : await response.json();
    if (value?.executionId && response.headers.get('x-ai-trace-id')) traces.set(value.executionId,response.headers.get('x-ai-trace-id'));
    return value;
  }
  token = (await request('/auth/login',{email:env.AI_LIVE_EMAIL,password:env.AI_LIVE_PASSWORD})).accessToken;
  const provider = await request('/ai/agents/status'); if (provider.provider !== 'Ollama' || provider.health !== 'ready') throw new Error('Actual local Ollama required.');
  const stamp = Date.now(); const category = await request('/categories',{name:'Synthetic quality category '+stamp,description:'Isolated acceptance only',isActive:true});
  const product = await request('/products',{name:'Quality component '+stamp,description:'Isolated quality evaluation fixture',categoryId:category.id,price:12.5,isActive:true,productType:'RawMaterial'});
  const customer = await request('/customers',{name:'Quality customer '+stamp,email:'',phone:'',address:'',isActive:true});
  await request(`/inventory/${product.id}/minimum-level`,{minimumStockLevel:5},'PUT');
  const before = await request('/orders');
  async function run(mode,message) {
    const created = await request('/ai/agents/runs',{mode}); const started = performance.now();
    const result = await request(`/ai/agents/runs/${created.id}/execute`,{message,language:'en',period:'last30days'});
    if (result.isMock || result.responseKind !== 'model') throw new Error('A real model response was not verified.');
    const latencyMs = Math.round(performance.now()-started);
    const journal = await request(`/ai/agents/runs/${created.id}`); if (journal.execution.provider !== 'Ollama' || journal.execution.providerCalls < 1 || journal.execution.totalTokens < 1 || journal.execution.outcome !== 'success') throw new Error('Actual model audit verification failed.');
    return {result,journal,latencyMs};
  }
  const a = await run('inventory','Analyze inventory for the last 30 days.');
  const observed = a.result.inventory.products.find(p => p.productId === product.id); if (!observed || observed.currentStock !== 0 || observed.minimumStock !== 5) throw new Error('Inventory facts mismatch.');
  const b = await run('collaboration','Analyze sales and inventory for the last 30 days.');
  if (!b.result.sales || !b.result.inventory || !b.journal.steps.some(s=>s.agent==='sales') || !b.journal.steps.some(s=>s.agent==='inventory')) throw new Error('Multi-agent execution mismatch.');
  const c = await run('order',`Prepare an order draft for 2 units of ${product.name} for ${customer.name}.`);
  const draft = c.result.orderDraft;
  if (!draft || !draft.notSubmitted || draft.persisted || !draft.requiresHumanReview || draft.customerId !== customer.id || draft.lines.length !== 1 || draft.lines[0].productId !== product.id || draft.lines[0].quantity !== 2 || draft.lines[0].unitPrice !== 12.5 || !c.result.proposals?.length) throw new Error('Approval-required order draft verification failed.');
  const after = await request('/orders'); if (JSON.stringify(before.map(o=>o.id).sort()) !== JSON.stringify(after.map(o=>o.id).sort())) throw new Error('Unexpected order mutation.');
  const rag = await runRagLiveSmoke({...env,AI_RAG_LIVE_SMOKE:'true'},()=>{});
  const traceId = traces.get(b.result.executionId); if (!traceId) throw new Error('Trace correlation header missing.');
  let spanRows = [];
  for (let attempt=0; attempt<20; attempt++) {
    try { spanRows=await request('/quality/traces/'+traceId); } catch { /* bounded persistence wait */ }
    if (spanRows.some(s=>s.operation==='request') && spanRows.some(s=>s.operation==='provider')) break;
    await new Promise(resolve=>setTimeout(resolve,500));
  }
  if (!spanRows.some(s=>s.operation==='orchestration' && s.executionId===b.result.executionId) || !spanRows.some(s=>s.operation==='tool') || !spanRows.some(s=>s.operation==='provider' && s.tokens>0) || JSON.stringify(spanRows).includes(product.name)) throw new Error('Safe trace verification failed.');
  const output = { liveQualityVerified:true,provider:provider.provider,model:provider.model,scenarios:{A:{executionId:a.result.executionId,totalTokens:a.journal.execution.totalTokens,latencyMs:a.latencyMs},B:{executionId:b.result.executionId,totalTokens:b.journal.execution.totalTokens,latencyMs:b.latencyMs},C:{executionId:c.result.executionId,approvalRequired:true,ordersUnchanged:true},D:{documentId:rag.documentId,citations:rag.citations,dimensions:rag.dimensions},E:'separate deterministic backend enforcement tests',F:'separate simulated provider outage tests'},traceId,traceObservations:spanRows.length };
  write(JSON.stringify(output)); return output;
}
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) runQualityLiveSmoke().catch(() => { console.error('Live AI quality verification failed. Inspect safe local provider/evaluation categories.'); process.exitCode=1; });
