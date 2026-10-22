import { test } from 'node:test';
import assert from 'node:assert/strict';
import { runRagLiveSmoke } from '../ai-rag-live-smoke.mjs';
test('RAG live smoke is strictly opt-in and makes no network request by default', async () => {
  const original = global.fetch; let calls = 0; global.fetch = () => { calls++; throw new Error('Forbidden network'); };
  try { assert.deepEqual(await runRagLiveSmoke({}, () => {}), { skipped: true }); assert.equal(calls, 0); } finally { global.fetch = original; }
});
test('RAG live smoke rejects external and credential-bearing URLs before login', async () => {
  for (const API_URL of ['https://example.com', 'http://127.0.0.1@evil.test', 'http://localhost/path', 'http://localhost?redirect=evil']) await assert.rejects(runRagLiveSmoke({ AI_RAG_LIVE_SMOKE: 'true', API_URL }, () => {}), /loopback/);
});
test('RAG live smoke requires explicit existing credentials', async () => { await assert.rejects(runRagLiveSmoke({ AI_RAG_LIVE_SMOKE: 'true' }, () => {}), /credentials/); });
test('RAG smoke validates exact source excerpts, missing evidence and coordinated read-only results with intercepted HTTP', async () => {
  const original = global.fetch; const printed = []; let question = 0;
  const quote = 'A new order stays Pending until a separate authorized confirmation.';
  const citation = { documentId: 'document', version: 1, chunkId: 'chunk', quote };
  const answer = { answer: 'Verified excerpt', citations: [citation], responseKind: 'model_extract', dimensions: 768, embeddingModel: 'nomic-embed-text', retrievalMs: 10, llmMs: 20, totalTokens: 100, insufficientEvidence: false };
  global.fetch = async (url, options) => {
    assert.equal(options.redirect, 'error'); let data; const path = url.pathname;
    if (path === '/api/auth/login') data = { accessToken: 'private-test-token' };
    else {
      assert.equal(options.headers.Authorization, 'Bearer private-test-token');
      if (path === '/api/knowledge/status') data = { enabled: true, vectorAvailable: true };
      else if (path === '/api/ai/agents/status') data = { provider: 'Ollama', model: 'qwen3:8b', health: 'ready' };
      else if (path === '/api/knowledge/documents') { assert.ok(options.body instanceof FormData); data = { id: 'document' }; }
      else if (path === '/api/knowledge/documents/document') data = { status: 'indexed' };
      else if (path === '/api/knowledge/chat') data = ++question === 1 ? answer : { insufficientEvidence: true, citations: [] };
      else if (path.endsWith('/chunks/chunk')) data = { text: quote, version: 1 };
      else if (path === '/api/ai/agents/runs') data = { id: 'run' };
      else if (path.endsWith('/execute')) data = { intent: 'inventoryknowledge', inventory: {}, knowledge: answer, orderDraft: null, proposals: [] };
      else if (path === '/api/ai/agents/runs/run') data = { execution: { outcome: 'success', providerCalls: 1, totalTokens: 100 }, steps: ['orchestrator', 'inventory', 'knowledge'].map(agent => ({ agent })) };
      else throw new Error('Unexpected path');
    }
    return new Response(JSON.stringify(data), { status: 200 });
  };
  try {
    const result = await runRagLiveSmoke({ AI_RAG_LIVE_SMOKE: 'true', AI_LIVE_EMAIL: 'test@example.com', AI_LIVE_PASSWORD: 'private-test-password' }, text => printed.push(text));
    assert.equal(result.liveRagVerified, true); assert.equal(result.evaluationScenariosPassed, 3); assert.equal(result.citedQuotesVerified, 2);
    assert.ok(!printed.join('').includes(quote)); assert.ok(!printed.join('').includes('private-test-password')); assert.ok(!printed.join('').includes('private-test-token'));
  } finally { global.fetch = original; }
});
