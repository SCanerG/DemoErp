import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { compareReports, validateReport, verifyEvaluation } from '../verify-ai-evaluation.mjs';
const report = () => ({ suite:'contracts-v1',version:'1',mode:'deterministic-fixtures',datasetHash:'a'.repeat(64),passed:1,failed:0,criticalFailures:0,cases:[{id:'authorization',category:'security',critical:true,passed:true,durationMs:10}] });
test('evaluation report validates shape totals and unique IDs', () => {
  assert.equal(validateReport(report()).passed,1); assert.throws(() => validateReport({})); assert.throws(() => validateReport({...report(),passed:2})); assert.throws(() => validateReport({...report(),cases:[...report().cases,...report().cases]}));
});
test('critical regression recovery and latency are distinct', () => {
  const prior = report(); const current = {...prior,passed:0,failed:1,criticalFailures:1,cases:[{...prior.cases[0],passed:false,durationMs:100}]};
  const change = compareReports(current,prior); assert.deepEqual(change.newlyFailing,['authorization']); assert.equal(change.criticalRegressions,1); assert.deepEqual(change.latencyRegressions,['authorization']);
  assert.deepEqual(compareReports(prior,current).recovered,['authorization']); assert.equal(compareReports(prior,{...prior,datasetHash:'b'.repeat(64)}).comparable,false); assert.throws(() => compareReports(prior,prior,0));
});
test('CI verifier writes comparison and rejects critical failures without model or business access', async () => {
  const directory = await mkdtemp(join(tmpdir(),'demoerp-quality-gate-'));
  try {
    const current = report(); await writeFile(join(directory,'evaluation-report.json'),JSON.stringify(current)); await writeFile(join(directory,'baseline.json'),JSON.stringify(current));
    const result = await verifyEvaluation(directory,{AI_EVAL_BASELINE:join(directory,'baseline.json')},()=>{}); assert.equal(result.passed,1); assert.equal(result.comparison.comparable,true);
    await writeFile(join(directory,'evaluation-report.json'),JSON.stringify({...current,passed:0,failed:1,criticalFailures:1,cases:[{...current.cases[0],passed:false}]})); await assert.rejects(verifyEvaluation(directory,{},()=>{}),/failed/);
  } finally { await rm(directory,{recursive:true,force:true}); }
});
