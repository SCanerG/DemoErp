import { readFile, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
export function validateReport(report) {
  if (report?.suite !== 'contracts-v1' || report.version !== '1' || report.mode !== 'deterministic-fixtures' || !/^[A-Fa-f0-9]{64}$/.test(report.datasetHash) || !Array.isArray(report.cases) || report.cases.length < 1 || report.cases.length > 100) throw new Error('Invalid evaluation report schema.');
  const ids = new Set();
  for (const item of report.cases) {
    if (typeof item.id !== 'string' || !/^[a-zA-Z0-9._-]{1,64}$/.test(item.id) || ids.has(item.id) || typeof item.passed !== 'boolean' || typeof item.critical !== 'boolean' || !Number.isFinite(item.durationMs) || item.durationMs < 0) throw new Error('Invalid evaluation scenario.');
    ids.add(item.id);
  }
  if (report.passed !== report.cases.filter(c => c.passed).length || report.failed !== report.cases.filter(c => !c.passed).length || report.criticalFailures !== report.cases.filter(c => !c.passed && c.critical).length) throw new Error('Inconsistent evaluation totals.');
  return report;
}
export function compareReports(current, previous, ratio = 2, minimumIncreaseMs = 20) {
  validateReport(current); validateReport(previous);
  if (!Number.isFinite(ratio) || ratio < 1.1 || ratio > 10 || !Number.isFinite(minimumIncreaseMs) || minimumIncreaseMs < 1 || minimumIncreaseMs > 60000) throw new Error('Invalid comparison thresholds.');
  if (current.datasetHash !== previous.datasetHash || current.mode !== previous.mode || current.suite !== previous.suite) return { comparable:false, newlyFailing:[], recovered:[], latencyRegressions:[], criticalRegressions:0 };
  const prior = new Map(previous.cases.map(c => [c.id,c])); const newFailures = current.cases.filter(c => !c.passed && prior.get(c.id)?.passed);
  return { comparable:true, newlyFailing:newFailures.map(c => c.id), recovered:current.cases.filter(c => c.passed && prior.get(c.id)?.passed === false).map(c => c.id),
    latencyRegressions:current.cases.filter(c => { const p = prior.get(c.id); return p && p.durationMs > 0 && c.durationMs / p.durationMs >= ratio && c.durationMs - p.durationMs >= minimumIncreaseMs; }).map(c => c.id), criticalRegressions:newFailures.filter(c => c.critical).length };
}
export async function verifyEvaluation(directory, env = process.env, write = console.log) {
  const report = validateReport(JSON.parse(await readFile(join(directory, 'evaluation-report.json'), 'utf8')));
  // Critical authorization/approval failures fail CI even if another scenario's output looks successful.
  if (report.criticalFailures || report.failed) throw new Error('Deterministic AI evaluation failed.');
  let comparison = null;
  if (env.AI_EVAL_BASELINE) {
    const previous = JSON.parse(await readFile(env.AI_EVAL_BASELINE, 'utf8'));
    comparison = compareReports(report, previous, Number(env.AI_EVAL_LATENCY_RATIO ?? 2), Number(env.AI_EVAL_MINIMUM_INCREASE_MS ?? 20));
    await writeFile(join(directory,'evaluation-comparison.json'),JSON.stringify(comparison,null,2));
    if (!comparison.comparable || comparison.criticalRegressions) throw new Error('Evaluation baseline is incompatible or has a critical regression.');
    if (env.AI_EVAL_BLOCK_LATENCY === 'true' && comparison.latencyRegressions.length) throw new Error('Configured evaluation latency threshold exceeded.');
  }
  const result = { suite:report.suite, mode:report.mode, scenarios:report.cases.length, passed:report.passed, criticalFailures:report.criticalFailures, comparison };
  write(JSON.stringify(result)); return result;
}
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) verifyEvaluation(process.argv[2] ?? 'artifacts/ai-evaluation').catch(() => { console.error('AI evaluation gate failed. Inspect the machine-readable synthetic reports.'); process.exitCode = 1; });
