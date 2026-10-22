import test from 'node:test';
import assert from 'node:assert/strict';
import { runQualityLiveSmoke } from '../ai-quality-live-smoke.mjs';
test('quality live runner has no implicit inference or fixture writes', async () => assert.deepEqual(await runQualityLiveSmoke({},()=>{}),{skipped:true}));
test('quality live runner requires separate isolated fixture opt-in', async () => assert.rejects(runQualityLiveSmoke({AI_QUALITY_LIVE_SMOKE:'true'}),/fixture/));
test('quality live runner rejects remote endpoints paths and URL credentials', async () => {
  for (const API_URL of ['https://example.com','http://127.0.0.1:5118/api','http://x:y@127.0.0.1:5118']) await assert.rejects(runQualityLiveSmoke({AI_QUALITY_LIVE_SMOKE:'true',AI_QUALITY_ALLOW_SYNTHETIC_FIXTURES:'true',API_URL}),/loopback/);
});
test('quality live runner never requests credentials through chat', async () => assert.rejects(runQualityLiveSmoke({AI_QUALITY_LIVE_SMOKE:'true',AI_QUALITY_ALLOW_SYNTHETIC_FIXTURES:'true'}),/credentials/));
