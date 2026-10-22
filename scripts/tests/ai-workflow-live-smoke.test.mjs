import { test } from 'node:test';
import assert from 'node:assert/strict';
import { runWorkflowLiveSmoke } from '../ai-workflow-live-smoke.mjs';
test('workflow write smoke never connects without both explicit flags', async () => {
  const original = global.fetch; let calls = 0; global.fetch = () => { calls++; throw new Error('Network forbidden'); };
  try {
    assert.deepEqual(await runWorkflowLiveSmoke({}, () => {}), { skipped: true });
    await assert.rejects(runWorkflowLiveSmoke({ AI_WORKFLOW_LIVE_SMOKE: 'true' }, () => {}), /ALLOW_WRITE/);
    assert.equal(calls, 0);
  } finally { global.fetch = original; }
});
test('workflow write smoke rejects nonlocal destinations before sending credentials', async () => {
  for (const API_URL of ['https://example.com', 'http://127.0.0.1@evil.test', 'http://127.0.0.1/path', 'http://localhost?target=evil']) {
    await assert.rejects(runWorkflowLiveSmoke({ AI_WORKFLOW_LIVE_SMOKE: 'true', AI_WORKFLOW_ALLOW_WRITE: 'true', API_URL }, () => {}), /loopback/);
  }
});
test('workflow write smoke requires explicit existing ERP credentials', async () => {
  await assert.rejects(runWorkflowLiveSmoke({ AI_WORKFLOW_LIVE_SMOKE: 'true', AI_WORKFLOW_ALLOW_WRITE: 'true' }, () => {}), /credentials/);
});
test('workflow runner verifies the exact purchase and reuses its approval key with intercepted HTTP', async () => {
  const original = global.fetch; const keys = []; const printed = []; let approved = false;
  const proposal = { id: 'proposal', kind: 'purchase', state: 'pending', fingerprint: 'a'.repeat(64), facts: { counterpartyId: 'supplier', lines: [{ productId: 'product', quantity: 2, unitPrice: 7 }] } };
  global.fetch = async (url, options) => {
    assert.equal(options.redirect, 'error'); const path = url.pathname;
    let data; let status = 200;
    if (path === '/api/auth/login') data = { accessToken: 'test-session-token', user: { id: 'user', role: 'Admin' } };
    else {
      assert.equal(options.headers.Authorization, 'Bearer test-session-token');
      if (path === '/api/ai/agents/status') data = { provider: 'Ollama', isMock: false, health: 'ready', model: 'qwen3:8b' };
      else if (path === '/api/orders' || path === '/api/inventory') data = [];
      else if (path === '/api/ai/agents/runs') { data = { id: 'run' }; status = 201; }
      else if (path.endsWith('/execute')) data = { intent: 'procurement', sales: {}, inventory: {}, responseKind: 'model', proposals: [proposal] };
      else if (path === '/api/ai/agents/runs/run') data = { execution: { outcome: 'success', provider: 'Ollama', providerCalls: 1, totalTokens: 123, durationMs: 20 }, steps: ['orchestrator', 'sales', 'inventory', 'order', 'summary'].map(agent => ({ agent, outcome: 'success' })) };
      else if (path === '/api/purchase-orders') data = { totalCount: approved ? 1 : 0 };
      else if (path.endsWith('/approve')) {
        const body = JSON.parse(options.body); assert.deepEqual(Object.keys(body).sort(), ['fingerprint', 'idempotencyKey']); assert.equal(body.fingerprint, proposal.fingerprint); keys.push(body.idempotencyKey); approved = true;
        data = { ...proposal, state: 'approved', resultVerified: true, resultId: 'purchase', resultStatus: 'Draft' };
      } else if (path === '/api/purchase-orders/purchase') data = { id: 'purchase', supplierId: 'supplier', status: 'Draft', items: [{ productId: 'product', quantityOrdered: 2, unitCost: 7 }] };
      else if (path === '/api/audit-logs') data = { items: [{ entityId: 'purchase', userId: 'user' }] };
      else if (path.endsWith('/proposals')) data = [{ ...proposal, resultId: 'purchase', resultVerified: true }];
      else throw new Error('Unexpected fixture path');
    }
    return new Response(JSON.stringify(data), { status });
  };
  try {
    const result = await runWorkflowLiveSmoke({ AI_WORKFLOW_LIVE_SMOKE: 'true', AI_WORKFLOW_ALLOW_WRITE: 'true', AI_LIVE_EMAIL: 'test@example.com', AI_LIVE_PASSWORD: 'test-password' }, text => printed.push(text));
    assert.equal(result.liveWorkflowVerified, true); assert.equal(keys.length, 2); assert.equal(keys[0], keys[1]);
    assert.ok(!printed.join('').includes('test-password')); assert.ok(!printed.join('').includes('test-session-token'));
  } finally { global.fetch = original; }
});
