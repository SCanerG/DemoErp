import test from 'node:test';
import assert from 'node:assert/strict';
import { runProactiveLiveSmoke } from '../proactive-live-smoke.mjs';
test('proactive live verification requires opt-in and makes no implicit requests', async () => { const logs = []; assert.deepEqual(await runProactiveLiveSmoke({}, text => logs.push(text)), { skipped:true }); assert.equal(logs.length, 1); });
test('proactive live verification requires separate synthetic fixture authorization', async () => { await assert.rejects(runProactiveLiveSmoke({ PROACTIVE_API_LIVE_SMOKE:'true' }), /fixture opt-in/); });
test('proactive live verification rejects remote origins and credentials in URL', async () => {
  for (const API_URL of ['https://example.com', 'http://user:password@127.0.0.1:5116', 'http://127.0.0.1:5116/api']) await assert.rejects(runProactiveLiveSmoke({ PROACTIVE_API_LIVE_SMOKE:'true', PROACTIVE_ALLOW_SYNTHETIC_FIXTURES:'true', API_URL }), /loopback/);
});
test('proactive live verification requires existing local credentials', async () => { await assert.rejects(runProactiveLiveSmoke({ PROACTIVE_API_LIVE_SMOKE:'true', PROACTIVE_ALLOW_SYNTHETIC_FIXTURES:'true' }), /credentials/); });
