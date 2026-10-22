import test from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';
import { runLiveSmoke } from '../ai-live-smoke.mjs';

test('live smoke is disabled by default and makes no HTTP call', async () => {
  const lines=[];assert.deepEqual(await runLiveSmoke({}, line=>lines.push(line)),{skipped:true});assert.match(lines[0],/^SKIP:/);
});
test('live smoke rejects insecure remote origins before sending credentials', async () => {
  await assert.rejects(()=>runLiveSmoke({AI_LIVE_SMOKE:'true',API_URL:'http://remote.invalid',AI_LIVE_EMAIL:'synthetic',AI_LIVE_PASSWORD:'synthetic'},()=>{}),/HTTPS/);
});
for (const provider of ['OpenAI','Ollama']) test(`${provider} live smoke validates auth, source and persisted metadata against an intercepted local API`, async () => {
  let posts=0;const observed=[];
  const server=http.createServer(async (req,res)=>{
    let text='';for await(const chunk of req)text+=chunk;
    observed.push({method:req.method,path:req.url});res.setHeader('Content-Type','application/json');
    if(req.method==='POST')posts++;
    const body=req.url==='/api/auth/login'?{accessToken:'synthetic-session'}:
      req.url==='/api/ai/status'?{available:true,isMock:false,provider}:
      req.url?.startsWith('/api/reports/inventory')?{items:[],totalCount:0}:
      req.url==='/api/ai/chat'?{isMock:false,language:'tr',answer:'No matching records (synthetic test fixture)',sources:[{name:'get_low_stock_products',reportPath:'/reports/inventory'}],executionId:'11111111-1111-1111-1111-111111111111',requestId:'fixture-correlation'}:
      {id:'11111111-1111-1111-1111-111111111111',provider,model:'test-model',outcome:'success',correlationId:'fixture-correlation',totalTokens:10,tools:[{name:'get_low_stock_products',outcome:'success'}]};
    if(req.url==='/api/ai/chat')assert.deepEqual(JSON.parse(text),{message:'Which products currently have low inventory?',language:'tr',...(provider === 'Ollama' ? {agent:'inventory'} : {})});
    res.end(JSON.stringify(body));
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  try {
    const port=server.address().port;const lines=[];
    const result=await runLiveSmoke({AI_LIVE_SMOKE:'true',AI_LIVE_PROVIDER:provider,API_URL:`http://127.0.0.1:${port}`,AI_LIVE_EMAIL:'synthetic@example.com',AI_LIVE_PASSWORD:'synthetic-local-test-password'},line=>lines.push(line));
    assert.equal(result.liveProviderVerified,true);assert.equal(result.actualLowStockRowsAtPrecheck,0);assert.equal(posts,2);assert.equal(observed.length,5);
    assert.ok(!lines.join('').includes('synthetic-local-test-password'));
  } finally {await new Promise(resolve=>server.close(resolve));}
});

test('live smoke rejects unsupported providers before HTTP', async()=>{
  await assert.rejects(()=>runLiveSmoke({AI_LIVE_PROVIDER:'Untrusted'},()=>{}),/Unsupported/);
});
