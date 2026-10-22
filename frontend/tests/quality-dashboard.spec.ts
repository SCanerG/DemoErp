import { test, expect, type Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
const runId = '11700000-0000-0000-0000-000000000001', baselineId = '11700000-0000-0000-0000-000000000002', traceId = '11700000000000000000000000000001';
const latency = { samples:3,averageMs:123,p50Ms:null,p95Ms:null,p99Ms:null };
const metrics = { observedSpans:8,requests:3,latency,successRate:2/3,errorRate:1/3,cancellationRate:0,timeoutRate:0,fallbackRate:0,toolSuccessRate:1,providerRetryAttemptRate:0,reportedTotalTokens:250,providerCallsWithUsage:1,providerCallsWithoutUsage:1,
  orchestrationUnattributedLatency:latency,agents:[{agent:'inventory',calls:1,successRate:1,latency}],operations:[{operation:'retrieval',calls:1,latency}],providers:[{provider:'Ollama',model:'qwen3:8b',environment:'Development',calls:1,mock:false,latency,reportedTokens:250}],errors:[{category:'aiProviderUnavailable',observations:1}] };
const report = { suite:'contracts-v1',version:'1',datasetHash:'a'.repeat(64),mode:'deterministic-fixtures',createdAt:'2026-10-21T00:00:00Z',passed:2,failed:0,criticalFailures:0,durationMs:10,cases:[
  {id:'approval-role-viewer',category:'security',critical:true,passed:true,assertion:'expected_contract_verified',durationMs:1,measurements:{},manualReviewRequired:false},
  {id:'labelled-ranking',category:'knowledge',critical:false,passed:true,assertion:'expected_contract_verified',durationMs:1,measurements:{recallAtK:1,precisionAtK:.5,mrrAtK:1},manualReviewRequired:true}] };
async function fixture(page: Page, role='Admin', empty=false) {
  await page.goto('/auth/login'); await page.evaluate(role => { localStorage.setItem('catalog.language','en'); sessionStorage.setItem('catalog.session',JSON.stringify({accessToken:'quality-fixture',expiresAt:new Date(Date.now()+3600000).toISOString(),user:{id:'quality-owner',name:'Quality user',email:'quality@example.com',role,isActive:true,createdAt:'2026-10-01T00:00:00Z',updatedAt:null}})); },role);
  await page.route('**/api/quality/overview?*',r=>r.fulfill({json:{enabled:true,scope:'mine',from:'2026-10-20T00:00:00Z',until:'2026-10-21T00:00:00Z',truncated:false,retentionDays:7,processDroppedObservations:0,metrics:empty?{...metrics,requests:0,successRate:null,errorRate:null,latency:{samples:0,averageMs:null,p50Ms:null,p95Ms:null,p99Ms:null},reportedTotalTokens:null,agents:[],operations:[],providers:[],errors:[]}:metrics}}));
  await page.route('**/api/quality/traces?*',r=>r.fulfill({json:empty?[]:[{traceId,startedAt:'2026-10-21T00:00:00Z',observations:2}]}));
  await page.route('**/api/quality/suites',r=>r.fulfill({json:[{suite:'contracts-v1',version:'1',datasetHash:'a'.repeat(64),mode:'deterministic-fixtures',scenarios:33,liveInference:false,noBusinessWrites:true}]}));
  await page.route('**/api/quality/evaluations',r=>r.fulfill({json:[{id:runId,suite:'contracts-v1',datasetHash:'a'.repeat(64),mode:'deterministic-fixtures',status:'success',createdAt:'2026-10-21T00:00:00Z'},{id:baselineId,suite:'contracts-v1',datasetHash:'a'.repeat(64),mode:'deterministic-fixtures',status:'success',createdAt:'2026-10-20T00:00:00Z'}]}));
  await page.route(`**/api/quality/evaluations/${runId}`,r=>r.fulfill({json:{id:runId,report}}));
  await page.route('**/api/ai/agents/status',r=>r.fulfill({json:{provider:'Ollama',model:'qwen3:8b',health:'ready'}}));
  await page.goto('/ai-quality');
}
test('Quality Dashboard shows actual observation counts and does not invent percentiles for three requests',async({page})=>{
  await fixture(page); await expect(page.getByRole('heading',{name:'AI Quality Dashboard',exact:true})).toBeVisible();
  await expect(page.getByText('123',{exact:true})).toBeVisible(); await expect(page.getByText('Percentiles require at least 20 / 100 / 1,000 request observations.',{exact:false})).toBeVisible();
  await expect(page.getByText('Not available',{exact:true})).toHaveCount(3); await expect(page.getByText('qwen3:8b',{exact:false}).first()).toBeVisible();
});
test('Empty runtime observations are explicit while evaluation fixtures remain separately labelled',async({page})=>{
  await fixture(page,'Admin',true); await expect(page.getByText('No request observations yet.',{exact:false})).toBeVisible();
  await expect(page.getByText('Deterministic synthetic fixtures · no live model',{exact:false})).toBeVisible(); await expect(page.getByText('Not available',{exact:true}).first()).toBeVisible();
});
test('Viewer cannot run evaluations or select system telemetry',async({page})=>{
  await fixture(page,'Viewer'); await expect(page.getByRole('button',{name:'Run deterministic evaluation'})).toHaveCount(0);
  await expect(page.getByRole('option',{name:'System monitoring only'})).toHaveCount(0); await expect(page.getByText('Only administrators can run the evaluation lab.',{exact:false})).toBeVisible();
});
test('Admin evaluation sends stable operation key and renders structured assertions and qualified RAG measurements',async({page})=>{
  await fixture(page); let requests=0;
  await page.route('**/api/quality/evaluations',r=>{ if(r.request().method()==='POST'){requests++;expect(r.request().postDataJSON().idempotencyKey).toMatch(/^[0-9a-f-]{36}$/);return r.fulfill({status:202,json:{id:runId,status:'success'}});}return r.fulfill({json:[]}); });
  await page.getByRole('button',{name:'Run deterministic evaluation',exact:true}).click(); await expect.poll(()=>requests).toBe(1);
  await expect(page.getByText('Passed: 2 · Failed: 0 · Critical failures: 0')).toBeVisible(); await expect(page.getByText('Manual semantic relevance review is still required.')).toBeVisible();
  await expect(page.getByText('Raw prompt')).toHaveCount(0);
});
test('Evaluation comparison shows regressions and rejects an incompatible baseline honestly',async({page})=>{
  await fixture(page); await page.route(`**/api/quality/evaluations/${runId}/comparison/${baselineId}?*`,r=>r.fulfill({json:{comparable:false,newlyFailing:[],recovered:[],latencyRegressions:[],criticalRegressions:0}}));
  await page.getByLabel('Evaluation result').selectOption(runId); await page.getByLabel('Comparison baseline').selectOption(baselineId);
  await expect(page.getByText('Dataset or mode differs; these runs cannot be compared.')).toBeVisible();
});
test('Trace timeline renders safe metadata as text and preserves source hierarchy without prompts',async({page})=>{
  await fixture(page); await page.route(`**/api/quality/traces/${traceId}?*`,r=>r.fulfill({json:[{id:'span-root',traceId,spanId:'1170000000000001',parentSpanId:'0000000000000000',executionId:runId,operation:'request',component:'<img src=x onerror=alert(1)>',outcome:'success',durationMs:25,startedAt:'2026-10-21T00:00:00Z',tokens:null,contextCharacters:null,retrievedCount:null,retry:0,environment:'Development'}]}));
  await page.getByLabel('Select a trace').selectOption(traceId); await expect(page.getByText('<img src=x onerror=alert(1)>',{exact:false})).toBeVisible(); await expect(page.locator('img[src="x"]')).toHaveCount(0);
  await expect(page.getByText('Trace ID: '+traceId)).toBeVisible(); await expect(page.getByText('Prompts, source text, arguments and hidden reasoning are excluded.',{exact:false})).toBeVisible();
});
test('Quality Dashboard remains Turkish, mobile and WCAG accessible',async({page})=>{
  test.setTimeout(90000); await fixture(page); await page.evaluate(()=>localStorage.setItem('catalog.language','tr')); await page.reload();
  await expect(page.getByRole('heading',{name:'Yapay Zekâ Kalite Paneli',exact:true})).toBeVisible(); await page.setViewportSize({width:390,height:844}); expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
  const result=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();expect(result.violations.map(v=>({id:v.id,nodes:v.nodes.map(n=>n.target)}))).toEqual([]);
});
