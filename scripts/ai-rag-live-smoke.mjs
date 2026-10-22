import { pathToFileURL } from 'node:url';
export async function runRagLiveSmoke(env = process.env, write = console.log) {
  if (env.AI_RAG_LIVE_SMOKE !== 'true') { write('SKIP: AI_RAG_LIVE_SMOKE=true is required. No network request made.'); return { skipped: true }; }
  const base = new URL(env.API_URL ?? 'http://127.0.0.1:5106');
  if (base.protocol !== 'http:' || !['127.0.0.1', 'localhost', '[::1]'].includes(base.hostname) || base.username || base.password || base.search || base.hash || !['', '/'].includes(base.pathname)) throw new Error('Only a loopback origin is supported.');
  if (!env.AI_LIVE_EMAIL || !env.AI_LIVE_PASSWORD) throw new Error('Existing isolated ERP administrator credentials are required.');
  let token;
  async function request(path, data, method) {
    const form = data instanceof FormData;
    const response = await fetch(new URL('/api' + path, base), { method: method ?? (data ? 'POST' : 'GET'), redirect: 'error', signal: AbortSignal.timeout(150000),
      headers: { ...(token ? { Authorization: 'Bearer ' + token } : {}), ...(data && !form ? { 'Content-Type': 'application/json' } : {}) }, ...(data ? { body: form ? data : JSON.stringify(data) } : {}) });
    if (!response.ok) throw new Error(`RAG verification HTTP ${response.status} at ${path.split('/').slice(0,3).join('/')}.`);
    return response.status === 204 ? null : response.json();
  }
  const session = await request('/auth/login', { email: env.AI_LIVE_EMAIL, password: env.AI_LIVE_PASSWORD }); token = session.accessToken;
  const status = await request('/knowledge/status'), provider = await request('/ai/agents/status');
  if (!status.enabled || !status.vectorAvailable || provider.provider !== 'Ollama' || provider.health !== 'ready') throw new Error('Real Ollama, enabled knowledge and pgvector are required.');
  const manual = '# Creating customer orders\nTo create an order in DemoERP, select an active customer, add products and quantities, select a warehouse, and save the order. A new order stays Pending until a separate authorized confirmation.\n\n# Replenishment policy\nThe documented replenishment policy requires a manager to review stock shortages before purchasing. Stock below the minimum threshold should be reviewed weekly. Policy guidance does not authorize automatic order creation.';
  const form = new FormData(); form.append('file', new Blob([manual], { type: 'text/markdown' }), 'synthetic-rag-acceptance.md'); form.append('scope', 'private');
  const document = await request('/knowledge/documents', form);
  let metadata;
  for (let attempt = 0; attempt < 120; attempt++) {
    metadata = await request('/knowledge/documents/' + document.id);
    if (metadata.status === 'indexed') break;
    if (['failed','ocrRequired','cancelled'].includes(metadata.status)) throw new Error('Indexing failed: ' + metadata.error);
    await new Promise(resolve => setTimeout(resolve, 1000));
  }
  if (metadata.status !== 'indexed') throw new Error('Indexing did not complete within the bounded wait.');
  const answer = await request('/knowledge/chat', { message: 'How do I create an order in DemoERP?', language: 'en', documentId: document.id });
  if (answer.insufficientEvidence || answer.responseKind !== 'model_extract' || !answer.citations?.length || answer.dimensions < 1) throw new Error('Live grounded answer failed.');
  for (const citation of answer.citations) {
    if (citation.documentId !== document.id || citation.version !== 1 || !manual.includes(citation.quote)) throw new Error('Citation not grounded in actual document.');
    const source = await request(`/knowledge/documents/${document.id}/chunks/${citation.chunkId}`);
    if (!source.text.includes(citation.quote)) throw new Error('Authorized source preview mismatch.');
  }
  const missing = await request('/knowledge/chat', { message: 'What is the company policy for reimbursing interplanetary spaceship fuel?', language: 'en', documentId: document.id });
  if (!missing.insufficientEvidence || missing.citations.length) throw new Error('Unsupported question was not rejected.');
  const run = await request('/ai/agents/runs', { mode: 'inventoryknowledge' });
  const combined = await request(`/ai/agents/runs/${run.id}/execute`, { message: 'Analyze inventory and explain the documented replenishment policy.', language: 'en', period: 'last30days' });
  if (combined.intent !== 'inventoryknowledge' || !combined.inventory || !combined.knowledge || combined.knowledge.insufficientEvidence || !combined.knowledge.citations.length || combined.orderDraft || combined.proposals.length) throw new Error('Combined read-only knowledge and inventory workflow failed.');
  for (const citation of combined.knowledge.citations) {
    const source = await request(`/knowledge/documents/${citation.documentId}/chunks/${citation.chunkId}`);
    if (citation.version !== source.version || !source.text.includes(citation.quote)) throw new Error('Combined citation verification failed.');
  }
  const audit = await request(`/ai/agents/runs/${run.id}`);
  if (audit.execution.outcome !== 'success' || audit.steps.map(s => s.agent).join(',') !== 'orchestrator,inventory,knowledge' || !audit.execution.providerCalls) throw new Error('Combined execution audit failed.');
  const result = { liveRagVerified: true, provider: provider.provider, chatModel: provider.model, embeddingModel: answer.embeddingModel, dimensions: answer.dimensions,
    documentId: document.id, responseKind: answer.responseKind, citations: answer.citations.length, retrievalMs: answer.retrievalMs, llmMs: answer.llmMs, totalTokens: answer.totalTokens,
    missingEvidenceVerified: true, combinedExecutionId: run.id, combinedResponseKind: combined.knowledge.responseKind, combinedTokens: audit.execution.totalTokens,
    evaluationScenariosPassed: 3, evaluationScenariosTotal: 3, citedQuotesVerified: answer.citations.length + combined.knowledge.citations.length };
  write(JSON.stringify(result)); return result;
}
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  runRagLiveSmoke().catch(error => { console.error(error.message.startsWith('Indexing failed: knowledge') ? error.message : 'Live RAG verification failed. Inspect safe local backend categories; no credentials or document text are printed.'); process.exitCode = 1; });
}
